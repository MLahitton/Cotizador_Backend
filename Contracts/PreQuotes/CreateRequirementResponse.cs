namespace Contracts.PreQuotes;

public sealed record CreateRequirementResponse(
    Guid RequirementId,
    Guid PreQuoteId,
    int FileCount,
    string CommercialLine,
    string Status,
    bool CanEditDocuments,
    bool CanCancel,
    bool CanReplace,
    bool IsCurrent,
    Guid? SupersedesRequirementId,
    Guid? SupersededByRequirementId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<RequirementDocumentResponse> Documents);

public sealed record RequirementLifecycleResponse(
    Guid RequirementId,
    Guid PreQuoteId,
    int FileCount,
    string? CommercialLine,
    string Status,
    bool CanEditDocuments,
    bool CanCancel,
    bool CanReplace,
    bool IsCurrent,
    Guid? SupersedesRequirementId,
    Guid? SupersededByRequirementId,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RequirementDocumentResponse> Documents);
