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

    [Fact]
    public void CreateManual_SetsManualSourceAndEffectiveValues()
    {
        var systemId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var glassId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var finishId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var item = RequirementTechnicalProposalItem.CreateManual(
            Guid.NewGuid(),
            2,
            " M-02 ",
            " Ventana manual ",
            StructuredElementType.Window,
            3,
            1500,
            2400,
            systemId,
            glassId,
            finishId,
            UserId,
            At,
            " Agregada por plano ");

        Assert.Equal(TechnicalProposalItemSource.Manual, item.Source);
        Assert.Null(item.RequirementExtractedItemId);
        Assert.Equal(2, item.Sequence);
        Assert.Equal("M-02", item.Reference);
        Assert.Equal("Ventana manual", item.Description);
        Assert.Equal(3, item.EffectiveQuantity);
        Assert.Equal(1500, item.EffectiveWidthMillimeters);
        Assert.Equal(2400, item.EffectiveHeightMillimeters);
        Assert.Equal(systemId, item.SelectedSystemId);
        Assert.Equal(glassId, item.SelectedGlassTypeId);
        Assert.Equal(finishId, item.SelectedFinishTypeId);
        Assert.True(item.IsIncluded);
        Assert.Equal("Agregada por plano", item.ManualNote);
    }

    private static RequirementTechnicalProposalItem Item() =>
        RequirementTechnicalProposalItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "A",
            "Ventana",
            StructuredElementType.Window,
            1,
            1000,
            1000,
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
