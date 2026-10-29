using System.Data.Common;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class AssignedJobStartPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed record Seed(AssignedJobExecution Execution, Guid Actor, Guid Project, Guid Client, Guid Quote, Guid Correlation);
    private sealed record Snapshot(RequirementStatus Status, DateTimeOffset Updated, DateTimeOffset Activity,
        Guid? Attempt, int Count, RequirementProcessingJobState JobState, string? LeaseOwner,
        DateTimeOffset? Expires, DateTimeOffset? Cancellation, int Attempts);

    private StartAssignedJobService Service(params IInterceptor[] interceptors) => new(new AssignedJobStartStore(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(interceptors).Options));

    private async Task<Seed> SeedAsync(bool expired = false, bool publicContext = false)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var now = await DbNowAsync();
        var old = now.AddMinutes(-5);
        var user = ProcessPublicExperienceSubmissionTests.Owner();
        var client = Client.Create(ClientType.Person, "Atomic start", null, null, null, null, null, null, null, user.Id, old);
        var project = Project.Create(client.Id, "ATOMIC", "Atomic start", null, null, user.Id, old);
        var quote = PreQuote.Create(project.Id, user.Id, "PC-2026-0001", null, old);
        var requirement = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, old);
        var submission = publicContext ? ProcessPublicExperienceSubmissionTests.Submission() : null;
        if (submission is not null)
        {
            submission.BeginProcessing(DateTimeOffset.UtcNow);
            submission.CompleteProcessing(client.Id, project.Id, DateTimeOffset.UtcNow);
            submission.CompleteRequirementPreparation(quote.Id, requirement.Id, DateTimeOffset.UtcNow);
            db.Add(submission);
        }
        var job = RequirementProcessingJob.Create(requirement.Id, Guid.NewGuid(), Guid.NewGuid(),
            publicContext ? RequirementProcessingJobTrigger.PublicPreparation : RequirementProcessingJobTrigger.ManualInitial,
            old, submission?.Id);
        job.AssignExecution("executor", old.AddMinutes(1), expired ? now.AddSeconds(-1) : now.AddMinutes(3));
        db.AddRange(user, client, project, quote, requirement, job,
            RequirementFile.Create(requirement.Id, "test.pdf", "application/pdf", 12, "tests/no-storage-read.pdf", old));
        await db.SaveChangesAsync(Token);
        return new(new(job.Id, job.ExecutionRequestId, requirement.Id, "executor", 1), user.Id, project.Id, client.Id, quote.Id, job.CorrelationId);
    }

    private async Task<DateTimeOffset> DbNowAsync()
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(Token);
    }

    private async Task<Snapshot> ReadAsync(Seed seed)
    {
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.SingleAsync(x => x.Id == seed.Execution.JobId, Token);
        var requirement = await db.Requirements.SingleAsync(x => x.Id == seed.Execution.RequirementId, Token);
        var quote = await db.PreQuotes.SingleAsync(x => x.Id == seed.Quote, Token);
        return new(requirement.Status, requirement.UpdatedAtUtc, quote.UpdatedAtUtc, job.ProcessingAttemptId,
            job.AttemptCount, job.State, job.LeaseOwner, job.LeaseExpiresAtUtc, job.CancellationRequestedAtUtc,
            await db.RequirementProcessingAttempts.CountAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAndReplay_AtomicAndReadOnlyRecognition(bool publicContext)
    {
        var seed = await SeedAsync(publicContext: publicContext);
        var before = await ReadAsync(seed);
        var result = await Service().StartAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.Started, result.State);
        Assert.Equal(seed.Correlation, result.Receipt!.CorrelationId);
        var after = await ReadAsync(seed);
        Assert.Equal(RequirementStatus.Processing, after.Status);
        Assert.Equal(1, after.Attempts);
        Assert.Equal(1, after.Count);
        Assert.Equal(result.Receipt.ProcessingAttemptId, after.Attempt);
        Assert.True(after.Activity > before.Activity);
        Assert.Equal(before.Expires, after.Expires);
        await using var db = fixture.CreateDbContext();
        var attempt = await db.RequirementProcessingAttempts.SingleAsync(Token);
        Assert.Equal(DocumentProcessingState.Processing, attempt.ProcessingState);
        Assert.Equal(seed.Actor, attempt.RequestedByUserId);
        Assert.Equal(seed.Correlation, attempt.CorrelationId);
        var replay = await Service().StartAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.AlreadyStarted, replay.State);
        Assert.Equal(result.Receipt, replay.Receipt);
        Assert.Equal(after, await ReadAsync(seed));
    }

    [Fact]
    public async Task ConcurrentStarts_TwoConnections_OneStartedOneRecognition()
    {
        var seed = await SeedAsync();
        var saved = Signal(); var release = Signal(); var competing = Signal();
        var firstPid = 0; var secondPid = 0;
        var first = Service(new SaveHook(async context =>
        {
            firstPid = ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;
            saved.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        })).StartAsync(seed.Execution, seed.Actor, Token);
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        var second = Service(new ReadHook(command =>
        {
            if (command.CommandText.Contains("pre_quotes") && command.CommandText.Contains("FOR UPDATE"))
            {
                secondPid = ((NpgsqlConnection)command.Connection!).ProcessID;
                competing.TrySetResult();
            }
        })).StartAsync(seed.Execution, seed.Actor, Token);
        await competing.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        release.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30), Token);
        Assert.NotEqual(firstPid, secondPid);
        Assert.Single(results, x => x.State == AssignedJobStartState.Started);
        Assert.Single(results, x => x.State == AssignedJobStartState.AlreadyStarted);
        Assert.Equal(results[0].Receipt, results[1].Receipt);
        Assert.Equal(1, (await ReadAsync(seed)).Attempts);
    }

    [Fact]
    public async Task FailureAfterSaveBeforeGuard_RollsBackEverything()
    {
        var seed = await SeedAsync();
        var before = await ReadAsync(seed);
        var result = await Service(new SaveHook(_ => throw new InvalidOperationException("test rollback")))
            .StartAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(AssignedJobStartFailure.PersistenceError, result.Failure);
        Assert.Equal(before, await ReadAsync(seed));
    }

    [Theory]
    [InlineData("cancel", AssignedJobStartFailure.CancellationRequested)]
    [InlineData("expired", AssignedJobStartFailure.LeaseConflict)]
    [InlineData("active", AssignedJobStartFailure.ActiveAttempt)]
    [InlineData("request", AssignedJobStartFailure.IdentityConflict)]
    [InlineData("requirement", AssignedJobStartFailure.IdentityConflict)]
    [InlineData("leaseOwner", AssignedJobStartFailure.LeaseConflict)]
    [InlineData("generation", AssignedJobStartFailure.LeaseConflict)]
    [InlineData("attempt", AssignedJobStartFailure.IdentityConflict)]
    [InlineData("otherUser", AssignedJobStartFailure.NotFound)]
    [InlineData("adminOther", AssignedJobStartFailure.NotFound)]
    [InlineData("missingUser", AssignedJobStartFailure.Unauthorized)]
    [InlineData("inactiveUser", AssignedJobStartFailure.InactiveUser)]
    [InlineData("inactiveProject", AssignedJobStartFailure.InactiveProject)]
    [InlineData("inactiveClient", AssignedJobStartFailure.InactiveClient)]
    [InlineData("notCurrent", AssignedJobStartFailure.StateConflict)]
    [InlineData("noFiles", AssignedJobStartFailure.InvalidDocuments)]
    [InlineData("queued", AssignedJobStartFailure.StateConflict)]
    [InlineData("publicLinks", AssignedJobStartFailure.ContextConflict)]
    public async Task RejectionsNeverWrite(string scenario, AssignedJobStartFailure expected)
    {
        var seed = await SeedAsync(expired: scenario == "expired");
        var execution = seed.Execution;
        var actor = seed.Actor;
        await using (var db = fixture.CreateDbContext())
        {
            var now = await DbNowAsync();
            switch (scenario)
            {
                case "cancel": (await db.RequirementProcessingJobs.SingleAsync(Token)).RequestCancellation(now); break;
                case "active":
                    var attempt = RequirementProcessingAttempt.Create(execution.RequirementId, actor, Guid.NewGuid(), now);
                    attempt.Start(now); db.Add(attempt); break;
                case "request": execution = execution with { ExecutionRequestId = Guid.NewGuid() }; break;
                case "requirement": execution = execution with { RequirementId = Guid.NewGuid() }; break;
                case "leaseOwner": execution = execution with { LeaseOwner = "different" }; break;
                case "generation": execution = execution with { LeaseGeneration = 2 }; break;
                case "attempt": execution = execution with { ProcessingAttemptId = Guid.NewGuid() }; break;
                case "missingUser": actor = Guid.NewGuid(); break;
                case "otherUser":
                case "adminOther":
                    var other = global::Domain.Identity.User.CreateFromGoogle("other@example.test", "Other", null, null, now);
                    if (scenario == "adminOther") other.ChangeRole(global::Domain.Identity.UserRole.Admin, now);
                    db.Add(other); actor = other.Id; break;
                case "inactiveUser": (await db.Users.SingleAsync(Token)).Deactivate(now); break;
                case "inactiveProject": (await db.Projects.SingleAsync(Token)).SetActive(false, actor, now); break;
                case "inactiveClient": (await db.Clients.SingleAsync(Token)).SetActive(false, actor, now); break;
                case "notCurrent": (await db.Requirements.SingleAsync(Token)).Cancel(now); break;
                case "noFiles": db.RequirementFiles.RemoveRange(await db.RequirementFiles.ToListAsync(Token)); break;
                case "queued":
                    db.RequirementProcessingJobs.RemoveRange(await db.RequirementProcessingJobs.ToListAsync(Token));
                    await db.SaveChangesAsync(Token);
                    var queued = RequirementProcessingJob.Create(execution.RequirementId, execution.ExecutionRequestId,
                        seed.Correlation, RequirementProcessingJobTrigger.ManualInitial, now);
                    db.Add(queued); execution = execution with { JobId = queued.Id }; seed = seed with { Execution = execution }; break;
                case "publicLinks":
                    var submission = ProcessPublicExperienceSubmissionTests.Submission();
                    db.Add(submission); await db.SaveChangesAsync(Token);
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET submission_id = {submission.Id} WHERE id = {execution.JobId}", Token);
                    break;
            }
            await db.SaveChangesAsync(Token);
        }
        var before = await ReadAsync(seed);
        var result = await Service().StartAsync(execution, actor, Token);
        Assert.Equal(AssignedJobStartState.Rejected, result.State);
        Assert.Equal(expected, result.Failure);
        Assert.Null(result.Receipt);
        Assert.Equal(before, await ReadAsync(seed));
    }

    [Fact]
    public async Task ExpiresAfterSave_ObservedDatabaseTime_GuardRollsBack()
    {
        var seed = await SeedAsync();
        var expiry = (await DbNowAsync()).AddSeconds(4);
        await using (var db = fixture.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc = {expiry} WHERE id = {seed.Execution.JobId}", Token);
        var before = await ReadAsync(seed);
        var saved = Signal(); var release = Signal();
        var operation = Service(new SaveHook(async _ =>
        {
            Assert.True(await DbNowAsync() < expiry);
            saved.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        })).StartAsync(seed.Execution, seed.Actor, Token);
        await saved.Task.WaitAsync(TimeSpan.FromSeconds(20), Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (await DbNowAsync() < expiry) await Task.Delay(25, deadline.Token);
        release.SetResult();
        var result = await operation.WaitAsync(TimeSpan.FromSeconds(20), Token);
        Assert.Equal(AssignedJobStartFailure.LeaseConflict, result.Failure);
        Assert.Equal(before, await ReadAsync(seed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostCommitResponse_ReconcilesOrReturnsIndeterminate_NoCompensation(bool unavailable)
    {
        var seed = await SeedAsync();
        var committed = false;
        var result = await Service(new CommitHook(() => { committed = true; throw new IOException("simulated lost commit response"); }),
            new OpenHook(() => { if (unavailable && committed) throw new IOException("simulated reconciliation outage"); }))
            .StartAsync(seed.Execution, seed.Actor, Token);
        Assert.True(committed);
        Assert.Equal(unavailable ? AssignedJobStartState.Indeterminate : AssignedJobStartState.AlreadyStarted, result.State);
        var persisted = await ReadAsync(seed);
        Assert.Equal(1, persisted.Attempts);
        Assert.Equal(RequirementStatus.Processing, persisted.Status);
        var recognized = await Service().RecognizeAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(persisted.Attempt, recognized.Receipt!.ProcessingAttemptId);
        Assert.Equal(persisted, await ReadAsync(seed));
    }

    [Fact]
    public async Task RecognitionAfterTerminalAndReplacement_DoesNotRequireReleasedLeaseOrCurrentRequirement()
    {
        var seed = await SeedAsync();
        var first = await Service().StartAsync(seed.Execution, seed.Actor, Token);
        await using (var db = fixture.CreateDbContext())
        {
            var now = await DbNowAsync();
            var job = await db.RequirementProcessingJobs.SingleAsync(Token);
            var requirement = await db.Requirements.SingleAsync(Token);
            (await db.RequirementProcessingAttempts.SingleAsync(Token)).Fail("TEST_FAILURE", now);
            job.Fail("TEST_FAILURE", now); requirement.MarkFailed(now);
            var replacement = Requirement.Create(seed.Quote, seed.Actor, RequirementCommercialLine.Essential, now);
            replacement.MarkAsReplacementOf(requirement.Id);
            requirement.SupersedeBy(replacement.Id, now); db.Add(replacement);
            await db.SaveChangesAsync(Token);
        }
        var before = await ReadAsync(seed);
        var result = await Service().RecognizeAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.AlreadyStarted, result.State);
        Assert.Equal(first.Receipt, result.Receipt);
        Assert.Equal(before, await ReadAsync(seed));
        var bad = await Service().RecognizeAsync(seed.Execution with { ProcessingAttemptId = Guid.NewGuid() }, seed.Actor, Token);
        Assert.Equal(AssignedJobStartFailure.IdentityConflict, bad.Failure);
    }

    [Fact]
    public async Task RecognitionWithExpiredLease_StillAuthorizesCurrentReader_AndDoesNotWrite()
    {
        var seed = await SeedAsync();
        var first = await Service().StartAsync(seed.Execution, seed.Actor, Token);
        var now = await DbNowAsync();
        var admin = global::Domain.Identity.User.CreateFromGoogle("admin@example.test", "Admin", null, null, now);
        admin.ChangeRole(global::Domain.Identity.UserRole.Admin, now);
        var other = global::Domain.Identity.User.CreateFromGoogle("other@example.test", "Other", null, null, now);
        await using (var db = fixture.CreateDbContext())
        {
            db.AddRange(admin, other);
            await db.SaveChangesAsync(Token);
            var expired = now.AddSeconds(-1);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET lease_expires_at_utc = {expired} WHERE id = {seed.Execution.JobId}", Token);
        }
        var before = await ReadAsync(seed);
        var ownerReplay = await Service().StartAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.AlreadyStarted, ownerReplay.State);
        Assert.Equal(first.Receipt, ownerReplay.Receipt);
        var adminRead = await Service().RecognizeAsync(seed.Execution, admin.Id, Token);
        Assert.Equal(AssignedJobStartState.AlreadyStarted, adminRead.State);
        var adminMutation = await Service().StartAsync(seed.Execution, admin.Id, Token);
        Assert.Equal(AssignedJobStartFailure.NotFound, adminMutation.Failure);
        var denied = await Service().RecognizeAsync(seed.Execution, other.Id, Token);
        Assert.Equal(AssignedJobStartFailure.NotFound, denied.Failure);
        Assert.Null(denied.Receipt);
        Assert.Equal(before, await ReadAsync(seed));
    }

    [Fact]
    public async Task InconsistentRecognition_ReturnsIndeterminate_WithoutRepair()
    {
        var seed = await SeedAsync();
        await Service().StartAsync(seed.Execution, seed.Actor, Token);
        await using (var db = fixture.CreateDbContext())
        {
            var different = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id = {different} WHERE requirement_id = {seed.Execution.RequirementId}", Token);
        }
        var before = await ReadAsync(seed);
        var result = await Service().RecognizeAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.Indeterminate, result.State);
        Assert.Null(result.Receipt);
        Assert.Equal(before, await ReadAsync(seed));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class SaveHook(Func<DbContext, Task> callback) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { await callback(eventData.Context!); return result; }
    }
    private sealed class ReadHook(Action<DbCommand> callback) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { callback(command); return ValueTask.FromResult(result); }
    }
    private sealed class CommitHook(Action callback) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { callback(); return Task.CompletedTask; }
    }
    private sealed class OpenHook(Action callback) : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { callback(); return ValueTask.FromResult(result); }
    }
}
