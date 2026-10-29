using System.Text.Json;
using Application.Common.Abstractions.Authentication;
using Application.PreQuotes.ConfirmRequirementTechnicalProposalSelection;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class JobProtocolSequencePostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private JobProtocolTestSupport S => new(fixture);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task StartPreparePublishRecognize_ManualAndPublic(bool publicContext)
    {
        var seed = await S.H.Create(publicContext: publicContext);
        await using var read = fixture.CreateDbContext();
        var original = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        var source = JsonSerializer.Serialize(await read.Set<PublicExperienceSubmission>().AsNoTracking().ToListAsync(Token));
        seed = await S.Start(seed);
        var before = await S.P.Snapshot();
        var prepared = await S.Prepare(seed);
        Assert.Equal(before, await S.P.Snapshot());
        var published = await S.P.Service().PublishAsync(seed.Execution, seed.Actor, prepared, Token);
        Assert.Equal(JobPublicationState.Published, published.State);
        Assert.Equal(original.Id, published.Receipt!.JobId);
        Assert.Equal(original.CorrelationId, published.Receipt.CorrelationId);
        Assert.Equal(seed.Execution.ProcessingAttemptId, published.Receipt.ProcessingAttemptId);
        var after = await S.P.Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var replay = await S.P.Service(writes).PublishAsync(seed.Execution, seed.Actor, prepared, Token);
        Assert.Equal(JobPublicationState.AlreadyPublished, replay.State);
        Assert.Equal(published.Receipt, replay.Receipt);
        var startReplay = await S.H.StartService(writes).StartAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(AssignedJobStartState.AlreadyStarted, startReplay.State);
        Assert.Equal(published.Receipt.ProcessingAttemptId, startReplay.Receipt!.ProcessingAttemptId);
        Assert.Equal(after, await S.P.Snapshot());
        Assert.Equal(0, writes.Count);
        Assert.Equal(source, JsonSerializer.Serialize(await read.Set<PublicExperienceSubmission>().AsNoTracking().ToListAsync(Token)));
        var job = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(Token);
        Assert.Equal(publicContext, job.SubmissionId.HasValue);
        Assert.Equal(original.CreatedAtUtc, job.CreatedAtUtc);
        Assert.Equal(original.StartedAtUtc, job.StartedAtUtc);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(RequirementProcessingJobState.Succeeded, job.State);
        Assert.Equal(RequirementStatus.Processed, (await read.Requirements.AsNoTracking().SingleAsync(Token)).Status);
        Assert.Equal(1, await read.RequirementProcessingAttempts.CountAsync(Token));
        Assert.Equal(1, await read.RequirementExtractionResults.CountAsync(Token));
        Assert.Equal(1, await read.RequirementTechnicalProposals.CountAsync(Token));
    }

    [Fact]
    public async Task StartRequestConfirm_RepetitionsPreserveFirstDatesAndActivity()
    {
        var seed = await S.Start(await S.H.Create());
        var request = await S.H.Call("request", seed);
        Assert.Equal(JobTerminationState.CancellationRequested, request.State);
        var requested = await S.P.Snapshot();
        Assert.Equal(JobTerminationState.AlreadyRecognized, (await S.H.Call("request", seed)).State);
        Assert.Equal(requested, await S.P.Snapshot());
        var confirmation = await S.H.Call("confirm", seed);
        Assert.Equal(JobTerminationState.CancellationConfirmed, confirmation.State);
        Assert.Equal(request.Receipt!.CancellationRequestedAtUtc, confirmation.Receipt!.CancellationRequestedAtUtc);
        var terminal = await S.P.Snapshot();
        Assert.Equal(confirmation.Receipt, (await S.H.Call("confirm", seed)).Receipt);
        Assert.Equal(confirmation.Receipt, (await S.H.Call("request", seed)).Receipt);
        Assert.Equal(terminal, await S.P.Snapshot());
        await AssertTerminal(seed, RequirementProcessingJobState.Cancelled, RequirementStatus.Pending, DocumentProcessingOutcome.Cancelled);
    }

    [Fact]
    public async Task StartFail_LatePublicationCannotChangeClosure()
    {
        var seed = await S.Start(await S.H.Create());
        var prepared = await S.Prepare(seed);
        var failed = await S.H.Call("fail", seed);
        Assert.Equal(JobTerminationState.Failed, failed.State);
        var terminal = await S.P.Snapshot();
        var rejected = await S.P.Service().PublishAsync(seed.Execution, seed.Actor, prepared, Token);
        Assert.Equal(JobPublicationFailure.TerminalConflict, rejected.Failure);
        Assert.Equal(failed.Receipt, (await S.H.Call("fail", seed)).Receipt);
        Assert.Equal(terminal, await S.P.Snapshot());
        await AssertTerminal(seed, RequirementProcessingJobState.Failed, RequirementStatus.Failed, DocumentProcessingOutcome.Failed);
    }

    private async Task AssertTerminal(Seed seed, RequirementProcessingJobState jobState, RequirementStatus requirementState,
        DocumentProcessingOutcome outcome)
    {
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.SingleAsync(Token);
        var attempt = await db.RequirementProcessingAttempts.SingleAsync(Token);
        Assert.Equal(jobState, job.State);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(seed.Execution.ProcessingAttemptId, attempt.Id);
        Assert.Equal(attempt.Id, job.ProcessingAttemptId);
        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(outcome, attempt.Outcome);
        Assert.Equal(job.FinishedAtUtc, attempt.CompletedAtUtc);
        Assert.Equal(job.LastErrorCode, attempt.ErrorCode);
        Assert.Equal(job.FinishedAtUtc, (await db.PreQuotes.SingleAsync(Token)).UpdatedAtUtc);
        Assert.Equal(requirementState, (await db.Requirements.SingleAsync(Token)).Status);
        Assert.False(await db.RequirementExtractionResults.AnyAsync(Token));
        Assert.False(await db.RequirementTechnicalProposals.AnyAsync(Token));
    }

    [Fact]
    public async Task CancelledExecution_LateCallsLeaveRunningSuccessorUntouched()
    {
        var old = await S.Start(await S.H.Create());
        var prepared = await S.Prepare(old);
        await S.H.Call("request", old);
        Assert.Equal(JobTerminationState.CancellationConfirmed, (await S.H.Call("confirm", old)).State);
        Seed next;
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(RequirementStatus.Pending, (await db.Requirements.SingleAsync(Token)).Status);
            var now = await S.H.Now();
            var job = RequirementProcessingJob.Create(old.Execution.RequirementId, Guid.NewGuid(), Guid.NewGuid(),
                RequirementProcessingJobTrigger.ManualRetry, now, requestedByUserId: old.Actor, retryOfJobId: old.Execution.JobId);
            job.AssignExecution("successor", now, now.AddMinutes(10));
            db.RequirementProcessingJobs.Add(job);
            await db.SaveChangesAsync(Token);
            next = new(new(job.Id, job.ExecutionRequestId, job.RequirementId, "successor", 1), old.Actor, old.Quote);
        }
        next = await S.Start(next);
        Assert.NotEqual(old.Execution.JobId, next.Execution.JobId);
        Assert.NotEqual(old.Execution.ProcessingAttemptId, next.Execution.ProcessingAttemptId);
        var before = await S.P.Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        Assert.Equal(JobPublicationFailure.TerminalConflict, (await S.P.Service(writes).PublishAsync(old.Execution, old.Actor, prepared, Token)).Failure);
        Assert.Equal(JobTerminationState.AlreadyRecognized, (await S.H.Call("confirm", old, S.H.Service(writes))).State);
        Assert.Equal(JobTerminationState.AlreadyRecognized, (await S.H.Call("request", old, S.H.Service(writes))).State);
        Assert.Equal(JobTerminationState.Conflict, (await S.H.Call("fail", old, S.H.Service(writes))).State);
        var oldStart = await S.H.StartService(writes).StartAsync(old.Execution, old.Actor, Token);
        Assert.Equal(old.Execution.ProcessingAttemptId, oldStart.Receipt!.ProcessingAttemptId);
        Assert.Equal(before, await S.P.Snapshot());
        Assert.Equal(0, writes.Count);
        await using var read = fixture.CreateDbContext();
        var active = await read.RequirementProcessingJobs.SingleAsync(j => j.Id == next.Execution.JobId, Token);
        Assert.Equal(RequirementProcessingJobState.Running, active.State);
        Assert.Equal(next.Execution.ProcessingAttemptId, active.ProcessingAttemptId);
        Assert.Equal(1, active.AttemptCount);
        Assert.Equal(2, await read.RequirementProcessingAttempts.CountAsync(Token));
        var oldAttempt = await read.RequirementProcessingAttempts.SingleAsync(a => a.Id == old.Execution.ProcessingAttemptId, Token);
        var nextAttempt = await read.RequirementProcessingAttempts.SingleAsync(a => a.Id == next.Execution.ProcessingAttemptId, Token);
        Assert.Equal(DocumentProcessingState.Finished, oldAttempt.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Cancelled, oldAttempt.Outcome);
        Assert.Equal(DocumentProcessingState.Processing, nextAttempt.ProcessingState);
        Assert.Equal(active.CorrelationId, nextAttempt.CorrelationId);
        Assert.Null(nextAttempt.CompletedAtUtc);
        Assert.Equal(RequirementStatus.Processing, (await read.Requirements.SingleAsync(Token)).Status);
        Assert.False(await read.RequirementTechnicalProposals.AnyAsync(Token));
    }

    [Fact]
    public async Task Replay_PreservesRealCommercialRevisionConfirmationAndOverrides()
    {
        var s = await S.P.Create(review: false);
        var first = await S.P.Publish(s);
        Assert.Equal(JobPublicationState.Published, first.State);
        await using (var db = fixture.CreateDbContext())
        {
            var proposal = await db.RequirementTechnicalProposals.Include(p => p.Items).SingleAsync(Token);
            var item = Assert.Single(proposal.Items);
            item.UpdateManualLocation("Confirmed location");
            proposal.MarkCommerciallyChanged();
            await db.SaveChangesAsync(Token);
            var actor = Substitute.For<ICurrentUser>();
            actor.IsAuthenticated.Returns(true);
            actor.UserId.Returns(s.Execution.Actor);
            var confirmation = await new ConfirmRequirementTechnicalProposalSelectionService(actor,
                new IdentityRepository(db), new RequirementRepository(db), new PreQuoteRepository(db),
                new ProjectRepository(db), new ClientRepository(db), new ProductSystemCatalogRepository(db), TimeProvider.System)
                .ExecuteAsync(new(proposal.Id), Token);
            Assert.True(confirmation.IsSuccess, confirmation.Failure.ToString());
            Assert.Equal(2, proposal.CommercialRevision);
            Assert.NotNull(proposal.CommercialConfirmedAtUtc);
            Assert.Equal(s.Execution.Actor, proposal.CommercialConfirmedByUserId);
            Assert.True(item.HasSelectedConfiguration());
        }
        var before = await S.P.Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var replay = await S.P.Publish(s, S.P.Service(writes));
        Assert.Equal(JobPublicationState.AlreadyPublished, replay.State);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal(before, await S.P.Snapshot());
        Assert.Equal(0, writes.Count);
    }
}
