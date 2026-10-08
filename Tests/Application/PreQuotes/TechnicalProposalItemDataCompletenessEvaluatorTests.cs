using System.Reflection;
using Application.PreQuotes.TechnicalProposalDataCompleteness;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class TechnicalProposalItemDataCompletenessEvaluatorTests
{
    private static readonly DateTimeOffset At =
        new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 1000, 1200, "COMPLETE")]
    [InlineData(null, 1000, 1200, "INCOMPLETE", "QUANTITY")]
    [InlineData(1, null, 1200, "INCOMPLETE", "WIDTH")]
    [InlineData(1, 1000, null, "INCOMPLETE", "HEIGHT")]
    [InlineData(1, null, null, "INCOMPLETE", "WIDTH", "HEIGHT")]
    [InlineData(null, null, null, "INCOMPLETE", "QUANTITY", "WIDTH", "HEIGHT")]
    public void Evaluate_WithEffectiveData_ReturnsExpectedCompleteness(
        int? quantity,
        int? widthMillimeters,
        int? heightMillimeters,
        string expectedState,
        params string[] expectedMissingFields)
    {
        var item = ProposalItem(quantity, widthMillimeters, heightMillimeters);

        var completeness = TechnicalProposalItemDataCompletenessEvaluator.Evaluate(item);

        Assert.Equal(expectedState, completeness.State);
        Assert.Equal(expectedMissingFields.Length == 0, completeness.IsComplete);
        Assert.Equal(expectedMissingFields, completeness.MissingFields);
    }

    [Fact]
    public void Evaluate_WithManualQuantityOverride_UsesEffectiveQuantity()
    {
        var item = ProposalItem(null, 1000, 1200);
        item.ApplyManualDataOverride(2, null, null);

        var completeness = TechnicalProposalItemDataCompletenessEvaluator.Evaluate(item);

        Assert.Equal("COMPLETE", completeness.State);
        Assert.True(completeness.IsComplete);
        Assert.Empty(completeness.MissingFields);
    }

    [Fact]
    public void Evaluate_WithManualWidthOverride_UsesEffectiveWidth()
    {
        var item = ProposalItem(1, null, 1200);
        item.ApplyManualDataOverride(null, 1000, null);

        var completeness = TechnicalProposalItemDataCompletenessEvaluator.Evaluate(item);

        Assert.Equal("COMPLETE", completeness.State);
        Assert.True(completeness.IsComplete);
        Assert.DoesNotContain("WIDTH", completeness.MissingFields);
    }

    [Fact]
    public void Evaluate_WithManualHeightOverride_UsesEffectiveHeight()
    {
        var item = ProposalItem(1, 1000, null);
        item.ApplyManualDataOverride(null, null, 1200);

        var completeness = TechnicalProposalItemDataCompletenessEvaluator.Evaluate(item);

        Assert.Equal("COMPLETE", completeness.State);
        Assert.True(completeness.IsComplete);
        Assert.DoesNotContain("HEIGHT", completeness.MissingFields);
    }

    private static RequirementTechnicalProposalItem ProposalItem(
        int? quantity,
        int? widthMillimeters,
        int? heightMillimeters)
    {
        var extracted = RequirementExtractedItem.Create(
            Guid.NewGuid(),
            "element-1",
            1,
            "V-01",
            "Ventana",
            StructuredElementType.Window,
            quantity,
            widthMillimeters,
            heightMillimeters,
            null,
            0.90m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            "FIXED",
            "FIXED",
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            "RECTANGULAR",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            At);
        var item = RequirementTechnicalProposalItem.Create(
            Guid.NewGuid(),
            extracted.Id,
            null,
            null,
            null,
            extracted.Sequence,
            extracted.Reference,
            extracted.Description,
            extracted.ElementType,
            extracted.Quantity,
            extracted.WidthMillimeters,
            extracted.HeightMillimeters,
            0.90m,
            0.90m,
            0.90m,
            0.90m,
            false,
            false,
            false,
            [],
            [],
            [],
            [],
            0,
            null,
            null,
            "NotEvaluated",
            At);
        SetPrivateProperty(item, "ExtractedItem", extracted);
        return item;
    }

    private static void SetPrivateProperty<T>(
        object target,
        string propertyName,
        T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        property!.SetValue(target, value);
    }
}