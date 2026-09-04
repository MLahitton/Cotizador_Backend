namespace Contracts.PreQuotes;

public sealed record CreateManualRequirementTechnicalProposalItemRequest(
    string Reference,
    string? Description,
    string ElementType,
    int Quantity,
    int WidthMillimeters,
    int HeightMillimeters,
    Guid SystemId,
    Guid GlassTypeId,
    Guid FinishTypeId,
    string? Note = null);

public sealed record CreateManualRequirementTechnicalProposalItemResponse(
    Guid TechnicalProposalId,
    Guid ItemId,
    string Source,
    int Sequence,
    long CommercialRevision);
