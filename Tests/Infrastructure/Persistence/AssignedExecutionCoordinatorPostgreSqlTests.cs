using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.Identity;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private CoordinateAssignedExecutionService Coordinator(Files files, Copies copies, ConsumingClient client,
        IInterceptor[]? start = null, IInterceptor[]? documents = null, IInterceptor[]? publication = null,
        IInterceptor[]? termination = null, IExecutionDocumentPreparation? preparation = null) => new(
            new(new AssignedJobStartStore(H.Options(start ?? []))), preparation ?? Store(files, copies, documents ?? []),
            Consumer(files, copies, client),
            new(new(NullLogger.Instance), PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>())),
            new(new JobPublicationStore(H.Options(publication ?? []))), H.Service(termination ?? []));

    private async Task<Seed> WithAttempt(Seed s)
    {
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        return s with { Execution = s.Execution with { ProcessingAttemptId = job.ProcessingAttemptId } };
    }

    private static void Released(Files files, Copies copies, ConsumingClient client)
    {
        Assert.Equal(0, files.Deletes);
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
        Assert.All(client.Readers, r => Assert.False(r.CanRead));
        Assert.Equal("abc"u8.ToArray(), System.IO.File.ReadAllBytes(Path.Combine(files.Root, "documents/0.pdf")));
        Assert.Equal("abc"u8.ToArray(), System.IO.File.ReadAllBytes(Path.Combine(files.Root, "documents/1.pdf")));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Coordinator_Success_PublicManual_Review_AndNoResend(bool publicContext, bool review)
    {
        await using var files = new Files();
        var s = await SeedFiles(files, publicContext, start: false);
        var copies = new Copies(); var client = new ConsumingClient();
        client.Action = (request, _) =>
        {
            var response = ConsumingClient.Response(request).Response!;
            return Task.FromResult(DocumentProcessingClientResult.Success(review
                ? response with { Outcome = DocumentProcessingOutcome.RequiresReview } : response));
        };
        var result = await Coordinator(files, copies, client).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Published, result.Outcome);
        Assert.Equal(JobPublicationState.Published, result.Publication!.State);
        Assert.Equal(PreparedJobPublication.Version2, result.Publication.Receipt!.ContractVersion);
        await AssertRows(1);
        await using (var db = fixture.CreateDbContext())
        {
            var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
            var attempt = await db.RequirementProcessingAttempts.AsNoTracking().SingleAsync(Token);
            var evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().SingleAsync(Token);
            var proposal = await db.RequirementTechnicalProposals.AsNoTracking().SingleAsync(Token);
            Assert.Equal(RequirementProcessingJobState.Succeeded, job.State);
            Assert.Equal(result.Publication.Receipt.ContentHash, job.PublicationContentHash);
            Assert.Equal(job.ProcessingAttemptId, evidence.ProcessingAttemptId);
            Assert.Equal(job.ProcessingAttemptId, attempt.Id);
            Assert.Equal(result.Publication.Receipt.Outcome, attempt.Outcome);
            if (review) Assert.Equal(DocumentProcessingOutcome.RequiresReview, attempt.Outcome);
            Assert.Null(proposal.CommercialConfirmedAtUtc);
            Assert.Equal(RequirementStatus.Processed, (await db.Requirements.SingleAsync(Token)).Status);
            var old = await db.Requirements.SingleAsync(Token);
            var successor = Requirement.Create(old.PreQuoteId, s.Actor, RequirementCommercialLine.Signature, await H.Now());
            old.SupersedeBy(successor.Id, await H.Now());
            db.Add(successor);
            await db.SaveChangesAsync(Token);
        }
        var before = await new JobPublicationTestSupport(fixture).Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var again = await Coordinator(files, new Copies(), client, start: [writes]).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.NotEqual(AssignedExecutionOutcome.Published, again.Outcome);
        Assert.Equal(1, client.Calls); Assert.Equal(2, files.Opens); Assert.Equal(0, writes.Count);
        Assert.Equal(before, await new JobPublicationTestSupport(fixture).Snapshot());
        Assert.All(client.Bytes, b => Assert.Equal("abc"u8.ToArray(), b));
        Released(files, copies, client);
    }

    [Theory]
    [InlineData("foreign")] [InlineData("admin")] [InlineData("inactive")]
    [InlineData("v1")] [InlineData("lease")] [InlineData("caller-attempt")]
    public async Task Coordinator_Rejection_NoStartOrProvider(string mode)
    {
        await using var files = new Files();
        var s = await SeedFiles(files, start: false, verifiedPublication: mode != "v1");
        await using (var db = fixture.CreateDbContext())
        {
            if (mode is "foreign" or "admin")
            {
                var other = User.CreateFromGoogle("coordinator-other@test.local", "Other", null, null, await H.Now());
                if (mode == "admin") other.ChangeRole(UserRole.Admin, await H.Now());
                db.Add(other); s = s with { Actor = other.Id };
            }
            if (mode == "inactive") (await db.Users.SingleAsync(Token)).Deactivate(await H.Now());
            await db.SaveChangesAsync(Token);
        }
        if (mode == "lease") s = s with { Execution = s.Execution with { LeaseOwner = "stale" } };
        if (mode == "caller-attempt") s = s with { Execution = s.Execution with { ProcessingAttemptId = Guid.NewGuid() } };
        var before = await H.Snapshot(); var copies = new Copies(); var client = new ConsumingClient();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var result = await Coordinator(files, copies, client, start: [writes], termination: [writes]).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Rejected, result.Outcome);
        Assert.Null(result.Termination);
        Assert.Equal(before, await H.Snapshot()); Assert.Equal(0, writes.Count);
        Assert.Equal(0, client.Calls); Assert.Equal(0, files.Opens); Released(files, copies, client);
    }

    [Fact]
    public async Task Coordinator_AlreadyStarted_DoesNotResume()
    {
        await using var files = new Files(); var s = await SeedFiles(files);
        var copies = new Copies(); var client = new ConsumingClient(); var before = await H.Snapshot();
        var result = await Coordinator(files, copies, client).ExecuteAsync(s.Execution with { ProcessingAttemptId = null }, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.AlreadyStarted, result.Outcome);
        Assert.Equal(before, await H.Snapshot()); Assert.Equal(0, client.Calls); Assert.Equal(0, files.Opens);
        Released(files, copies, client);
    }

    [Theory]
    [InlineData("before-start")] [InlineData("during-copy")] [InlineData("during-provider")]
    public async Task Coordinator_BusinessCancellation_ConfirmedOnlyByService(string phase)
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var copies = new Copies(); var client = new ConsumingClient(); var requested = false;
        async Task Request()
        {
            if (requested) return;
            requested = true;
            Assert.Equal(JobTerminationState.CancellationRequested, (await H.Call("request", await WithAttempt(s))).State);
        }
        if (phase == "before-start") await Request();
        if (phase == "during-copy") copies.AfterCopy = Request;
        if (phase == "during-provider") client.DuringRead = Request;
        var result = await Coordinator(files, copies, client).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.True(requested);
        Assert.Equal(AssignedExecutionOutcome.Cancelled, result.Outcome);
        Assert.Equal(JobTerminationState.CancellationConfirmed, result.Termination!.State);
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Cancelled, job.State);
        Assert.Null(job.PublicationContentHash); Assert.Null(job.LeaseOwner);
        Assert.Equal(phase == "during-provider" ? 1 : 0, client.Calls);
        Assert.Equal(RequirementStatus.Pending, (await db.Requirements.SingleAsync(Token)).Status);
        Assert.Equal(phase == "before-start" ? 0 : 1, await db.RequirementProcessingAttempts.CountAsync(Token));
        Assert.Empty(await db.RequirementTechnicalProposals.ToListAsync(Token));
        Released(files, copies, client);
    }

    [Fact]
    public async Task Coordinator_CallerCancellation_DoesNotRequestOrClose()
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var copies = new Copies(); var client = new ConsumingClient { IgnoreCancellation = true };
        client.DuringRead = () => { caller.Cancel(); return Task.CompletedTask; };
        var writes = new JobProtocolTestSupport.WriteProbe();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            Coordinator(files, copies, client, termination: [writes]).ExecuteAsync(s.Execution, s.Actor, caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.FinishedAtUtc);
        Assert.Equal(0, writes.Count); Assert.Equal(1, client.Calls);
        Assert.Empty(await db.RequirementTechnicalProposals.ToListAsync(Token)); Released(files, copies, client);
    }

    [Theory]
    [InlineData("source")] [InlineData("provider")]
    public async Task Coordinator_KnownFailure_FinalizesWithExistingCode(string phase)
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var copies = new Copies(); var client = new ConsumingClient();
        if (phase == "source") files.BeforeOpen = _ => throw new IOException("controlled source failure");
        else client.Action = (_, _) => Task.FromResult(DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.Timeout));
        var result = await Coordinator(files, copies, client).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Failed, result.Outcome);
        Assert.Equal(phase == "source" ? "REQUIREMENT_STORAGE_ERROR" : "AI2_TIMEOUT", result.Termination!.Receipt!.ErrorCode);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(RequirementProcessingJobState.Failed, (await db.RequirementProcessingJobs.SingleAsync(Token)).State);
        Assert.Equal(DocumentProcessingOutcome.Failed, (await db.RequirementProcessingAttempts.SingleAsync(Token)).Outcome);
        Assert.Equal(RequirementStatus.Failed, (await db.Requirements.SingleAsync(Token)).Status);
        Assert.Empty(await db.RequirementTechnicalProposals.ToListAsync(Token));
        Assert.Equal(phase == "source" ? 0 : 1, client.Calls); Released(files, copies, client);
    }

    [Theory]
    [InlineData("start")] [InlineData("documents")] [InlineData("publication")] [InlineData("termination")]
    public async Task Coordinator_CommitUncertain_NoDependentActions(string phase)
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var copies = new Copies(); var client = new ConsumingClient(); var lost = false;
        IInterceptor[] hooks = [new CommitHook(() => { lost = true; throw new IOException("response lost"); }),
            new OpenHook(() => { if (lost) throw new IOException("observer unavailable"); })];
        var closes = new JobProtocolTestSupport.WriteProbe();
        if (phase == "termination") client.Action = (_, _) => Task.FromResult(DocumentProcessingClientResult.Failed(DocumentProcessingClientFailure.Timeout));
        var result = await Coordinator(files, copies, client,
            start: phase == "start" ? hooks : null, documents: phase == "documents" ? hooks : null,
            publication: phase == "publication" ? hooks : null,
            termination: phase == "termination" ? hooks : [closes]).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.True(lost); Assert.Equal(AssignedExecutionOutcome.Indeterminate, result.Outcome);
        Assert.Equal(phase is "publication" or "termination" ? 1 : 0, client.Calls);
        Assert.Equal(0, closes.Count);
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(phase == "publication" ? RequirementProcessingJobState.Succeeded
            : phase == "termination" ? RequirementProcessingJobState.Failed : RequirementProcessingJobState.Running, job.State);
        Assert.Equal(phase == "start" ? 0 : 1, await db.Set<RequirementExecutionEvidence>().CountAsync(Token));
        Assert.Equal(phase == "publication" ? 1 : 0, await db.RequirementTechnicalProposals.CountAsync(Token));
        Released(files, copies, client);
    }

    [Theory]
    [InlineData("lease")] [InlineData("authority")]
    public async Task Coordinator_LostAuthorityBeforePublication_NoCompensation(string mode)
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var copies = new Copies(); var client = new ConsumingClient(); var closes = new JobProtocolTestSupport.WriteProbe();
        client.Action = async (r, _) =>
        {
            await using var db = fixture.CreateDbContext();
            if (mode == "authority") { (await db.Users.SingleAsync(Token)).Deactivate(await H.Now()); await db.SaveChangesAsync(Token); }
            else
            {
                var expired = (await db.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token)).StartedAtUtc!.Value.AddTicks(10);
                Assert.True(await H.Now() > expired);
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc={expired}", Token);
            }
            return ConsumingClient.Response(r);
        };
        var result = await Coordinator(files, copies, client, termination: [closes]).ExecuteAsync(s.Execution, s.Actor, Token);
        Assert.Equal(AssignedExecutionOutcome.Rejected, result.Outcome); Assert.Null(result.Termination);
        Assert.Equal(0, closes.Count); Assert.Equal(1, client.Calls);
        await using var read = fixture.CreateDbContext();
        Assert.Empty(await read.RequirementTechnicalProposals.ToListAsync(Token));
        Assert.Equal(RequirementProcessingJobState.Running, (await read.RequirementProcessingJobs.SingleAsync(Token)).State);
        Released(files, copies, client);
    }

    [Fact]
    public async Task Coordinator_ConcurrentIndependentStarts_OneProvider_NoLoserDisposal()
    {
        await using var files = new Files(); var s = await SeedFiles(files, start: false);
        var winnerCopies = new Copies(); var loserCopies = new Copies(); var client = new ConsumingClient();
        var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = Signal(); var probe = new JobProtocolTestSupport.QuoteProbe();
        var providerEntered = Signal(); var releaseProvider = Signal();
        client.DuringRead = async () => { providerEntered.TrySetResult(); await releaseProvider.Task.WaitAsync(Token); };
        var winner = Coordinator(files, winnerCopies, client, start: [new Saved(async db =>
        { entered.TrySetResult(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID); await release.Task.WaitAsync(Token); })])
            .ExecuteAsync(s.Execution, s.Actor, Token);
        Task<AssignedExecutionResult>? loser = null;
        try
        {
            var pid = await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            loser = Coordinator(files, loserCopies, client, start: [probe]).ExecuteAsync(s.Execution, s.Actor, Token);
            var blocked = await probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.NotEqual(pid, blocked);
            await new JobProtocolTestSupport(fixture).WaitBlocked(blocked, pid);
            output.WriteLine($"Coordinator start winner PID {pid}; blocked PID {blocked}; pg_blocking_pids observed.");
            Assert.False(winner.IsCompleted); Assert.False(loser.IsCompleted); Assert.Equal(0, client.Calls);
            release.TrySetResult();
            Assert.Equal(AssignedExecutionOutcome.AlreadyStarted, (await loser).Outcome);
            await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.All(winnerCopies.Created, copy => Assert.False(copy.Disposed));
            Assert.Empty(loserCopies.Created);
            releaseProvider.TrySetResult();
            Assert.Equal(AssignedExecutionOutcome.Published, (await winner).Outcome);
            Assert.Empty(loserCopies.Created); Assert.Equal(1, client.Calls); Assert.Equal(2, files.Opens);
            await AssertRows(1); Released(files, winnerCopies, client);
        }
        finally { release.TrySetResult(); releaseProvider.TrySetResult(); await winner; if (loser is not null) await loser; }
    }
}
