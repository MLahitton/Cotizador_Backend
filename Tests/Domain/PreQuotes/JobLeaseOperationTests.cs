using System.Text.Json;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class JobLeaseOperationTests
{
    public static readonly DateTimeOffset At = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid Instance = Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb");
    public sealed record Input
    {
        public Guid OperationId { get; init; } = Guid.NewGuid();
        public Guid JobId { get; init; } = Guid.NewGuid();
        public Guid RequirementId { get; init; } = Guid.NewGuid();
        public Guid ExecutionRequestId { get; init; } = Guid.NewGuid();
        public Guid ActorUserId { get; init; } = Guid.NewGuid();
        public Guid ClaimOperationId { get; init; }
        public string LeaseOwner { get; init; } = "";
        public long Generation { get; init; } = 1;
        public short Policy { get; init; } = 1;
        public DateTimeOffset Assigned { get; init; } = At.AddTicks(1234560);
        public DateTimeOffset Applied { get; init; } = At.AddTicks(1234560);
        public DateTimeOffset Expires { get; init; } = At.AddTicks(1234560).AddSeconds(120);
        public DateTimeOffset Expected { get; init; } = At.AddTicks(1234560).AddSeconds(120);
        public Guid? Attempt { get; init; }
        public JobLeaseOperation Make(bool renew = false) => renew
            ? JobLeaseOperation.CreateRenew(OperationId, JobId, RequirementId, ExecutionRequestId, ActorUserId,
                ClaimOperationId, LeaseOwner, Generation, Policy, Expected, Attempt, Assigned, Applied, Expires)
            : JobLeaseOperation.CreateClaim(OperationId, JobId, RequirementId, ExecutionRequestId, ActorUserId,
                ClaimOperationId, LeaseOwner, Generation, Policy, Assigned, Applied, Expires);
    }
    public static Input Claim()
    {
        var value = new Input();
        return value with { ClaimOperationId = value.OperationId, LeaseOwner = $"exec:{Instance:D}:{value.OperationId:D}" };
    }
    public static Input Renew(Input claim, bool extend = true) => claim with
    {
        OperationId = Guid.NewGuid(), Applied = claim.Applied.AddSeconds(extend ? 30 : 0),
        Expected = claim.Expires, Expires = claim.Expires.AddSeconds(extend ? 30 : 0)
    };

    [Fact]
    public void ValidReceipts_PreciseTimes_NoHiddenIdentityOrJobMutation()
    {
        var a = Claim(); var claim = a.Make();
        Assert.Equal(a.OperationId, claim.OperationId); Assert.Equal(a.Applied, claim.AppliedAtUtc);
        Assert.Null(claim.ExpectedLeaseExpiresAtUtc); Assert.Null(claim.ExpectedProcessingAttemptId);
        Assert.False(claim.IsEffectiveExtension);
        var b = Renew(a); var renewal = b.Make(true);
        Assert.True(renewal.IsEffectiveExtension); Assert.Equal(b.Expected, renewal.ExpectedLeaseExpiresAtUtc);
        Assert.False(Renew(a, false).Make(true).IsEffectiveExtension);
        var far = Renew(a, false) with { Expected = a.Expires.AddHours(1), Expires = a.Expires.AddHours(1) };
        Assert.False(far.Make(true).IsEffectiveExtension);
        var job = RequirementProcessingJob.CreateForVerifiedPublication(a.RequirementId, a.ExecutionRequestId,
            Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, At);
        var before = JsonSerializer.Serialize(job);
        _ = (a with { JobId = job.Id }).Make();
        Assert.Equal(before, JsonSerializer.Serialize(job));
        Assert.Empty(typeof(JobLeaseOperation).GetConstructors());
        Assert.All(typeof(JobLeaseOperation).GetProperties(), p => Assert.False(p.SetMethod?.IsPublic ?? false));
    }

    public static IEnumerable<object[]> InvalidCommon()
    {
        foreach (var field in new[] { "operation", "job", "requirement", "request", "actor", "claim", "owner-null",
            "owner-upper", "owner-instance-empty", "owner-wrong-claim", "owner-prefix", "owner-space", "owner-format",
            "generation", "policy", "assigned-offset", "applied-offset", "expires-offset", "assigned-ticks", "applied-ticks", "expires-ticks" })
            foreach (var renew in new[] { false, true }) yield return new object[] { field, renew };
    }
    [Theory]
    [MemberData(nameof(InvalidCommon))]
    public void RejectsInvalidIdentityOwnerVersionAndDates(string field, bool renew)
    {
        var a = Claim(); if (renew) a = Renew(a);
        a = field switch
        {
            "operation" => a with { OperationId = Guid.Empty }, "job" => a with { JobId = Guid.Empty },
            "requirement" => a with { RequirementId = Guid.Empty }, "request" => a with { ExecutionRequestId = Guid.Empty },
            "actor" => a with { ActorUserId = Guid.Empty }, "claim" => a with { ClaimOperationId = Guid.Empty },
            "owner-null" => a with { LeaseOwner = null! }, "owner-upper" => a with { LeaseOwner = a.LeaseOwner.ToUpperInvariant() },
            "owner-instance-empty" => a with { LeaseOwner = $"exec:{Guid.Empty:D}:{a.ClaimOperationId:D}" },
            "owner-wrong-claim" => a with { LeaseOwner = $"exec:{Instance:D}:{Guid.NewGuid():D}" },
            "owner-prefix" => a with { LeaseOwner = a.LeaseOwner.Replace("exec:", "node:") },
            "owner-space" => a with { LeaseOwner = a.LeaseOwner + " " },
            "owner-format" => a with { LeaseOwner = $"exec:{Instance:N}:{a.ClaimOperationId:D}" },
            "generation" => a with { Generation = 2 }, "policy" => a with { Policy = 2 },
            "assigned-offset" => a with { Assigned = a.Assigned.ToOffset(TimeSpan.FromHours(1)) },
            "applied-offset" => a with { Applied = a.Applied.ToOffset(TimeSpan.FromHours(1)) },
            "expires-offset" => a with { Expires = a.Expires.ToOffset(TimeSpan.FromHours(1)) },
            "assigned-ticks" => a with { Assigned = a.Assigned.AddTicks(1) },
            "applied-ticks" => a with { Applied = a.Applied.AddTicks(1) },
            _ => a with { Expires = a.Expires.AddTicks(1) }
        };
        Assert.Throws<ArgumentException>(() => a.Make(renew));
    }
    [Theory]
    [InlineData("claim-id")] [InlineData("claim-times")] [InlineData("claim-expiry")]
    [InlineData("renew-id")] [InlineData("renew-attempt")] [InlineData("renew-before")]
    [InlineData("renew-expired")] [InlineData("renew-formula")] [InlineData("renew-expected-ticks")]
    [InlineData("renew-expected-offset")]
    public void RejectsOperationSpecificInconsistencies(string mode)
    {
        var renew = mode.StartsWith("renew"); var a = Claim(); if (renew) a = Renew(a);
        a = mode switch
        {
            "claim-id" => a with { OperationId = Guid.NewGuid() },
            "claim-times" => a with { Assigned = a.Applied.AddSeconds(-1) },
            "claim-expiry" => a with { Expires = a.Expires.AddSeconds(1) },
            "renew-id" => a with { OperationId = a.ClaimOperationId },
            "renew-attempt" => a with { Attempt = Guid.Empty },
            "renew-before" => a with { Assigned = a.Applied.AddSeconds(1) },
            "renew-expired" => a with { Expected = a.Applied },
            "renew-formula" => a with { Expires = a.Expires.AddSeconds(1) },
            "renew-expected-ticks" => a with { Expected = a.Expected.AddTicks(1) },
            _ => a with { Expected = a.Expected.ToOffset(TimeSpan.FromHours(1)) }
        };
        Assert.Throws<ArgumentException>(() => a.Make(renew));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OverflowIsRejectedBeforeMaterialization(bool renew)
    {
        var a = Claim(); if (renew) a = Renew(a);
        var maximum = DateTimeOffset.MaxValue.AddTicks(-9);
        a = a with { Applied = maximum, Assigned = maximum, Expires = maximum, Expected = maximum };
        Assert.Throws<ArgumentOutOfRangeException>(() => a.Make(renew));
    }
}
