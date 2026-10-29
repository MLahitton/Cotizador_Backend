using System.Data.Common;
using System.Text.Json;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.Clients;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

internal sealed class JobTerminationTestSupport(PostgreSqlIntegrationFixture fixture)
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;
    internal sealed record Seed(AssignedJobExecution Execution, Guid Actor, Guid Quote)
    {
        public JobCancellationRequest Request => new(Execution.JobId, Execution.ExecutionRequestId, Execution.RequirementId);
    }
    internal DbContextOptions<ApplicationDbContext> Options(params IInterceptor[] hooks) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(hooks).Options;
    internal JobTerminationService Service(params IInterceptor[] hooks) => new(new JobTerminationStore(Options(hooks)));
    internal StartAssignedJobService StartService(params IInterceptor[] hooks) => new(new AssignedJobStartStore(Options(hooks)));
    internal async Task<DateTimeOffset> Now()
    {
        await using var db = fixture.CreateDbContext();
        return await db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(Token);
    }
    internal async Task<Seed> Create(bool running = true, bool attempt = false, bool request = false, bool publicContext = false, bool verifiedPublication = false)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var now = await Now();
        await using var db = fixture.CreateDbContext();
        var old = now.AddMinutes(-5);
        var user = ProcessPublicExperienceSubmissionTests.Owner();
        var client = Client.Create(ClientType.Person, "Termination", null, null, null, null, null, null, null, user.Id, old);
        var project = Project.Create(client.Id, "TERM", "Termination", null, null, user.Id, old);
        var quote = PreQuote.Create(project.Id, user.Id, "PC-2026-0001", null, old);
        var requirement = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, old);
        var submission = publicContext ? ProcessPublicExperienceSubmissionTests.Submission() : null;
        if (submission is not null)
        {
            submission.BeginProcessing(now);
            submission.CompleteProcessing(client.Id, project.Id, now);
            submission.CompleteRequirementPreparation(quote.Id, requirement.Id, now);
            db.Add(submission);
        }
        var createJob = verifiedPublication
            ? (Func<Guid, Guid, Guid, RequirementProcessingJobTrigger, DateTimeOffset, Guid?, Guid?, Guid?, RequirementProcessingJob>)RequirementProcessingJob.CreateForVerifiedPublication
            : RequirementProcessingJob.Create;
        var job = createJob(requirement.Id, Guid.NewGuid(), Guid.NewGuid(),
            publicContext ? RequirementProcessingJobTrigger.PublicPreparation : RequirementProcessingJobTrigger.ManualInitial,
            old, submission?.Id, null, null);
        if (running && !verifiedPublication) job.AssignExecution("executor", old.AddMinutes(1), now.AddMinutes(10));
        db.AddRange(user, client, project, quote, requirement, job,
            RequirementFile.Create(requirement.Id, "test.pdf", "application/pdf", 10, "tests/no-read.pdf", old));
        await db.SaveChangesAsync(Token);
        var e = new AssignedJobExecution(job.Id, job.ExecutionRequestId, requirement.Id, "executor", 1);
        if (running && verifiedPublication)
        {
            var claimed = await new ClaimInitialLeaseService(new InitialLeaseClaimStore(Options())).ClaimInitialV2Async(
                new(job.Id, requirement.Id, job.ExecutionRequestId, Guid.NewGuid(), Guid.NewGuid()), user.Id, Token);
            Assert.Equal(LeaseOperationState.Claimed, claimed.State);
            e = e with { LeaseOwner = claimed.Receipt!.LeaseOwner, LeaseGeneration = claimed.Receipt.LeaseGeneration };
        }
        if (attempt)
        {
            var started = await new AssignedJobStartStore(Options(), verifiedPublication
                ? RequirementProcessingJob.PublicationV2 : RequirementProcessingJob.PublicationV1).StartAsync(e, user.Id, Token);
            Assert.Equal(AssignedJobStartState.Started, started.State);
            e = e with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId };
        }
        var seed = new Seed(e, user.Id, quote.Id);
        if (request) Assert.Equal(JobTerminationState.CancellationRequested,
            (await Service().RequestJobCancellationAsync(seed.Request, seed.Actor, Token)).State);
        return seed;
    }
    internal Task<JobTerminationResult> Call(string operation, Seed s, JobTerminationService? service = null, CancellationToken? token = null) =>
        operation switch
        {
            "request" => (service ?? Service()).RequestJobCancellationAsync(s.Request, s.Actor, token ?? Token),
            "confirm" => (service ?? Service()).ConfirmJobCancellationAsync(s.Execution, s.Actor, token ?? Token),
            _ => (service ?? Service()).FinalizeJobFailedAsync(s.Execution, s.Actor, "AI2_TIMEOUT", token ?? Token)
        };
    internal async Task<string> Snapshot(bool includeJobs = true)
    {
        await using var db = fixture.CreateDbContext();
        var jobs = includeJobs ? await db.RequirementProcessingJobs.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token) : [];
        var requirements = await db.Requirements.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Status, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc, x.PreQuoteId, x.SupersededByRequirementId, x.SupersedesRequirementId }).ToListAsync(Token);
        var attempts = await db.RequirementProcessingAttempts.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.RequirementId, x.RequestedByUserId, x.CorrelationId, x.CreatedAtUtc, x.StartedAtUtc,
                x.CompletedAtUtc, x.ProcessingState, x.Outcome, x.ErrorCode }).ToListAsync(Token);
        var quotes = await db.PreQuotes.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.UpdatedAtUtc }).ToListAsync(Token);
        return JsonSerializer.Serialize(new { jobs, requirements, attempts, quotes });
    }
    internal static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal sealed class Saved(Func<DbContext, Task> callback) : SaveChangesInterceptor
    {
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken cancellationToken = default)
        { await callback(e.Context!); return result; }
    }
    internal sealed class CommandHook(Func<DbCommand, Task>? afterJobWrite = null, Action<DbCommand>? beforeQuoteLock = null) : DbCommandInterceptor
    {
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (afterJobWrite is not null && command.CommandText.StartsWith("UPDATE core.requirement_processing_jobs"))
                await afterJobWrite(command);
            return result;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pre_quotes") && command.CommandText.Contains("FOR UPDATE")) beforeQuoteLock?.Invoke(command);
            return ValueTask.FromResult(result);
        }
    }
    internal sealed class CommitHook(Action callback, bool after = true) : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { if (!after) callback(); return ValueTask.FromResult(result); }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (after) callback(); return Task.CompletedTask; }
    }
    internal sealed class OpenHook(Action callback) : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { callback(); return ValueTask.FromResult(result); }
    }
}
