namespace Contracts.PreQuotes;

public sealed record UpdateRequirementTechnicalProposalItemSelectionRequest(
    bool? ConfirmSuggested,
    Guid? SystemId,
    Guid? GlassId,
    Guid? FinishId);

public sealed record UpdateRequirementTechnicalProposalItemSelectionResponse(
    Guid TechnicalProposalId,
    Guid ItemId,
    string SelectionState,
    DateTimeOffset? SelectedAtUtc,
    Guid? SelectedByUserId,
    RequirementTechnicalProposalSystemOptionResponse? System,
    RequirementTechnicalProposalGlassOptionResponse? Glass,
    RequirementTechnicalProposalFinishOptionResponse? Finish);
