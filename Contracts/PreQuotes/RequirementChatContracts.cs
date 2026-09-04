namespace Contracts.PreQuotes;

public sealed record RequirementChatResponse(
    Guid ThreadId,
    Guid RequirementId,
    Guid? TechnicalProposalItemId,
    string Scope,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RequirementChatMessageResponse> Messages,
    RequirementChatInteractionResponse? LastInteraction = null);

public sealed record RequirementChatMessageResponse(
    Guid MessageId,
    string Role,
    string Content,
    int Sequence,
    DateTimeOffset CreatedAtUtc);

public sealed record SendRequirementChatMessageRequest(string Message);

public sealed record RequirementChatInteractionResponse(
    string MessageType,
    Guid? PlanId,
    bool RequiresConfirmation,
    string? ActionType,
    RequirementChatActionTargetResponse? Target,
    string? CurrentValue,
    string? RequestedValue,
    string? PricingImpactExpected,
    string? PricingStatus,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<RequirementChatInteractionOptionResponse> AvailableOptions);

public sealed record RequirementChatActionTargetResponse(
    Guid? TechnicalProposalItemId,
    string? Reference);

public sealed record RequirementChatInteractionOptionResponse(
    Guid? Id,
    string? Code,
    string DisplayName,
    string OptionType);
