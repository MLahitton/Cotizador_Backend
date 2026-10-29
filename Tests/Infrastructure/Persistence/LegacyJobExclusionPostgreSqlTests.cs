using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class LegacyJobExclusionPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private JobTerminationTestSupport H => new(fixture);
    internal sealed record LegacySeed(Guid Requirement, Guid Actor, Guid Quote);

    private async Task<LegacySeed> Seed()
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        var user = ProcessPublicExperienceSubmissionTests.Owner();
        var client = Client.Create(ClientType.Person, "Legacy", null, null, null, null, null, null, null, user.Id, at);
        var project = Project.Create(client.Id, "LEG", "Legacy", null, null, user.Id, at);
        var quote = PreQuote.Create(project.Id, user.Id, "PC-2026-0001", null, at);
        var requirement = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, at);
        db.AddRange(user, client, project, quote, requirement,
            RequirementFile.Create(requirement.Id, "test.pdf", "application/pdf", 10, "stub.pdf", at));
        await db.SaveChangesAsync(Token);
        return new(requirement.Id, user.Id, quote.Id);
    }

    private (ProcessRequirementCoreService Core, IAi2DocumentProcessingClient Provider, IOperationCancellationRegistry Registry)
        Core(ApplicationDbContext db)
    {
        var repository = new RequirementRepository(db);
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(new byte[10])));
        var provider = Substitute.For<IAi2DocumentProcessingClient>();
        provider.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => DocumentProcessingClientResult.Success(ProcessRequirementServiceTests.CreateResponse(call.Arg<DocumentProcessingClientRequest>()!)));
        var registry = Substitute.For<IOperationCancellationRegistry>();
        registry.Register(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<CancellationToken>());
        return (new(new IdentityRepository(db), new PreQuoteRepository(db), new ProjectRepository(db), new ClientRepository(db),
            repository, new RequirementProcessingLock(db), storage, provider, registry,
            PrepareJobPublicationTests.Builder(repository), TimeProvider.System, NullLogger<ProcessRequirementService>.Instance), provider, registry);
    }

    private CancelRequirementProcessingAttemptService Cancel(ApplicationDbContext db, Guid actor, IOperationCancellationRegistry registry)
    {
        var current = Substitute.For<ICurrentUser>();
        current.IsAuthenticated.Returns(true);
        current.UserId.Returns(actor);
        return new(current, new IdentityRepository(db), new PreQuoteRepository(db), new ProjectRepository(db),
            new RequirementRepository(db), registry, TimeProvider.System);
    }

    [Theory]
    [InlineData("Queued")] [InlineData("Running")] [InlineData("Succeeded")] [InlineData("Failed")] [InlineData("Cancelled")]
    public async Task EveryHistoryState_BlocksCoreCancelAndInternalFinalizers_WithoutWritesOrSignals(string state)
    {
        var s = await H.Create(running: state != "Queued", attempt: state == "Succeeded");
        await using (var edit = fixture.CreateDbContext())
        {
            var job = await edit.RequirementProcessingJobs.SingleAsync(Token);
            var now = DateTimeOffset.UtcNow;
            if (state == "Succeeded") job.Succeed(now);
            if (state == "Failed") job.Fail("TEST_FAILURE", now);
            if (state == "Cancelled") { job.RequestCancellation(now); job.ConfirmCancellation(now); }
            await edit.SaveChangesAsync(Token);
        }
        var before = await H.Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(H.Options(writes));
        var c = Core(db);
        Assert.Equal(ProcessRequirementFailure.ProcessingManagedByJob, (await c.Core.ExecuteAsync(s.Execution.RequirementId, s.Actor, Token)).Failure);
        var cancel = Cancel(db, s.Actor, c.Registry);
        Assert.Equal(CancelRequirementProcessingAttemptFailure.ProcessingManagedByJob,
            (await cancel.ExecuteAsync(new CancelRequirementProcessingByRequirementCommand(s.Execution.RequirementId), Token)).Failure);
        var repository = new RequirementRepository(db);
        var attempt = s.Execution.ProcessingAttemptId ?? Guid.NewGuid();
        Assert.Equal(ProcessRequirementFailure.ProcessingManagedByJob,
            (await Assert.ThrowsAsync<LegacyProcessingRejectedException>(() => repository.FinalizeProcessingFailureAsync(
                s.Execution.RequirementId, attempt, "TEST_FAILURE", DateTimeOffset.UtcNow, Token, s.Actor))).Failure);
        Assert.Equal(ProcessRequirementFailure.ProcessingManagedByJob,
            (await Assert.ThrowsAsync<LegacyProcessingRejectedException>(() => repository.FinalizeProcessingCancellationAsync(
                s.Execution.RequirementId, attempt, DateTimeOffset.UtcNow, Token, s.Actor))).Failure);
        Assert.Empty(c.Provider.ReceivedCalls());
        Assert.Empty(c.Registry.ReceivedCalls());
        Assert.Equal(0, writes.Count);
        Assert.Equal(before, await H.Snapshot());
        Assert.False(await db.RequirementTechnicalProposals.AnyAsync(Token));
        Assert.False(await db.RequirementExtractionResults.AnyAsync(Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ForeignOwnerAndAdmin_DoNotSeeHistory(bool admin)
    {
        var s = await H.Create();
        await using var db = fixture.CreateDbContext();
        var user = User.CreateFromGoogle("other@test.local", "Other", null, null, DateTimeOffset.UtcNow);
        if (admin) user.ChangeRole(UserRole.Admin, DateTimeOffset.UtcNow);
        db.Add(user);
        await db.SaveChangesAsync(Token);
        var before = await H.Snapshot();
        var c = Core(db);
        Assert.Equal(ProcessRequirementFailure.RequirementNotFound, (await c.Core.ExecuteAsync(s.Execution.RequirementId, user.Id, Token)).Failure);
        Assert.Equal(CancelRequirementProcessingAttemptFailure.ProcessingAttemptNotFound,
            (await Cancel(db, user.Id, c.Registry).ExecuteAsync(new CancelRequirementProcessingByRequirementCommand(s.Execution.RequirementId), Token)).Failure);
        Assert.Empty(c.Registry.ReceivedCalls());
        Assert.Empty(c.Provider.ReceivedCalls());
        Assert.Equal(before, await H.Snapshot());
    }

    [Fact]
    public async Task NoHistory_StartIsCommittedBeforeProvider_AndSuccessIsAtomic()
    {
        var s = await Seed();
        await using var db = fixture.CreateDbContext();
        var c = Core(db);
        c.Provider.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await using var read = fixture.CreateDbContext();
            var attempt = await read.RequirementProcessingAttempts.SingleAsync(Token);
            Assert.Equal(DocumentProcessingState.Processing, attempt.ProcessingState);
            Assert.Equal(RequirementStatus.Processing, (await read.Requirements.SingleAsync(Token)).Status);
            Assert.Equal(call.Arg<DocumentProcessingClientRequest>()!.ProcessingAttemptId, attempt.Id);
            Assert.Null(db.Database.CurrentTransaction);
            return DocumentProcessingClientResult.Success(ProcessRequirementServiceTests.CreateResponse(call.Arg<DocumentProcessingClientRequest>()!));
        });
        var result = await c.Core.ExecuteAsync(s.Requirement, s.Actor, Token);
        Assert.True(result.IsSuccess);
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(RequirementStatus.Processed, (await verify.Requirements.SingleAsync(Token)).Status);
        Assert.Equal(DocumentProcessingState.Finished, (await verify.RequirementProcessingAttempts.SingleAsync(Token)).ProcessingState);
        Assert.Single(await verify.RequirementTechnicalProposals.ToListAsync(Token));
        Assert.Single(await verify.RequirementExtractionResults.ToListAsync(Token));
        Assert.False(await verify.RequirementProcessingJobs.AnyAsync(Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StartCommitException_NeverInvokesProviderOrCompensation(bool committed)
    {
        var s = await Seed();
        await using var db = new ApplicationDbContext(H.Options(new CommitHook(() => throw new IOException("lost response"), committed)));
        var c = Core(db);
        Assert.Equal(ProcessRequirementFailure.PersistenceError, (await c.Core.ExecuteAsync(s.Requirement, s.Actor, Token)).Failure);
        Assert.Empty(c.Provider.ReceivedCalls());
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(committed ? 1 : 0, await verify.RequirementProcessingAttempts.CountAsync(Token));
        Assert.Equal(committed ? RequirementStatus.Processing : RequirementStatus.Pending, (await verify.Requirements.SingleAsync(Token)).Status);
        if (committed) Assert.Equal(DocumentProcessingState.Processing, (await verify.RequirementProcessingAttempts.SingleAsync(Token)).ProcessingState);
        Assert.False(await verify.RequirementTechnicalProposals.AnyAsync(Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task JobInsertedByUncoordinatedWriterDuringProvider_RejectsFinalizationWithoutFallback(bool providerFails)
    {
        var s = await Seed();
        await using var db = fixture.CreateDbContext();
        var c = Core(db);
        string? before = null;
        c.Provider.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await using var other = fixture.CreateDbContext();
            other.Add(RequirementProcessingJob.Create(s.Requirement, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, DateTimeOffset.UtcNow));
            await other.SaveChangesAsync(Token);
            before = await H.Snapshot();
            return providerFails ? DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.Timeout)
                : DocumentProcessingClientResult.Success(ProcessRequirementServiceTests.CreateResponse(call.Arg<DocumentProcessingClientRequest>()!));
        });
        Assert.Equal(ProcessRequirementFailure.ProcessingManagedByJob, (await c.Core.ExecuteAsync(s.Requirement, s.Actor, Token)).Failure);
        Assert.NotNull(before);
        Assert.Equal(before, await H.Snapshot());
        await using var read = fixture.CreateDbContext();
        Assert.False(await read.RequirementTechnicalProposals.AnyAsync(Token));
        Assert.False(await read.RequirementExtractionResults.AnyAsync(Token));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FutureEnqueuerAndLegacyStart_BothLockOrders(bool jobFirst)
    {
        var s = await Seed();
        var held = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal();
        var legacyProbe = new JobProtocolTestSupport.QuoteProbe();
        var enqueueProbe = new JobProtocolTestSupport.QuoteProbe();
        var saved = new Saved(async context =>
        {
            if (jobFirst || !context.ChangeTracker.Entries<RequirementProcessingAttempt>().Any()) return;
            if (held.TrySetResult(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID))
                await release.Task.WaitAsync(Token);
        });
        await using var legacyDb = new ApplicationDbContext(H.Options(saved, legacyProbe));
        var c = Core(legacyDb);
        Task<ProcessRequirementResult>? legacy = null;
        Task<bool>? enqueue = null;
        async Task<bool> EnqueueFixture()
        {
            await using var db = new ApplicationDbContext(H.Options(enqueueProbe));
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await db.PreQuotes.FromSqlInterpolated($"SELECT * FROM core.pre_quotes WHERE id = {s.Quote} FOR UPDATE").SingleAsync(Token);
            await db.Requirements.FromSqlInterpolated($"SELECT * FROM core.requirements WHERE id = {s.Requirement} FOR UPDATE").SingleAsync(Token);
            if (await db.RequirementProcessingAttempts.AnyAsync(x => x.RequirementId == s.Requirement
                    && (x.ProcessingState == DocumentProcessingState.Pending || x.ProcessingState == DocumentProcessingState.Processing), Token)) return false;
            db.Add(RequirementProcessingJob.Create(s.Requirement, Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Token);
            if (jobFirst)
            {
                held.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
                await release.Task.WaitAsync(Token);
            }
            await tx.CommitAsync(Token);
            return true;
        }
        // Keep the provider pending so the enqueuer observes the committed active legacy attempt.
        var providerRelease = Signal();
        c.Provider.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await providerRelease.Task.WaitAsync(Token);
            return DocumentProcessingClientResult.Success(ProcessRequirementServiceTests.CreateResponse(call.Arg<DocumentProcessingClientRequest>()!));
        });
        try
        {
            if (jobFirst) enqueue = EnqueueFixture();
            else legacy = c.Core.ExecuteAsync(s.Requirement, s.Actor, Token);
            var winnerPid = await held.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            if (jobFirst) legacy = c.Core.ExecuteAsync(s.Requirement, s.Actor, Token);
            else enqueue = EnqueueFixture();
            var loserPid = await (jobFirst ? legacyProbe : enqueueProbe).Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(winnerPid, loserPid);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loserPid, winnerPid);
            output.WriteLine($"jobFirst={jobFirst}; winner PID={winnerPid}; loser PID={loserPid}; PostgreSQL Lock and pg_blocking_pids observed before release.");
            release.TrySetResult();
            Assert.Equal(jobFirst, await enqueue!);
            providerRelease.TrySetResult();
            var result = await legacy!;
            Assert.Equal(jobFirst ? ProcessRequirementFailure.ProcessingManagedByJob : ProcessRequirementFailure.None, result.Failure);
            await using var verify = fixture.CreateDbContext();
            Assert.Equal(jobFirst ? 1 : 0, await verify.RequirementProcessingJobs.CountAsync(Token));
            Assert.Equal(jobFirst ? 0 : 1, await verify.RequirementProcessingAttempts.CountAsync(Token));
            if (jobFirst) Assert.Empty(c.Provider.ReceivedCalls());
        }
        finally
        {
            release.TrySetResult();
            providerRelease.TrySetResult();
            await Task.WhenAll(new Task?[] { enqueue, legacy }.OfType<Task>());
        }
    }

    [Fact]
    public async Task CancellationSignal_AfterCommitAndLocksReleased_UsesExactAttempt_NotSuccessor()
    {
        var s = await Seed();
        Guid old;
        var at = DateTimeOffset.UtcNow;
        await using (var setup = fixture.CreateDbContext())
        {
            var requirement = await setup.Requirements.SingleAsync(Token);
            var attempt = RequirementProcessingAttempt.Create(requirement.Id, s.Actor, Guid.NewGuid(), at);
            attempt.Start(at);
            requirement.StartProcessing(at);
            setup.Add(attempt);
            await setup.SaveChangesAsync(Token);
            old = attempt.Id;
        }
        await using var db = fixture.CreateDbContext();
        var registry = Substitute.For<IOperationCancellationRegistry>();
        Guid successor = Guid.Empty;
        registry.TryCancel(Arg.Any<string>()).Returns(call =>
        {
            Assert.Equal(RequirementOperationKeys.ProcessingAttempt(old), call.Arg<string>());
            Assert.Null(db.Database.CurrentTransaction);
            // Reentrant database work would deadlock if cancellation held the writer locks.
            using var other = fixture.CreateDbContext();
            other.Database.SetCommandTimeout(3);
            using var tx = other.Database.BeginTransaction();
            var requirement = other.Requirements.FromSqlInterpolated($"SELECT * FROM core.requirements WHERE id = {s.Requirement} FOR UPDATE").Single();
            Assert.Equal(RequirementStatus.Pending, requirement.Status);
            Assert.Equal(DocumentProcessingOutcome.Cancelled, other.RequirementProcessingAttempts.Single().Outcome);
            var next = RequirementProcessingAttempt.Create(requirement.Id, s.Actor, Guid.NewGuid(), at.AddSeconds(1));
            next.Start(at.AddSeconds(1));
            requirement.StartProcessing(at.AddSeconds(1));
            other.Add(next);
            other.SaveChanges();
            tx.Commit();
            successor = next.Id;
            return true;
        });
        Assert.True((await Cancel(db, s.Actor, registry).ExecuteAsync(new CancelRequirementProcessingAttemptCommand(old), Token)).IsSuccess);
        Assert.NotEqual(Guid.Empty, successor);
        var before = await H.Snapshot();
        var repository = new RequirementRepository(db);
        Assert.Null(await repository.FinalizeProcessingCancellationAsync(s.Requirement, old, at.AddSeconds(2), Token, s.Actor));
        Assert.Null(await repository.FinalizeProcessingFailureAsync(s.Requirement, old, "LATE_FAILURE", at.AddSeconds(2), Token, s.Actor));
        Assert.Equal(before, await H.Snapshot());
        registry.Received(1).TryCancel(RequirementOperationKeys.ProcessingAttempt(old));
        registry.DidNotReceive().TryCancel(RequirementOperationKeys.ProcessingAttempt(successor));
    }

    [Fact]
    public async Task Replacement_DoesNotInheritHistoryFromSameQuote()
    {
        var s = await H.Create();
        Guid replacementId;
        await using (var db = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var old = await db.Requirements.SingleAsync(Token);
            old.StartProcessing(now);
            old.MarkFailed(now);
            var replacement = Requirement.Create(old.PreQuoteId, s.Actor, RequirementCommercialLine.Essential, now);
            old.SupersedeBy(replacement.Id, now);
            replacement.MarkAsReplacementOf(old.Id);
            db.AddRange(replacement, RequirementFile.Create(replacement.Id, "replacement.pdf", "application/pdf", 10, "stub.pdf", now));
            await db.SaveChangesAsync(Token);
            replacementId = replacement.Id;
        }
        await using var run = fixture.CreateDbContext();
        Assert.True((await Core(run).Core.ExecuteAsync(replacementId, s.Actor, Token)).IsSuccess);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(s.Execution.RequirementId, (await read.RequirementProcessingJobs.SingleAsync(Token)).RequirementId);
        Assert.Equal(replacementId, (await read.RequirementProcessingAttempts.SingleAsync(Token)).RequirementId);
    }

    [Theory]
    [InlineData("success")] [InlineData("failure")] [InlineData("cancel")]
    public async Task TerminalCommitResponseLost_DoesNotCompensateOrSignal(string operation)
    {
        var s = await Seed();
        var commits = 0;
        var hook = new CommitHook(() => { if (++commits == 2) throw new IOException("lost terminal response"); });
        await using var db = new ApplicationDbContext(H.Options(hook));
        var c = Core(db);
        if (operation == "failure") c.Provider.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>())
            .Returns(DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.Timeout));
        if (operation == "cancel") c.Provider.ProcessAsync(Arg.Any<DocumentProcessingClientRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<DocumentProcessingClientResult>(new OperationCanceledException()));
        var result = await c.Core.ExecuteAsync(s.Requirement, s.Actor, Token);
        Assert.Equal(ProcessRequirementFailure.PersistenceError, result.Failure);
        Assert.Equal(2, commits);
        c.Registry.DidNotReceive().TryCancel(Arg.Any<string>());
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(operation switch { "success" => RequirementStatus.Processed, "failure" => RequirementStatus.Failed, _ => RequirementStatus.Pending },
            (await verify.Requirements.SingleAsync(Token)).Status);
        var attempt = await verify.RequirementProcessingAttempts.SingleAsync(Token);
        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        if (operation == "success") Assert.Contains(attempt.Outcome, new DocumentProcessingOutcome?[] { DocumentProcessingOutcome.Completed, DocumentProcessingOutcome.RequiresReview });
        else Assert.Equal(operation == "failure" ? DocumentProcessingOutcome.Failed : DocumentProcessingOutcome.Cancelled, attempt.Outcome);
        Assert.Equal(operation == "success" ? 1 : 0, await verify.RequirementTechnicalProposals.CountAsync(Token));
    }

    [Fact]
    public async Task CancellationRequestCommitResponseLost_ReturnsFailureWithoutSignal()
    {
        var s = await Seed();
        await using (var setup = fixture.CreateDbContext())
        {
            var now = DateTimeOffset.UtcNow;
            var requirement = await setup.Requirements.SingleAsync(Token);
            var attempt = RequirementProcessingAttempt.Create(s.Requirement, s.Actor, Guid.NewGuid(), now);
            attempt.Start(now);
            requirement.StartProcessing(now);
            setup.Add(attempt);
            await setup.SaveChangesAsync(Token);
        }
        await using var db = new ApplicationDbContext(H.Options(new CommitHook(() => throw new IOException("lost response"))));
        var registry = Substitute.For<IOperationCancellationRegistry>();
        var result = await Cancel(db, s.Actor, registry).ExecuteAsync(new CancelRequirementProcessingByRequirementCommand(s.Requirement), Token);
        Assert.Equal(CancelRequirementProcessingAttemptFailure.PersistenceError, result.Failure);
        Assert.Null(result.Attempt);
        Assert.Empty(registry.ReceivedCalls());
        await using var verify = fixture.CreateDbContext();
        Assert.Equal(RequirementStatus.Pending, (await verify.Requirements.SingleAsync(Token)).Status);
        Assert.Equal(DocumentProcessingOutcome.Cancelled, (await verify.RequirementProcessingAttempts.SingleAsync(Token)).Outcome);
    }
}
