using Application.PreQuotes.ProcessRequirement;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class StartAssignedJobServiceTests
{
    [Theory]
    [InlineData("job")]
    [InlineData("request")]
    [InlineData("requirement")]
    [InlineData("actor")]
    [InlineData("owner")]
    [InlineData("generation")]
    [InlineData("attempt")]
    public async Task InvalidInput_NeverReachesPersistence(string field)
    {
        var execution = new AssignedJobExecution(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "executor", 1);
        var actor = Guid.NewGuid();
        execution = field switch
        {
            "job" => execution with { JobId = Guid.Empty },
            "request" => execution with { ExecutionRequestId = Guid.Empty },
            "requirement" => execution with { RequirementId = Guid.Empty },
            "owner" => execution with { LeaseOwner = " " },
            "generation" => execution with { LeaseGeneration = 0 },
            "attempt" => execution with { ProcessingAttemptId = Guid.Empty },
            _ => execution
        };
        if (field == "actor") actor = Guid.Empty;
        var service = new StartAssignedJobService(new NeverCalled());
        var started = await service.StartAsync(execution, actor, TestContext.Current.CancellationToken);
        var recognized = await service.RecognizeAsync(execution, actor, TestContext.Current.CancellationToken);
        var verified = await service.StartVerifiedAsync(execution, actor, TestContext.Current.CancellationToken);
        Assert.Equal(AssignedJobStartFailure.InvalidRequest, started.Failure);
        Assert.Equal(AssignedJobStartFailure.InvalidRequest, recognized.Failure);
        Assert.Equal(AssignedJobStartFailure.InvalidRequest, verified.Failure);
    }

    private sealed class NeverCalled : IAssignedJobStartStore
    {
        public Task<AssignedJobStartResult> StartVerifiedAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence call");
        public Task<AssignedJobStartResult> StartAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence call");
        public Task<AssignedJobStartResult> RecognizeAsync(AssignedJobExecution execution, Guid actorUserId, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected persistence call");
    }
}
