namespace Contracts.PreQuotes;

public sealed record PlanRequirementChatActionRequest(
    string? Scope,
    string ActionType,
    Guid? TargetTechnicalProposalItemId,
    string? TargetReference,
    IReadOnlyList<string>? TargetReferences,
    string? RequestedValue,
    int? Quantity = null,
    int? WidthMm = null,
    int? HeightMm = null,
    string? RawUserMessage = null);

public sealed record ChatActionPlanResponse(
    Guid PlanId,
    Guid RequirementId,
    Guid TechnicalProposalId,
    string Scope,
    string Status,
    bool RequiresConfirmation,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? PricingStatus,
    IReadOnlyList<string> ExecutionReasons,
    IReadOnlyList<ChatActionPlanActionResponse> Actions);

public sealed record ChatActionPlanActionResponse(
    Guid ActionId,
    string ActionType,
    Guid? TargetTechnicalProposalItemId,
    string? TargetReference,
    string? RequestedValue,
    string? CurrentValue,
    ChatActionResolvedCatalogEntityResponse? ResolvedCatalogEntity,
    string ValidationState,
    IReadOnlyList<string> ValidationReasons,
    bool RequiresConfirmation,
    IReadOnlyList<ChatActionOptionResponse> AvailableOptions);

public sealed record ChatActionResolvedCatalogEntityResponse(
    Guid Id,
    string Code,
    string DisplayName,
    string EntityType);

public sealed record ChatActionOptionResponse(
    Guid? Id,
    string? Code,
    string DisplayName,
    string OptionType);
