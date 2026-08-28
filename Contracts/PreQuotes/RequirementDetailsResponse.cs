namespace Contracts.PreQuotes;

public sealed record RequirementDocumentResponse(
    Guid RequirementFileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);

public sealed record RequirementDetailsResponse(
    Guid RequirementId,
    Guid PreQuoteId,
    string Status,
    string? CommercialLine,
    bool CanEditDocuments,
    bool CanCancel,
    bool CanReplace,
    bool IsCurrent,
    Guid? SupersedesRequirementId,
    Guid? SupersededByRequirementId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RequirementDocumentResponse> Documents);
