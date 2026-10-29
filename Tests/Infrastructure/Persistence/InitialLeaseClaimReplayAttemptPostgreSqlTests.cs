using System.Text.Json;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class InitialLeaseClaimPostgreSqlTests
{
    [Theory]
    [InlineData("foreign-requirement")]
    [InlineData("correlation")]
    [InlineData("not-started")]
    [InlineData("early-start")]
    [InlineData("running-finished")]
    [InlineData("terminal-processing")]
    public async Task Replay_IncompatibleLinkedAttempt_ExactConflict_NoDisclosureOrWrites(string scenario)
    {
        var s = await H.Create(running: false, verifiedPublication: true);
        var request = Request(s);
        var claim = await Service().ClaimInitialV2Async(request, s.Actor, Token);
        Assert.Equal(LeaseOperationState.Claimed, claim.State);
        var receipt = Assert.IsType<JobLeaseOperation>(claim.Receipt);
        var execution = s.Execution with { LeaseOwner = receipt.LeaseOwner };
        var start = await H.StartService().StartVerifiedAsync(execution, s.Actor, Token);
        Assert.Equal(AssignedJobStartState.Started, start.State);
        var attemptId = start.Receipt!.ProcessingAttemptId;
        execution = execution with { ProcessingAttemptId = attemptId };
        var other = await OtherJob(s);

        if (scenario == "terminal-processing")
            Assert.Equal(JobTerminationState.Failed,
                (await H.Call("fail", s with { Execution = execution })).State);

        // Services cannot create these cross-row inconsistencies. Fixture SQL leaves
        // every FK/CHECK/index active and changes neither claim identity nor authority.
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario == "foreign-requirement")
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET requirement_id={other.Execution.RequirementId} WHERE id={attemptId}", Token);
            if (scenario == "correlation")
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET correlation_id={Guid.NewGuid()} WHERE id={attemptId}", Token);
            if (scenario == "not-started")
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET processing_state='Pending',started_at_utc=NULL WHERE id={attemptId}", Token);
            if (scenario == "early-start")
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET created_at_utc={receipt.AssignedAtUtc.AddSeconds(-2)},started_at_utc={receipt.AssignedAtUtc.AddSeconds(-1)} WHERE id={attemptId}", Token);
            if (scenario == "running-finished")
            {
                (await db.RequirementProcessingAttempts.SingleAsync(x => x.Id == attemptId, Token)).Fail("AI2_TIMEOUT", await H.Now());
                await db.SaveChangesAsync(Token);
            }
            if (scenario == "terminal-processing")
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_attempts SET processing_state='Processing',completed_at_utc=NULL,outcome=NULL,error_code=NULL WHERE id={attemptId}", Token);
        }

        await using (var read = fixture.CreateDbContext())
        {
            var job = await read.RequirementProcessingJobs.AsNoTracking().SingleAsync(x => x.Id == request.JobId, Token);
            var attempt = await read.RequirementProcessingAttempts.AsNoTracking().SingleAsync(x => x.Id == attemptId, Token);
            Assert.Equal(attemptId, job.ProcessingAttemptId); Assert.Equal(1, job.AttemptCount);
            Assert.Equal(receipt.AssignedAtUtc, job.StartedAtUtc);
            Assert.Equal(RequirementProcessingJob.PublicationV2, job.RequiredPublicationVersion);
            Assert.Equal(1, job.LeaseGeneration);
            Assert.Equal(scenario == "terminal-processing" ? RequirementProcessingJobState.Failed : RequirementProcessingJobState.Running, job.State);
            if (job.State == RequirementProcessingJobState.Running) Assert.Equal(receipt.LeaseOwner, job.LeaseOwner);
            Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(
                await read.JobLeaseOperations.AsNoTracking().SingleAsync(x => x.OperationId == request.OperationId, Token)));
            Assert.Equal(scenario == "foreign-requirement" ? other.Execution.RequirementId : request.RequirementId, attempt.RequirementId);
            if (scenario == "correlation") Assert.NotEqual(job.CorrelationId, attempt.CorrelationId);
            else Assert.Equal(job.CorrelationId, attempt.CorrelationId);
            if (scenario == "not-started") Assert.Null(attempt.StartedAtUtc);
            else if (scenario == "early-start") Assert.True(attempt.StartedAtUtc < job.StartedAtUtc);
            else Assert.True(attempt.StartedAtUtc >= job.StartedAtUtc);
            Assert.Equal(scenario == "not-started" ? DocumentProcessingState.Pending
                : scenario == "running-finished" ? DocumentProcessingState.Finished : DocumentProcessingState.Processing, attempt.ProcessingState);
        }

        var before = await Snapshot();
        var receiptRead = false;
        var probe = new Commands { Before = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("job_lease_operations")) receiptRead = true;
            return Task.CompletedTask;
        }};
        var replay = await Service(probe).ClaimInitialV2Async(request, s.Actor, Token);
        Assert.True(receiptRead);
        Assert.Equal(LeaseOperationState.Conflict, replay.State);
        Assert.Equal(LeaseOperationFailure.AttemptConflict, replay.Failure);
        Assert.Null(replay.Receipt); Assert.Null(replay.Observation);
        Assert.Equal(0, probe.Writes);
        Assert.Equal(before, await Snapshot());
        output.WriteLine($"Replay {scenario}: Conflict/AttemptConflict, receipt read, no receipt/observation, DML=0; independent snapshots identical.");
    }
}
