namespace Contracts.PreQuotes;

public sealed record UpdateRequirementTechnicalProposalItemSelectionRequest(
    bool? ConfirmSuggested,
    Guid? SystemId,
    Guid? GlassId,
    Guid? FinishId,
    int? Quantity = null,
    int? WidthMm = null,
    int? HeightMm = null);

public sealed record UpdateRequirementTechnicalProposalItemSelectionResponse(
    Guid TechnicalProposalId,
    Guid ItemId,
    string SelectionState,
    DateTimeOffset? SelectedAtUtc,
    Guid? SelectedByUserId,
    RequirementTechnicalProposalSystemOptionResponse? System,
    RequirementTechnicalProposalGlassOptionResponse? Glass,
    RequirementTechnicalProposalFinishOptionResponse? Finish);
