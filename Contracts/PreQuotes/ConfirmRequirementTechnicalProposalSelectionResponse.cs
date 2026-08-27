namespace Contracts.PreQuotes;

public sealed record ConfirmRequirementTechnicalProposalSelectionResponse(
    Guid TechnicalProposalId,
    string State,
    DateTimeOffset? ConfirmedAtUtc,
    Guid? ConfirmedByUserId);