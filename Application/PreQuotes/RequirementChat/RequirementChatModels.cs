namespace Application.PreQuotes.RequirementChat;

public sealed record RequirementChatMessageReadModel(
    Guid MessageId,
    string Role,
    string Content,
    int Sequence,
    DateTimeOffset CreatedAtUtc);

public sealed record RequirementChatThreadReadModel(
    Guid ThreadId,
    Guid RequirementId,
    Guid? TechnicalProposalItemId,
    string Scope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RequirementChatMessageReadModel> Messages);
