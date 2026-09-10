using Application.Common.Abstractions.Catalogs;
using Domain.Catalogs;
using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public sealed record RequirementTechnicalProposalProposedState(
    SgTechnicalSelectionInput SelectionInput,
    Guid? SystemId,
    Guid? GlassTypeId,
    Guid? FinishTypeId,
    int? Quantity,
    int? WidthMillimeters,
    int? HeightMillimeters,
    string ConfigurationSource,
    string MissingSystemCode,
    string MissingGlassCode,
    string MissingFinishCode)
{
    public bool HasFunctionalTypeMismatch(ProductSystemCatalogReadModel system) =>
        SgFunctionalCompatibilityEvaluator.Evaluate(
            SelectionInput,
            system).IsIncompatible;

    public bool HasHardConstraintFailure(
        ProductSystemCatalogReadModel system,
        ISgProductSystemConstraintEvaluator constraintEvaluator) =>
        constraintEvaluator.Evaluate(
                system,
                SelectionInput,
                ConstraintEvaluationStage.PreSelection)
            .HasHardFailure;

    public static RequirementTechnicalProposalProposedState FromExisting(
        RequirementTechnicalProposalItem item,
        bool? confirmSuggested,
        Guid? systemId,
        Guid? glassTypeId,
        Guid? finishTypeId,
        int? quantity,
        int? widthMillimeters,
        int? heightMillimeters,
        string? requestedCommercialLine = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        var baseConfiguration = EffectiveConfiguration(item, confirmSuggested);
        var input = RequirementFunctionalSelectionContextResolver.ResolveInput(
            item,
            requestedCommercialLine);
        var proposedWidth = widthMillimeters ?? input.WidthMillimeters;
        var proposedHeight = heightMillimeters ?? input.HeightMillimeters;

        return new RequirementTechnicalProposalProposedState(
            input with
            {
                WidthMillimeters = proposedWidth,
                HeightMillimeters = proposedHeight,
                AreaSquareMeters = AreaSquareMeters(proposedWidth, proposedHeight)
                    ?? input.AreaSquareMeters,
                RequestedCommercialLine = requestedCommercialLine
            },
            systemId ?? baseConfiguration.SystemId,
            glassTypeId ?? baseConfiguration.GlassTypeId,
            finishTypeId ?? baseConfiguration.FinishTypeId,
            quantity ?? item.EffectiveQuantity,
            proposedWidth,
            proposedHeight,
            baseConfiguration.Source,
            baseConfiguration.MissingSystemCode,
            baseConfiguration.MissingGlassCode,
            baseConfiguration.MissingFinishCode);
    }

    public static RequirementTechnicalProposalProposedState FromManual(
        StructuredElementType elementType,
        int quantity,
        int widthMillimeters,
        int heightMillimeters,
        Guid systemId,
        Guid glassTypeId,
        Guid finishTypeId,
        string? description,
        string? requestedCommercialLine = null)
    {
        var area = AreaSquareMeters(widthMillimeters, heightMillimeters);
        return new RequirementTechnicalProposalProposedState(
            new SgTechnicalSelectionInput(
                SgFunctionalCompatibilityEvaluator.FunctionalTypeFromElementType(
                    elementType),
                null,
                widthMillimeters,
                heightMillimeters,
                area,
                null,
                null,
                null,
                null,
                null,
                [],
                null,
                requestedCommercialLine,
                null,
                null,
                null,
                description,
                null,
                false,
                null),
            systemId,
            glassTypeId,
            finishTypeId,
            quantity,
            widthMillimeters,
            heightMillimeters,
            "SELECTED",
            "SELECTED_SYSTEM_MISSING",
            "SELECTED_GLASS_MISSING",
            "SELECTED_FINISH_MISSING");
    }

    private static EffectiveTechnicalConfiguration EffectiveConfiguration(
        RequirementTechnicalProposalItem item,
        bool? confirmSuggested)
    {
        if (confirmSuggested == true || !item.HasSelectedConfiguration())
        {
            return new EffectiveTechnicalConfiguration(
                "SUGGESTED",
                item.SuggestedSystemId,
                item.SuggestedGlassTypeId,
                item.SuggestedFinishTypeId,
                "SYSTEM_NOT_RESOLVED",
                "GLASS_NOT_RESOLVED",
                "FINISH_NOT_RESOLVED");
        }

        return new EffectiveTechnicalConfiguration(
            "SELECTED",
            item.SelectedSystemId,
            item.SelectedGlassTypeId,
            item.SelectedFinishTypeId,
            "SELECTED_SYSTEM_MISSING",
            "SELECTED_GLASS_MISSING",
            "SELECTED_FINISH_MISSING");
    }

    private static decimal? AreaSquareMeters(
        int? widthMillimeters,
        int? heightMillimeters) =>
        widthMillimeters is > 0 && heightMillimeters is > 0
            ? widthMillimeters.Value * heightMillimeters.Value / 1_000_000m
            : null;

    private sealed record EffectiveTechnicalConfiguration(
        string Source,
        Guid? SystemId,
        Guid? GlassTypeId,
        Guid? FinishTypeId,
        string MissingSystemCode,
        string MissingGlassCode,
        string MissingFinishCode);
}
