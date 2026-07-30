using Domain.PreQuotes;
namespace Application.PreQuotes.UpdatePreQuoteDraft;
public sealed record UpdatePreQuoteDraftCommand(
    Guid PreQuoteId, int ExpectedVersion,
    string? ProjectName, string? ClientName, string? Location,
    IReadOnlyList<PreQuoteDraftItemEdit> Items,
    IReadOnlyList<PreQuoteDraftRequirementEdit> Requirements,
    IReadOnlyList<PreQuoteDraftReferenceEdit> DocumentReferences,
    IReadOnlyList<PreQuoteDraftResolutionEdit> Issues,
    IReadOnlyList<PreQuoteDraftResolutionEdit> Conflicts);
