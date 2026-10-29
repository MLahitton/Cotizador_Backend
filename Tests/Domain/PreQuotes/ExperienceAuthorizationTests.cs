using System.Text.Json;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class ExperienceAuthorizationTests
{
    internal static readonly DateTimeOffset At = new(2026, 3, 8, 5, 30, 0, TimeSpan.Zero);
    internal static RequirementProcessingJob Make() => RequirementProcessingJob.CreateForAutomaticExperience(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, At, At.AddHours(24));

    [Fact]
    public void ExplicitConstruction_ExactIdentityAndDates_NoExecution()
    {
        var id = Guid.NewGuid();
        var job = RequirementProcessingJob.CreateForAutomaticExperience(id, id, id, id, id, id, 1, At, At.AddHours(24));
        Assert.Equal(id, job.Id); Assert.Equal(id, job.AuthorizedActorUserId);
        Assert.Equal(id, job.RequirementId); Assert.Equal(id, job.SubmissionId);
        Assert.Equal(id, job.ExecutionRequestId); Assert.Equal(id, job.CorrelationId);
        Assert.Equal(At, job.CreatedAtUtc); Assert.Equal(At, job.AuthorizedAtUtc);
        Assert.Equal(At.AddHours(24), job.AuthorizationExpiresAtUtc);
        Assert.Equal(1, job.AutomaticAuthorizationPolicyVersion);
        Assert.Equal(RequirementProcessingJobTrigger.PublicPreparation, job.TriggerKind);
        Assert.Equal(RequirementProcessingJob.PublicationV2, job.RequiredPublicationVersion);
        Assert.Null(job.RequestedByUserId); Assert.Null(job.RetryOfJobId);
        Assert.Null(job.AuthorizationRevokedAtUtc); Assert.Null(job.DispatchAdmittedAtUtc);
        Assert.Null(job.DispatchInstanceId); Assert.Null(job.DispatchClaimOperationId);
        NoExecution(job);
        foreach (var property in typeof(RequirementProcessingJob).GetProperties().Where(p =>
            p.Name.StartsWith("Authoriz", StringComparison.Ordinal) || p.Name.StartsWith("Dispatch", StringComparison.Ordinal)
            || p.Name == nameof(job.AutomaticAuthorizationPolicyVersion)))
            Assert.True(property.SetMethod!.IsPrivate);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PreviousFactories_AllMetadataNull(bool verified)
    {
        var job = verified ? RequirementProcessingJob.CreateForVerifiedPublication(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.ManualInitial, At) : RequirementProcessingJob.Create(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, At);
        Assert.Null(job.AutomaticAuthorizationPolicyVersion); Assert.Null(job.AuthorizedActorUserId);
        Assert.Null(job.AuthorizedAtUtc); Assert.Null(job.AuthorizationExpiresAtUtc); Assert.Null(job.AuthorizationRevokedAtUtc);
        Assert.Null(job.DispatchAdmittedAtUtc); Assert.Null(job.DispatchInstanceId); Assert.Null(job.DispatchClaimOperationId);
        Assert.Throws<InvalidOperationException>(() => job.AdmitAutomaticDispatch(Guid.NewGuid(), Guid.NewGuid(), At));
        Assert.Throws<InvalidOperationException>(() => job.RevokeAutomaticAuthorization(At));
        NoExecution(job);
    }

    [Theory]
    [InlineData(0, "jobId")] [InlineData(1, "requirementId")] [InlineData(2, "submissionId")]
    [InlineData(3, "executionRequestId")] [InlineData(4, "correlationId")] [InlineData(5, "authorizedActorUserId")]
    public void EmptyIdentities(int index, string parameter)
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray(); ids[index] = Guid.Empty;
        Assert.Equal(parameter, Assert.Throws<ArgumentException>(() => RequirementProcessingJob.CreateForAutomaticExperience(
            ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], 1, At, At.AddHours(24))).ParamName);
    }

    [Theory]
    [InlineData("policy")] [InlineData("issued-offset")] [InlineData("expires-offset")]
    [InlineData("issued-ticks")] [InlineData("expires-ticks")] [InlineData("short")]
    [InlineData("long")] [InlineData("overflow")]
    public void ExactPolicyPrecisionDurationAndOverflow(string variant)
    {
        var start = variant switch { "issued-offset" => At.ToOffset(TimeSpan.FromHours(1)), "issued-ticks" => At.AddTicks(1),
            "overflow" => DateTimeOffset.MaxValue.AddTicks(-9), _ => At };
        var end = variant switch { "expires-offset" => At.AddHours(24).ToOffset(TimeSpan.FromHours(1)),
            "expires-ticks" => At.AddHours(24).AddTicks(1), "short" => At.AddHours(23), "long" => At.AddHours(25), _ => At.AddHours(24) };
        Assert.ThrowsAny<ArgumentException>(() => RequirementProcessingJob.CreateForAutomaticExperience(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), variant == "policy" ? 2 : 1, start, end));
    }

    [Theory]
    [InlineData("instance")] [InlineData("claim")] [InlineData("early")] [InlineData("expired")]
    [InlineData("offset")] [InlineData("ticks")]
    public void InvalidAdmission_NoPartialMutation(string variant)
    {
        var job = Make(); var before = JsonSerializer.Serialize(job);
        var date = variant switch { "early" => At.AddTicks(-10), "expired" => At.AddHours(24),
            "offset" => At.ToOffset(TimeSpan.FromHours(1)), "ticks" => At.AddTicks(1), _ => At };
        Assert.ThrowsAny<ArgumentException>(() => job.AdmitAutomaticDispatch(variant == "instance" ? Guid.Empty : Guid.NewGuid(),
            variant == "claim" ? Guid.Empty : Guid.NewGuid(), date));
        Assert.Equal(before, JsonSerializer.Serialize(job));
    }

    [Fact]
    public void AdmissionOnce_RevocationMonotone_AfterExpiry_NoLifecycleEffects()
    {
        var job = Make(); var instance = Guid.NewGuid(); var claim = Guid.NewGuid();
        job.AdmitAutomaticDispatch(instance, claim, At);
        var admitted = JsonSerializer.Serialize(job);
        Assert.Throws<InvalidOperationException>(() => job.AdmitAutomaticDispatch(instance, claim, At));
        Assert.Throws<InvalidOperationException>(() => job.AdmitAutomaticDispatch(Guid.NewGuid(), Guid.NewGuid(), At));
        Assert.Equal(admitted, JsonSerializer.Serialize(job));
        job.RevokeAutomaticAuthorization(At.AddDays(2));
        var revoked = JsonSerializer.Serialize(job);
        job.RevokeAutomaticAuthorization(At.AddDays(3));
        Assert.Equal(revoked, JsonSerializer.Serialize(job));
        Assert.Equal(instance, job.DispatchInstanceId); Assert.Equal(claim, job.DispatchClaimOperationId);
        Assert.Equal(At.AddHours(24), job.AuthorizationExpiresAtUtc); NoExecution(job);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RevocationBeforeAdmission_AndTerminalHistory(bool terminal)
    {
        var job = Make(); if (terminal) job.RequestCancellation(At.AddDays(2));
        job.RevokeAutomaticAuthorization(At.AddDays(3));
        Assert.Throws<InvalidOperationException>(() => job.AdmitAutomaticDispatch(Guid.NewGuid(), Guid.NewGuid(), At));
        Assert.Null(job.DispatchAdmittedAtUtc);
        Assert.Equal(terminal ? RequirementProcessingJobState.Cancelled : RequirementProcessingJobState.Queued, job.State);
        Assert.Equal(terminal ? At.AddDays(2) : (DateTimeOffset?)null, job.CancellationRequestedAtUtc);
    }

    [Theory]
    [InlineData("early")] [InlineData("before-dispatch")] [InlineData("offset")] [InlineData("ticks")]
    public void InvalidRevocation_PreservesAllFields(string variant)
    {
        var job = Make(); job.AdmitAutomaticDispatch(Guid.NewGuid(), Guid.NewGuid(), At.AddHours(1));
        var before = JsonSerializer.Serialize(job);
        var date = variant switch { "early" => At.AddTicks(-10), "before-dispatch" => At,
            "offset" => At.AddHours(2).ToOffset(TimeSpan.FromHours(1)), _ => At.AddHours(2).AddTicks(1) };
        Assert.Throws<ArgumentException>(() => job.RevokeAutomaticAuthorization(date));
        Assert.Equal(before, JsonSerializer.Serialize(job));
    }

    private static void NoExecution(RequirementProcessingJob job)
    {
        Assert.Equal(RequirementProcessingJobState.Queued, job.State); Assert.Equal(0, job.AttemptCount);
        Assert.Equal(0, job.LeaseGeneration); Assert.Null(job.ProcessingAttemptId); Assert.Null(job.StartedAtUtc);
        Assert.Null(job.FinishedAtUtc); Assert.Null(job.LeaseOwner); Assert.Null(job.LeaseExpiresAtUtc);
        Assert.Null(job.CancellationRequestedAtUtc); Assert.Null(job.PublicationContractVersion);
    }
}
