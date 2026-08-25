namespace Application.PreQuotes.CreateRequirement;

public sealed record CreateRequirementCommand(
    Guid PreQuoteId,
    string? CommercialLine,
    IReadOnlyList<CreateRequirementFileInput> Files);

public sealed record CreateRequirementFileInput(
    string? OriginalFileName,
    string? ContentType,
    long SizeBytes,
    Stream? Content);
