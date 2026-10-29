using System.Text.Json;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class ExpiredExecutionCancellationTests
{
    internal static readonly DateTimeOffset At = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);
    internal static readonly Guid Instance = Guid.Parse("69a253ad-1111-4222-8333-123456789abc");
    internal sealed record Input(Guid OperationId, Guid JobId, Guid RequirementId, Guid ExecutionRequestId,
        Guid ClaimOperationId, Guid ClosedByActorUserId, Guid CorrelationId, string LeaseOwner,
        long LeaseGeneration, DateTimeOffset AssignedAtUtc, DateTimeOffset ExpectedLeaseExpiresAtUtc,
        Guid? ExpectedProcessingAttemptId, DateTimeOffset CancellationRequestedAtUtc,
        DateTimeOffset FinishedAtUtc, DateTimeOffset GuardAtUtc, short PolicyVersion)
    {
        internal ExpiredExecutionCancellation Make() => ExpiredExecutionCancellation.Create(OperationId, JobId,
            RequirementId, ExecutionRequestId, ClaimOperationId, ClosedByActorUserId, CorrelationId, LeaseOwner,
            LeaseGeneration, AssignedAtUtc, ExpectedLeaseExpiresAtUtc, ExpectedProcessingAttemptId,
            CancellationRequestedAtUtc, FinishedAtUtc, GuardAtUtc, PolicyVersion);
    }
    internal static Input Valid(bool attempt = false)
    {
        var claim = Guid.NewGuid();
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), claim, Guid.NewGuid(),
            Guid.NewGuid(), $"exec:{Instance:D}:{claim:D}", 1, At.AddTicks(10), At.AddSeconds(120).AddTicks(20),
            attempt ? Guid.NewGuid() : null, At.AddSeconds(1).AddTicks(30), At.AddSeconds(121).AddTicks(40),
            At.AddSeconds(122).AddTicks(50), 1);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExactValues_WithOrWithoutAttempt(bool attempt)
    {
        var input = Valid(attempt); var receipt = input.Make();
        Assert.Equal(JsonSerializer.Serialize(input), JsonSerializer.Serialize(receipt));
        Assert.Equal(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(input.Make()));
        Assert.True(typeof(ExpiredExecutionCancellation).IsSealed);
        Assert.Empty(typeof(ExpiredExecutionCancellation).GetConstructors());
        Assert.All(typeof(ExpiredExecutionCancellation).GetProperties(), p => Assert.True(p.SetMethod!.IsPrivate));
    }

    [Fact]
    public void ApprovedEqualities_AndNoExtraCrossNamespaceOrTimeRules()
    {
        var input = Valid(true); var id = input.ClaimOperationId;
        var sameIds = input with { OperationId = id, JobId = id, RequirementId = id, ExecutionRequestId = id,
            ClosedByActorUserId = id, CorrelationId = id, ExpectedProcessingAttemptId = id,
            FinishedAtUtc = input.ExpectedLeaseExpiresAtUtc, GuardAtUtc = input.ExpectedLeaseExpiresAtUtc,
            CancellationRequestedAtUtc = input.ExpectedLeaseExpiresAtUtc };
        Assert.Equal(JsonSerializer.Serialize(sameIds), JsonSerializer.Serialize(sameIds.Make()));
        Assert.Equal(input.AssignedAtUtc.AddDays(-1),
            (input with { CancellationRequestedAtUtc = input.AssignedAtUtc.AddDays(-1) }).Make().CancellationRequestedAtUtc);
        var maximum = new DateTimeOffset(DateTimeOffset.MaxValue.Ticks / 10 * 10, TimeSpan.Zero);
        Assert.Equal(maximum, (input with { GuardAtUtc = maximum }).Make().GuardAtUtc);
    }

    [Theory]
    [InlineData("OperationId")] [InlineData("JobId")] [InlineData("RequirementId")]
    [InlineData("ExecutionRequestId")] [InlineData("ClaimOperationId")] [InlineData("ClosedByActorUserId")]
    [InlineData("CorrelationId")] [InlineData("ExpectedProcessingAttemptId")]
    public void EmptyIdentity_ExactArgumentException(string property)
    {
        var input = Valid(true);
        typeof(Input).GetProperty(property)!.SetValue(input, Guid.Empty);
        Assert.Throws<ArgumentException>(() => input.Make());
    }

    [Theory]
    [InlineData("null")] [InlineData("empty")] [InlineData("prefix")] [InlineData("upper")]
    [InlineData("space")] [InlineData("zero-instance")] [InlineData("wrong-claim")]
    [InlineData("bad-instance")] [InlineData("too-long")]
    public void Owner_ExactCanonicalClaimIdentity(string mode)
    {
        var input = Valid();
        var owner = mode switch
        {
            "null" => null!, "empty" => "", "prefix" => input.LeaseOwner.Replace("exec:", "Exec:"),
            "upper" => input.LeaseOwner.ToUpperInvariant(), "space" => input.LeaseOwner + " ",
            "zero-instance" => $"exec:{Guid.Empty:D}:{input.ClaimOperationId:D}",
            "wrong-claim" => $"exec:{Instance:D}:{Guid.NewGuid():D}",
            "bad-instance" => input.LeaseOwner.Replace("69a253ad", "zzzzzzzz"),
            _ => new string('a', 201)
        };
        var error = Assert.Throws<ArgumentException>(() => (input with { LeaseOwner = owner }).Make());
        Assert.Equal("leaseOwner", error.ParamName);
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(2, 1)] [InlineData(-1, 1)]
    [InlineData(1, 0)] [InlineData(1, 2)] [InlineData(1, -1)]
    public void GenerationAndPolicy(long generation, short policy) =>
        Assert.Throws<ArgumentException>(() => (Valid() with { LeaseGeneration = generation, PolicyVersion = policy }).Make());

    public static IEnumerable<object[]> Dates() => new[] { "AssignedAtUtc", "ExpectedLeaseExpiresAtUtc",
        "CancellationRequestedAtUtc", "FinishedAtUtc", "GuardAtUtc" }
        .SelectMany(p => new[] { new object[] { p, false }, new object[] { p, true } });

    [Theory]
    [MemberData(nameof(Dates))]
    public void Dates_NotUtcOrSubmicrosecond_RejectedWithoutNormalization(string property, bool offset)
    {
        var input = Valid(); var p = typeof(Input).GetProperty(property)!;
        var value = (DateTimeOffset)p.GetValue(input)!;
        p.SetValue(input, offset ? value.ToOffset(TimeSpan.FromHours(1)) : value.AddTicks(1));
        Assert.Throws<ArgumentException>(() => input.Make());
    }

    [Theory]
    [InlineData("assigned-equal")] [InlineData("assigned-after")] [InlineData("expiry-after")]
    [InlineData("finished-after")] [InlineData("request-after")]
    public void TemporalOrder_OnlyApprovedInequalities(string mode)
    {
        var input = Valid();
        input = mode switch
        {
            "assigned-equal" => input with { AssignedAtUtc = input.ExpectedLeaseExpiresAtUtc },
            "assigned-after" => input with { AssignedAtUtc = input.ExpectedLeaseExpiresAtUtc.AddTicks(10) },
            "expiry-after" => input with { ExpectedLeaseExpiresAtUtc = input.FinishedAtUtc.AddTicks(10) },
            "finished-after" => input with { FinishedAtUtc = input.GuardAtUtc.AddTicks(10) },
            _ => input with { CancellationRequestedAtUtc = input.FinishedAtUtc.AddTicks(10) }
        };
        Assert.Throws<ArgumentException>(() => input.Make());
    }
}
