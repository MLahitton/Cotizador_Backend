using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class RequirementProcessingJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static RequirementProcessingJob New() => RequirementProcessingJob.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, Now);
    private static RequirementProcessingJob Running()
    {
        var job = New();
        job.AssignExecution("worker-test", Now.AddSeconds(1), Now.AddMinutes(5));
        return job;
    }

    [Fact]
    public void Create_QueuedWithIndependentStableIdentitiesAndNoExecution()
    {
        var job = New();
        Assert.NotEqual(job.Id, job.ExecutionRequestId);
        Assert.Equal(RequirementProcessingJobState.Queued, job.State);
        Assert.Null(job.ProcessingAttemptId);
        Assert.Null(job.StartedAtUtc);
        Assert.Null(job.FinishedAtUtc);
        Assert.Null(job.LeaseOwner);
        Assert.Null(job.LeaseExpiresAtUtc);
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(0, job.LeaseGeneration);
    }

    [Fact]
    public void AssignmentAndLink_CountOnlyOneAttempt_ThenSucceedReleasesLease()
    {
        var job = Running();
        var request = job.ExecutionRequestId;
        var attempt = Guid.NewGuid();
        Assert.Equal(0, job.AttemptCount);
        Assert.Equal(1, job.LeaseGeneration);
        job.LinkStartedAttempt(attempt);
        job.LinkStartedAttempt(attempt);
        Assert.Equal(1, job.AttemptCount);
        Assert.Throws<InvalidOperationException>(() => job.LinkStartedAttempt(Guid.NewGuid()));
        job.Succeed(Now.AddSeconds(2));
        Assert.Equal(RequirementProcessingJobState.Succeeded, job.State);
        Assert.Equal(request, job.ExecutionRequestId);
        Assert.Equal(attempt, job.ProcessingAttemptId);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(1, job.LeaseGeneration);
        Assert.NotNull(job.StartedAtUtc);
        Assert.NotNull(job.FinishedAtUtc);
        Assert.Null(job.LeaseOwner);
        Assert.Null(job.LeaseExpiresAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_IsIdempotentAndBlocksOrdinaryCompletion(bool linkAttempt)
    {
        var job = Running();
        if (linkAttempt) job.LinkStartedAttempt(Guid.NewGuid());
        job.RequestCancellation(Now.AddSeconds(2));
        job.RequestCancellation(Now.AddSeconds(3));
        Assert.Equal(Now.AddSeconds(2), job.CancellationRequestedAtUtc);
        Assert.Equal(RequirementProcessingJobState.Running, job.State);
        Assert.Throws<InvalidOperationException>(() => job.LinkStartedAttempt(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => job.Succeed(Now.AddSeconds(4)));
        Assert.Throws<InvalidOperationException>(() => job.Fail("TEST_FAILURE", Now.AddSeconds(4)));
        job.ConfirmCancellation(Now.AddSeconds(4));
        job.RequestCancellation(Now.AddSeconds(5));
        Assert.Equal(Now.AddSeconds(4), job.FinishedAtUtc);
        Assert.Equal(Now.AddSeconds(2), job.CancellationRequestedAtUtc);
        Assert.Equal(linkAttempt ? 1 : 0, job.AttemptCount);
        Assert.Null(job.LeaseOwner);
        Assert.Null(job.LeaseExpiresAtUtc);
    }

    [Fact]
    public void QueueCancellation_DoesNotInventStartOrAttempt()
    {
        var job = New();
        job.RequestCancellation(Now);
        Assert.Equal(RequirementProcessingJobState.Cancelled, job.State);
        Assert.Equal(Now, job.FinishedAtUtc);
        Assert.Equal(Now, job.CancellationRequestedAtUtc);
        Assert.Null(job.StartedAtUtc);
        Assert.Null(job.ProcessingAttemptId);
        Assert.Equal(0, job.LeaseGeneration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failure_CanPrecedeAttemptAndPreservesHistory(bool linkAttempt)
    {
        var job = Running();
        if (linkAttempt) job.LinkStartedAttempt(Guid.NewGuid());
        job.Fail("TEST_FAILURE", Now.AddSeconds(3));
        Assert.Equal(RequirementProcessingJobState.Failed, job.State);
        Assert.Equal("TEST_FAILURE", job.LastErrorCode);
        Assert.Equal(linkAttempt ? 1 : 0, job.AttemptCount);
        Assert.Null(job.LeaseOwner);
        Assert.Null(job.LeaseExpiresAtUtc);
        Assert.Equal(1, job.LeaseGeneration);
    }

    [Theory]
    [InlineData(RequirementProcessingJobState.Succeeded)]
    [InlineData(RequirementProcessingJobState.Failed)]
    [InlineData(RequirementProcessingJobState.Cancelled)]
    public void TerminalsCannotReopen(RequirementProcessingJobState state)
    {
        var job = Running();
        job.LinkStartedAttempt(Guid.NewGuid());
        if (state == RequirementProcessingJobState.Succeeded) job.Succeed(Now.AddSeconds(2));
        if (state == RequirementProcessingJobState.Failed) job.Fail("TEST_FAILURE", Now.AddSeconds(2));
        if (state == RequirementProcessingJobState.Cancelled)
        {
            job.RequestCancellation(Now.AddSeconds(2));
            job.ConfirmCancellation(Now.AddSeconds(2));
        }
        Assert.Throws<InvalidOperationException>(() => job.AssignExecution("worker", Now.AddSeconds(3), Now.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => job.LinkStartedAttempt(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => job.Succeed(Now.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => job.Fail("TEST_FAILURE", Now.AddSeconds(3)));
        Assert.Throws<InvalidOperationException>(() => job.ConfirmCancellation(Now.AddSeconds(3)));
        if (state != RequirementProcessingJobState.Cancelled)
            Assert.Throws<InvalidOperationException>(() => job.RequestCancellation(Now.AddSeconds(3)));
    }

    [Fact]
    public void InvalidTransitionsAndDatesLeaveStateUnchanged()
    {
        var queued = New();
        Assert.Throws<InvalidOperationException>(() => queued.LinkStartedAttempt(Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => queued.Succeed(Now));
        Assert.Throws<InvalidOperationException>(() => queued.Fail("TEST_FAILURE", Now));
        Assert.Throws<InvalidOperationException>(() => queued.ConfirmCancellation(Now));
        Assert.Throws<ArgumentException>(() => queued.AssignExecution("worker", Now.AddSeconds(-1), Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => queued.AssignExecution("worker", Now, Now));
        Assert.Throws<ArgumentException>(() => queued.AssignExecution("", Now, Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() => queued.RequestCancellation(Now.ToOffset(TimeSpan.FromHours(1))));
        var job = Running();
        Assert.Throws<InvalidOperationException>(() => job.AssignExecution("worker", Now.AddSeconds(2), Now.AddMinutes(2)));
        Assert.Throws<ArgumentException>(() => job.LinkStartedAttempt(Guid.Empty));
        Assert.Throws<InvalidOperationException>(() => job.Succeed(Now.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => job.ConfirmCancellation(Now.AddSeconds(2)));
        Assert.Throws<ArgumentException>(() => job.Fail("TEST_FAILURE", Now));
        Assert.Null(job.LastErrorCode);
        job.RequestCancellation(Now.AddSeconds(3));
        Assert.Throws<ArgumentException>(() => job.ConfirmCancellation(Now.AddSeconds(2)));
        Assert.Throws<ArgumentException>(() => job.RequestCancellation(Now.AddSeconds(2)));
        Assert.Equal(RequirementProcessingJobState.Running, job.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("provider exception with secret")]
    [InlineData("lower_case")]
    [InlineData("BAD\nCODE")]
    public void RejectsArbitraryErrorMessages(string code)
    {
        var job = Running();
        Assert.Throws<ArgumentException>(() => job.Fail(code, Now.AddSeconds(2)));
        Assert.Null(job.LastErrorCode);
        Assert.Equal(RequirementProcessingJobState.Running, job.State);
    }

    [Fact]
    public void IdentityTriggerAndUtcValidation_RetryIsNewQueuedJob()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => RequirementProcessingJob.Create(Guid.Empty, id, id, RequirementProcessingJobTrigger.ManualInitial, Now));
        Assert.Throws<ArgumentException>(() => RequirementProcessingJob.Create(id, Guid.Empty, id, RequirementProcessingJobTrigger.ManualInitial, Now));
        Assert.Throws<ArgumentException>(() => RequirementProcessingJob.Create(id, id, Guid.Empty, RequirementProcessingJobTrigger.ManualInitial, Now));
        Assert.Throws<ArgumentException>(() => RequirementProcessingJob.Create(id, id, id, (RequirementProcessingJobTrigger)99, Now));
        Assert.Throws<ArgumentException>(() => RequirementProcessingJob.Create(id, id, id, RequirementProcessingJobTrigger.ManualRetry, Now));
        Assert.Throws<ArgumentException>(() => RequirementProcessingJob.Create(id, id, id, RequirementProcessingJobTrigger.PublicPreparation, Now, retryOfJobId: id));
        Assert.Throws<ArgumentException>(() => RequirementProcessingJob.Create(id, id, id, RequirementProcessingJobTrigger.ManualInitial, Now.ToOffset(TimeSpan.FromHours(1))));
        foreach (var field in new[] { "submission", "actor", "retry" })
            Assert.Throws<ArgumentException>(() => RequirementProcessingJob.Create(id, id, id, RequirementProcessingJobTrigger.ManualRetry, Now,
                submissionId: field == "submission" ? Guid.Empty : null,
                requestedByUserId: field == "actor" ? Guid.Empty : null,
                retryOfJobId: field == "retry" ? Guid.Empty : id));
        var previous = Running();
        previous.Fail("TEST_FAILURE", Now.AddSeconds(2));
        var retry = RequirementProcessingJob.Create(previous.RequirementId, Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.ManualRetry, Now.AddSeconds(3), retryOfJobId: previous.Id);
        Assert.NotEqual(previous.Id, retry.Id);
        Assert.NotEqual(previous.ExecutionRequestId, retry.ExecutionRequestId);
        Assert.Equal(RequirementProcessingJobState.Failed, previous.State);
        Assert.Equal(RequirementProcessingJobState.Queued, retry.State);
        Assert.Equal(0, retry.AttemptCount);
    }
}
