namespace Application.PreQuotes.CreateRequirement;

public sealed record CreateRequirementCommand(
    Guid PreQuoteId,
    IReadOnlyList<CreateRequirementFileInput> Files);

public sealed record CreateRequirementFileInput(
    string? OriginalFileName,
    string? ContentType,
    long SizeBytes,
    Stream? Content);
