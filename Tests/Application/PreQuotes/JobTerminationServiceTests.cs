using Application.PreQuotes.ProcessRequirement;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class JobTerminationServiceTests
{
    [Theory]
    [InlineData("job")]
    [InlineData("request")]
    [InlineData("requirement")]
    [InlineData("actor")]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("attempt")]
    [InlineData("error")]
    public async Task InvalidInputs_DoNotReachPersistence(string field)
    {
        var e = new AssignedJobExecution(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "executor", 1);
        var actor = field == "actor" ? Guid.Empty : Guid.NewGuid();
        e = field switch
        {
            "job" => e with { JobId = Guid.Empty },
            "request" => e with { ExecutionRequestId = Guid.Empty },
            "requirement" => e with { RequirementId = Guid.Empty },
            "owner" => e with { LeaseOwner = " " },
            "generation" => e with { LeaseGeneration = 0 },
            "attempt" => e with { ProcessingAttemptId = Guid.Empty },
            _ => e
        };
        var service = new JobTerminationService(new NeverCalled());
        var failed = await service.FinalizeJobFailedAsync(e, actor, field == "error" ? "SECRET_MESSAGE" : "AI2_TIMEOUT", TestContext.Current.CancellationToken);
        Assert.Equal(JobTerminationFailure.InvalidRequest, failed.Failure);
        if (field != "error")
            Assert.Equal(JobTerminationFailure.InvalidRequest,
                (await service.ConfirmJobCancellationAsync(e, actor, TestContext.Current.CancellationToken)).Failure);
        if (field is "job" or "request" or "requirement" or "actor")
            Assert.Equal(JobTerminationFailure.InvalidRequest,
                (await service.RequestJobCancellationAsync(new(e.JobId, e.ExecutionRequestId, e.RequirementId), actor, TestContext.Current.CancellationToken)).Failure);
    }
    private sealed class NeverCalled : IJobTerminationStore
    {
        public Task<JobTerminationResult> RequestJobCancellationAsync(JobCancellationRequest request, Guid actorUserId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence call");
        public Task<JobTerminationResult> ConfirmJobCancellationAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence call");
        public Task<JobTerminationResult> FinalizeJobFailedAsync(AssignedJobExecution execution, Guid actorUserId, string errorCode, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence call");
    }
}
