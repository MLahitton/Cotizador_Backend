using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class RequirementFunctionalSelectionContextResolverTests
{
    private static readonly Guid ExtractionResultId = Guid.Parse(
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset Now = new(
        2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("FIXED", null, "FIXED")]
    [InlineData("PROJECTING", "PROJECTING", "PROJECTING")]
    [InlineData("SWING_DOOR", "SWING", "SWING_DOOR")]
    [InlineData("SLIDING_WINDOW", "SLIDING", "SLIDING_WINDOW")]
    [InlineData("SLIDING_DOOR", "SLIDING", "SLIDING_DOOR")]
    [InlineData("FOLDING_WINDOW", "FOLDING", "FOLDING_WINDOW")]
    [InlineData("GRILLE", null, "GRILLE")]
    public void ResolveInput_WithSimpleFunctionalTypes_PreservesFunction(
        string functionalType,
        string? operation,
        string expected)
    {
        var item = ExtractedItem(functionalType, operation);

        var input = RequirementFunctionalSelectionContextResolver.Resolve(item)
            .Input;

        Assert.Equal(expected,
            SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(input));
    }

    [Theory]
    [InlineData("PROJECTING", "PROJECTING", "FIXED", "PROJECTING")]
    [InlineData("SWING_DOOR", "SWING", "FIXED", "SWING_DOOR")]
    [InlineData("SLIDING_WINDOW", "SLIDING", "FIXED", "SLIDING_WINDOW")]
    [InlineData("SLIDING_DOOR", "SLIDING", "FIXED", "SLIDING_DOOR")]
    [InlineData("FOLDING_WINDOW", "FOLDING", "FIXED", "FOLDING_WINDOW")]
    public void ResolveInput_WithSingleMovableAndFixed_PreservesMovable(
        string functionalType,
        string operation,
        string secondaryRole,
        string expected)
    {
        var item = ExtractedItem(functionalType, operation, height: 2400);
        AddSegment(item, 1, operation, height: 1600);
        AddSegment(item, 2, secondaryRole, height: 800);

        var input = RequirementFunctionalSelectionContextResolver.Resolve(item)
            .Input;

        Assert.Equal(expected,
            SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(input));
        Assert.Equal(1600, input.PrimaryComponentHeightMillimeters);
        Assert.True(input.HasCompositeGeometry);
    }

    [Theory]
    [InlineData("PROJECTING", "PROJECTING")]
    [InlineData("SWING_DOOR", "SWING")]
    [InlineData("SLIDING_WINDOW", "SLIDING")]
    public void ResolveInput_WithInvertedSegmentOrder_PreservesSameMovable(
        string functionalType,
        string operation)
    {
        var item = ExtractedItem(functionalType, operation, height: 2400);
        AddSegment(item, 1, "FIXED", height: 800);
        AddSegment(item, 2, operation, height: 1600);

        var input = RequirementFunctionalSelectionContextResolver.Resolve(item)
            .Input;

        Assert.Equal(functionalType,
            SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(input));
        Assert.Equal(1600, input.PrimaryComponentHeightMillimeters);
    }

    [Fact]
    public void ResolveInput_WithMultipleFixedComponents_ResolvesFixed()
    {
        var item = ExtractedItem("FIXED", "FIXED");
        AddSegment(item, 1, "FIXED", height: 900);
        AddSegment(item, 2, "FIXED", height: 1200);

        var context = RequirementFunctionalSelectionContextResolver.Resolve(item);

        Assert.Equal("FIXED",
            SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(
                context.Input));
        Assert.Equal("FIXED", context.PrimaryComponent.PrimaryRole);
    }

    [Fact]
    public void ResolveInput_WithMultipleMovableTypes_PreservesReviewAmbiguity()
    {
        var item = ExtractedItem("WINDOW", null);
        AddSegment(item, 1, "PROJECTING", height: 1000);
        AddSegment(item, 2, "SLIDING", height: 1200);

        var context = RequirementFunctionalSelectionContextResolver.Resolve(item);

        Assert.False(context.PrimaryComponent.OverridesItem);
        Assert.Contains(
            SgTechnicalSelectionReviewReasons
                .AssemblyMultipleMovableTypesRequiresReview,
            context.PrimaryComponent.ReviewReasons);
    }

    [Fact]
    public void Evaluator_WithProposalItemUsesSharedCompoundContext()
    {
        var extracted = ExtractedItem("SWING_DOOR", "SWING", height: 2400);
        AddSegment(extracted, 1, "SWING", height: 1700);
        AddSegment(extracted, 2, "FIXED", height: 700);
        var item = ProposalItem(extracted, SystemId("suggested"));
        item.AttachExtractedItem(extracted);

        var input = SgFunctionalCompatibilityEvaluator.ToSelectionInput(item);
        var compatibility = SgFunctionalCompatibilityEvaluator.Evaluate(
            item,
            ProductSystem(SystemId("swing"), "SWING_DOOR"));

        Assert.Equal("SWING_DOOR",
            SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(input));
        Assert.True(input.HasCompositeGeometry);
        Assert.Equal(1700, input.PrimaryComponentHeightMillimeters);
        Assert.True(compatibility.IsCompatible);
    }

    [Fact]
    public void Evaluator_WithRealMismatchStillBlocks()
    {
        var extracted = ExtractedItem("FIXED", "FIXED");
        var item = ProposalItem(extracted, SystemId("suggested"));
        item.AttachExtractedItem(extracted);

        var compatibility = SgFunctionalCompatibilityEvaluator.Evaluate(
            item,
            ProductSystem(SystemId("sliding-door"), "SLIDING_DOOR"));

        Assert.True(compatibility.IsIncompatible);
        Assert.Equal(
            SgFunctionalCompatibilityEvaluator
                .TechnicalProposalFunctionalTypeMismatch,
            compatibility.ReasonCode);
    }

    [Fact]
    public void ResolveInput_GenericSlidingWindowAboveThresholdKeepsInference()
    {
        var item = ExtractedItem("WINDOW", "SLIDING", height: 2700);

        var input = RequirementFunctionalSelectionContextResolver.Resolve(item)
            .Input;

        Assert.Equal("SLIDING_DOOR",
            SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(input));
        Assert.Contains(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            SgFunctionalCompatibilityEvaluator.FunctionalResolutionReasons(
                input));
    }

    [Theory]
    [InlineData(2400)]
    [InlineData(2600)]
    [InlineData(2601)]
    [InlineData(2700)]
    public void ResolveInput_ExplicitSlidingWindowPreservesFamily(
        int height)
    {
        var item = ExtractedItem("SLIDING_WINDOW", "SLIDING", height: height);

        var input = RequirementFunctionalSelectionContextResolver.Resolve(item)
            .Input;

        Assert.Equal("SLIDING_WINDOW",
            SgFunctionalCompatibilityEvaluator.EffectiveFunctionalType(input));
        Assert.DoesNotContain(
            SgTechnicalSelectionRuleCodes.WindowHeightOver2600AsDoor,
            SgFunctionalCompatibilityEvaluator.FunctionalResolutionReasons(
                input));
    }

    private static RequirementExtractedItem ExtractedItem(
        string functionalType,
        string? operation,
        int height = 2400) =>
        RequirementExtractedItem.Create(
            ExtractionResultId,
            null,
            1,
            "V-TEST",
            "Elemento de prueba",
            functionalType is "SLIDING_WINDOW" or "FOLDING_WINDOW" or "WINDOW"
                ? StructuredElementType.Window
                : StructuredElementType.Door,
            1,
            1000,
            height,
            2.4m,
            0.9m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            functionalType,
            operation,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
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
            Now);

    private static void AddSegment(
        RequirementExtractedItem item,
        int sequence,
        string role,
        int height) =>
        item.AddSegment(RequirementExtractedItemSegment.Create(
            item.Id,
            sequence,
            role,
            1000,
            height,
            1,
            role,
            null,
            role,
            null,
            null,
            null,
            null,
            null,
            0.9m,
            RequirementExtractionValueStatus.Explicit,
            Now));

    private static RequirementTechnicalProposalItem ProposalItem(
        RequirementExtractedItem extracted,
        Guid suggestedSystemId) =>
        RequirementTechnicalProposalItem.Create(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            extracted.Id,
            suggestedSystemId,
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            extracted.Sequence,
            extracted.Reference,
            extracted.Description,
            extracted.ElementType,
            extracted.Quantity,
            extracted.WidthMillimeters,
            extracted.HeightMillimeters,
            0.9m,
            0.9m,
            0.9m,
            0.9m,
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
            "UNAVAILABLE",
            Now);

    private static ProductSystemCatalogReadModel ProductSystem(
        Guid id,
        string functionalType) =>
        new(
            id,
            functionalType,
            $"Sistema {functionalType}",
            $"Sistema tecnico {functionalType}",
            functionalType,
            functionalType,
            functionalType,
            "SERIE",
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private static Guid SystemId(string value)
    {
        var bytes = new byte[16];
        var source = global::System.Text.Encoding.UTF8.GetBytes(value);
        Array.Copy(source, bytes, Math.Min(bytes.Length, source.Length));
        return new Guid(bytes);
    }
}
