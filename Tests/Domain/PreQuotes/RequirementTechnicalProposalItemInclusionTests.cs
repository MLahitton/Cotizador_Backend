using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class RequirementTechnicalProposalItemInclusionTests
{
    private static readonly DateTimeOffset At =
        new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_DefaultsToIncluded()
    {
        var item = Item();

        Assert.True(item.IsIncluded);
        Assert.Equal(TechnicalProposalItemInclusionState.Included,
            item.InclusionState);
        Assert.Null(item.ExcludedAtUtc);
        Assert.Null(item.ExcludedByUserId);
        Assert.Null(item.ExclusionReason);
    }

    [Fact]
    public void Exclude_WhenIncluded_SetsStateAndMetadata()
    {
        var item = Item();

        var changed = item.Exclude(UserId, At, " No aplica ");

        Assert.True(changed);
        Assert.False(item.IsIncluded);
        Assert.Equal(TechnicalProposalItemInclusionState.Excluded,
            item.InclusionState);
        Assert.Equal(At, item.ExcludedAtUtc);
        Assert.Equal(UserId, item.ExcludedByUserId);
        Assert.Equal("No aplica", item.ExclusionReason);
    }

    [Fact]
    public void Exclude_WhenAlreadyExcluded_IsIdempotent()
    {
        var item = Item();
        item.Exclude(UserId, At, "No aplica");

        var changed = item.Exclude(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            At.AddMinutes(5),
            "Otro motivo");

        Assert.False(changed);
        Assert.Equal(At, item.ExcludedAtUtc);
        Assert.Equal(UserId, item.ExcludedByUserId);
        Assert.Equal("No aplica", item.ExclusionReason);
    }

    [Fact]
    public void Reactivate_WhenExcluded_RestoresIncludedAndClearsMetadata()
    {
        var item = Item();
        item.Exclude(UserId, At, "No aplica");

        var changed = item.Reactivate();

        Assert.True(changed);
        Assert.True(item.IsIncluded);
        Assert.Equal(TechnicalProposalItemInclusionState.Included,
            item.InclusionState);
        Assert.Null(item.ExcludedAtUtc);
        Assert.Null(item.ExcludedByUserId);
        Assert.Null(item.ExclusionReason);
    }

    [Fact]
    public void Reactivate_WhenAlreadyIncluded_IsIdempotent()
    {
        var item = Item();

        var changed = item.Reactivate();

        Assert.False(changed);
        Assert.True(item.IsIncluded);
    }

    private static RequirementTechnicalProposalItem Item() =>
        RequirementTechnicalProposalItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1m,
            1m,
            1m,
            1m,
            false,
            true,
            true,
            [],
            [],
            [],
            [],
            0,
            null,
            null,
            "AVAILABLE",
            At);
}
