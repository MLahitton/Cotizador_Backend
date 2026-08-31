namespace Contracts.PreQuotes;

public sealed record RequirementChatResponse(
    Guid ThreadId,
    Guid RequirementId,
    Guid? TechnicalProposalItemId,
    string Scope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RequirementChatMessageResponse> Messages);

public sealed record RequirementChatMessageResponse(
    Guid MessageId,
    string Role,
    string Content,
    int Sequence,
    DateTimeOffset CreatedAtUtc);

public sealed record SendRequirementChatMessageRequest(string Message);
