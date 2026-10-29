using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class RequirementDocumentTransitionTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static Requirement NewRequirement() => Requirement.Create(Guid.NewGuid(), Guid.NewGuid(), RequirementCommercialLine.Essential, At);

    [Fact]
    public void PostCancellationTransition_DoesNotBroadenLocalCanReplace()
    {
        var requirement = NewRequirement();
        var successor = Guid.NewGuid();
        Assert.False(requirement.CanReplace);
        Assert.Throws<InvalidOperationException>(() => requirement.SupersedeBy(successor, At));
        requirement.SupersedeAfterConfirmedCancellationBy(successor, At.AddSeconds(1));
        Assert.False(requirement.IsCurrent);
        Assert.Equal(RequirementStatus.Superseded, requirement.Status);
        Assert.Equal(successor, requirement.SupersededByRequirementId);
        Assert.Equal(At, requirement.CreatedAtUtc);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("self")]
    [InlineData("early")]
    [InlineData("nonutc")]
    public void InvalidPostCancellationArguments_DoNotMutate(string scenario)
    {
        var requirement = NewRequirement();
        var id = scenario == "empty" ? Guid.Empty : scenario == "self" ? requirement.Id : Guid.NewGuid();
        var at = scenario == "early" ? At.AddSeconds(-1) : scenario == "nonutc" ? At.ToOffset(TimeSpan.FromHours(1)) : At;
        Assert.Throws<ArgumentException>(() => requirement.SupersedeAfterConfirmedCancellationBy(id, at));
        Assert.True(requirement.IsCurrent);
        Assert.Null(requirement.SupersededByRequirementId);
        Assert.Equal(At, requirement.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NonPendingOrNonCurrent_CannotUsePostCancellationTransition(bool cancelled)
    {
        var requirement = NewRequirement();
        if (cancelled) requirement.Cancel(At); else requirement.StartProcessing(At);
        Assert.Throws<InvalidOperationException>(() => requirement.SupersedeAfterConfirmedCancellationBy(Guid.NewGuid(), At));
        Assert.Null(requirement.SupersededByRequirementId);
    }
}
