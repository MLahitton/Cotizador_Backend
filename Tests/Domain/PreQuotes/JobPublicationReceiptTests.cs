using System.Text.Json;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class JobPublicationReceiptTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static RequirementProcessingJob Running()
    {
        var job = RequirementProcessingJob.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, Now);
        job.AssignExecution("worker", Now, Now.AddMinutes(1));
        job.LinkStartedAttempt(Guid.NewGuid());
        return job;
    }
    [Fact]
    public void ReceiptIsWrittenOnlyWithSuccess_AndCannotBeOverwritten()
    {
        var job = Running();
        job.SucceedWithPublicationReceipt("sng-job-publication-v1", new string('A', 64), Now);
        Assert.Equal(RequirementProcessingJobState.Succeeded, job.State);
        Assert.Equal(new string('A', 64), job.PublicationContentHash);
        Assert.Null(job.LeaseOwner);
        var before = JsonSerializer.Serialize(job);
        Assert.Throws<InvalidOperationException>(() => job.SucceedWithPublicationReceipt("v2", new string('B', 64), Now));
        Assert.Equal(before, JsonSerializer.Serialize(job));
    }
    [Theory]
    [InlineData("version")]
    [InlineData("hash")]
    [InlineData("date")]
    [InlineData("cancel")]
    [InlineData("failed")]
    [InlineData("unlinked")]
    public void InvalidReceiptOrLifecycle_HasNoPartialMutation(string scenario)
    {
        var job = Running();
        if (scenario == "cancel") job.RequestCancellation(Now);
        if (scenario == "failed") job.Fail("TEST", Now);
        if (scenario == "unlinked")
        {
            job = RequirementProcessingJob.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, Now);
            job.AssignExecution("worker", Now, Now.AddMinutes(1));
        }
        var before = JsonSerializer.Serialize(job);
        Action complete = () => job.SucceedWithPublicationReceipt(scenario == "version" ? " \t" : "v1",
            scenario == "hash" ? new string('g', 64) : new string('A', 64), scenario == "date" ? Now.AddSeconds(-1) : Now);
        if (scenario is "version" or "hash" or "date") Assert.Throws<ArgumentException>(complete);
        else Assert.Throws<InvalidOperationException>(complete);
        Assert.Equal(before, JsonSerializer.Serialize(job));
    }
    [Fact]
    public void HistoricalSuccessWithoutReceipt_RemainsRepresentable()
    {
        var job = Running();
        job.Succeed(Now);
        Assert.Null(job.PublicationContentHash);
        Assert.Null(job.PublicationContractVersion);
    }
}
