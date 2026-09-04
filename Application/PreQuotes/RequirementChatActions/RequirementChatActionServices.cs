using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Application.PreQuotes.PriceRequirementTechnicalProposal;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemInclusion;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemSelection;

namespace Application.PreQuotes.RequirementChatActions;

public sealed record PlanRequirementChatActionCommand(
    Guid RequirementId,
    Guid? ChatThreadId,
    Guid? ExistingPlanId,
    Guid? ContextTechnicalProposalItemId,
    string? Scope,
    string ActionType,
    Guid? TargetTechnicalProposalItemId,
    string? TargetReference,
    string? RequestedValue,
    int? Quantity,
    int? WidthMillimeters,
    int? HeightMillimeters,
    string? RawUserMessage);

public sealed record ConfirmRequirementChatActionCommand(
    Guid RequirementId,
    Guid PlanId);

public enum RequirementChatActionFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    RequirementNotFound,
    PreQuoteNotFound,
    ProjectNotFound,
    InactiveProject,
    ClientNotFound,
    InactiveClient,
    TechnicalProposalNotFound,
    PlanNotFound,
    QueryError,
    PersistenceError
}

public sealed record RequirementChatActionPlanResult(
    bool IsSuccess,
    RequirementChatActionFailure Failure,
    ChatActionPlanReadModel? Plan)
{
    public static RequirementChatActionPlanResult Success(ChatActionPlanReadModel plan) =>
        new(true, RequirementChatActionFailure.None, plan);

    public static RequirementChatActionPlanResult Failed(RequirementChatActionFailure failure) =>
        new(false, failure, null);
}

public sealed record ChatActionPlanReadModel(
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
    IReadOnlyList<ChatActionPlanActionReadModel> Actions,
    Guid? ChatThreadId = null);

public sealed record ChatActionPlanActionReadModel(
    Guid ActionId,
    string ActionType,
    Guid? TargetTechnicalProposalItemId,
    string? TargetReference,
    string? RequestedValue,
    string? CurrentValue,
    ChatActionResolvedCatalogEntityReadModel? ResolvedCatalogEntity,
    string ValidationState,
    IReadOnlyList<string> ValidationReasons,
    bool RequiresConfirmation,
    IReadOnlyList<ChatActionOptionReadModel> AvailableOptions);

public sealed record ChatActionResolvedCatalogEntityReadModel(
    Guid Id,
    string Code,
    string DisplayName,
    string EntityType);

public sealed record ChatActionOptionReadModel(
    Guid? Id,
    string? Code,
    string DisplayName,
    string OptionType);

public interface IRequirementChatActionPlanStore
{
    void Save(ChatActionPlanReadModel plan);
    ChatActionPlanReadModel? Find(Guid requirementId, Guid planId);
    ChatActionPlanReadModel? FindPendingClarification(
        Guid requirementId,
        string scope,
        Guid? technicalProposalItemId,
        Guid chatThreadId);
    ChatActionPlanReadModel? StartExecution(Guid requirementId, Guid planId);
}

public interface IRequirementChatTechnicalProposalReader
{
    Task<GetRequirementTechnicalProposalResult> GetAsync(
        Guid requirementId,
        CancellationToken cancellationToken);
}

public interface IRequirementChatSelectionExecutor
{
    Task<UpdateRequirementTechnicalProposalItemSelectionResult> ExecuteAsync(
        UpdateRequirementTechnicalProposalItemSelectionCommand command,
        CancellationToken cancellationToken);
}

public interface IRequirementChatInclusionExecutor
{
    Task<UpdateRequirementTechnicalProposalItemInclusionResult> ExecuteAsync(
        UpdateRequirementTechnicalProposalItemInclusionCommand command,
        CancellationToken cancellationToken);
}

public interface IRequirementChatPricingExecutor
{
    Task<PriceRequirementTechnicalProposalResult> PriceRequirementAsync(
        PriceRequirementTechnicalProposalCommand command,
        CancellationToken cancellationToken);

    Task<RepriceRequirementTechnicalProposalItemResult> RepriceItemAsync(
        RepriceRequirementTechnicalProposalItemCommand command,
        CancellationToken cancellationToken);
}

public sealed class InMemoryRequirementChatActionPlanStore(TimeProvider timeProvider)
    : IRequirementChatActionPlanStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid RequirementId, Guid PlanId), ChatActionPlanReadModel> _plans = [];

    public void Save(ChatActionPlanReadModel plan)
    {
        lock (_gate)
        {
            _plans[(plan.RequirementId, plan.PlanId)] = plan;
        }
    }

    public ChatActionPlanReadModel? Find(Guid requirementId, Guid planId)
    {
        lock (_gate)
        {
            return FindCore(requirementId, planId);
        }
    }

    public ChatActionPlanReadModel? FindPendingClarification(
        Guid requirementId,
        string scope,
        Guid? technicalProposalItemId,
        Guid chatThreadId)
    {
        lock (_gate)
        {
            return _plans.Values
                .Where(plan => plan.RequirementId == requirementId)
                .Select(plan => FindCore(plan.RequirementId, plan.PlanId))
                .Where(plan => plan is
                {
                    Status: "NEEDS_CLARIFICATION",
                    ChatThreadId: not null
                })
                .Where(plan => plan!.ChatThreadId == chatThreadId)
                .Where(plan => string.Equals(
                    plan!.Scope,
                    scope,
                    StringComparison.OrdinalIgnoreCase))
                .Where(plan =>
                {
                    var action = plan!.Actions.SingleOrDefault();
                    return action is not null
                        && (scope != "ITEM"
                            || action.TargetTechnicalProposalItemId
                                == technicalProposalItemId);
                })
                .OrderByDescending(plan => plan!.CreatedAtUtc)
                .FirstOrDefault();
        }
    }

    public ChatActionPlanReadModel? StartExecution(Guid requirementId, Guid planId)
    {
        lock (_gate)
        {
            var plan = FindCore(requirementId, planId);
            if (plan?.Status != "READY_FOR_CONFIRMATION")
            {
                return plan;
            }

            _plans[(requirementId, planId)] = plan with
            {
                Status = "EXECUTING",
                RequiresConfirmation = false
            };
            return plan;
        }
    }

    private ChatActionPlanReadModel? FindCore(Guid requirementId, Guid planId)
    {
        if (!_plans.TryGetValue((requirementId, planId), out var plan))
        {
            return null;
        }

        if (plan.ExpiresAtUtc is { } expires
            && expires <= timeProvider.GetUtcNow()
            && plan.Status is not "EXECUTED" and not "EXECUTED_WITH_PRICING_PENDING" and not "EXECUTING")
        {
            var expired = plan with { Status = "EXPIRED", RequiresConfirmation = false };
            _plans[(requirementId, planId)] = expired;
            return expired;
        }

        return plan;
    }
}

public sealed class RequirementChatTechnicalProposalReader(
    GetRequirementTechnicalProposalService service)
    : IRequirementChatTechnicalProposalReader
{
    public Task<GetRequirementTechnicalProposalResult> GetAsync(
        Guid requirementId,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(requirementId),
            cancellationToken);
}

public sealed class RequirementChatSelectionExecutor(
    UpdateRequirementTechnicalProposalItemSelectionService service)
    : IRequirementChatSelectionExecutor
{
    public Task<UpdateRequirementTechnicalProposalItemSelectionResult> ExecuteAsync(
        UpdateRequirementTechnicalProposalItemSelectionCommand command,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(command, cancellationToken);
}

public sealed class RequirementChatInclusionExecutor(
    UpdateRequirementTechnicalProposalItemInclusionService service)
    : IRequirementChatInclusionExecutor
{
    public Task<UpdateRequirementTechnicalProposalItemInclusionResult> ExecuteAsync(
        UpdateRequirementTechnicalProposalItemInclusionCommand command,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(command, cancellationToken);
}

public sealed class RequirementChatPricingExecutor(
    PriceRequirementTechnicalProposalService service)
    : IRequirementChatPricingExecutor
{
    public Task<PriceRequirementTechnicalProposalResult> PriceRequirementAsync(
        PriceRequirementTechnicalProposalCommand command,
        CancellationToken cancellationToken) =>
        service.ExecuteAsync(command, cancellationToken);

    public Task<RepriceRequirementTechnicalProposalItemResult> RepriceItemAsync(
        RepriceRequirementTechnicalProposalItemCommand command,
        CancellationToken cancellationToken) =>
        service.RepriceItemAsync(command, cancellationToken);
}

public sealed class PlanRequirementChatActionService(
    IRequirementChatTechnicalProposalReader technicalProposalReader,
    IProductSystemCatalogRepository productSystems,
    IGlassTypeCatalogRepository glassTypes,
    IFinishTypeCatalogRepository finishes,
    IRequirementChatActionPlanStore store,
    TimeProvider timeProvider)
{
    public async Task<RequirementChatActionPlanResult> ExecuteAsync(
        PlanRequirementChatActionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.ActionType))
        {
            return RequirementChatActionPlanResult.Failed(
                RequirementChatActionFailure.InvalidRequest);
        }

        var proposalResult = await technicalProposalReader.GetAsync(
            command.RequirementId,
            cancellationToken);
        if (!proposalResult.IsSuccess || proposalResult.Proposal is null)
        {
            return RequirementChatActionPlanResult.Failed(Map(proposalResult.Failure));
        }

        var proposal = proposalResult.Proposal;
        var scope = NormalizeScope(command.Scope, command.ContextTechnicalProposalItemId);
        var actionType = Normalize(command.ActionType);
        var now = timeProvider.GetUtcNow();
        var action = await BuildActionAsync(
            command,
            proposal,
            scope,
            actionType,
            cancellationToken);
        var status = action.ValidationState == "VALID"
            ? "READY_FOR_CONFIRMATION"
            : action.ValidationState;
        var plan = new ChatActionPlanReadModel(
            command.ExistingPlanId ?? Guid.NewGuid(),
            command.RequirementId,
            proposal.TechnicalProposalId,
            scope,
            status,
            status == "READY_FOR_CONFIRMATION",
            now,
            now.AddMinutes(15),
            null,
            [],
            [action],
            command.ChatThreadId);
        store.Save(plan);
        return RequirementChatActionPlanResult.Success(plan);
    }

    private async Task<ChatActionPlanActionReadModel> BuildActionAsync(
        PlanRequirementChatActionCommand command,
        RequirementTechnicalProposalReadModel proposal,
        string scope,
        string actionType,
        CancellationToken cancellationToken)
    {
        if (actionType == "CHANGE_COMMERCIAL_LINE")
        {
            return Invalid(actionType, command, "CHANGE_COMMERCIAL_LINE_NOT_SUPPORTED_YET");
        }

        var target = ResolveTarget(command, proposal, scope);
        if (target.State != "VALID")
        {
            return new ChatActionPlanActionReadModel(
                Guid.NewGuid(),
                actionType,
                null,
                command.TargetReference,
                RequestedValue(command, actionType),
                null,
                null,
                target.State,
                target.Reasons,
                false,
                target.Options);
        }

        var item = target.Item!;
        var resolved = actionType switch
        {
            "CHANGE_SYSTEM" => await ResolveSystemAsync(command.RequestedValue, cancellationToken),
            "CHANGE_GLASS" => await ResolveGlassAsync(command.RequestedValue, cancellationToken),
            "CHANGE_FINISH" => await ResolveFinishAsync(command.RequestedValue, cancellationToken),
            "CHANGE_QUANTITY" => command.Quantity is > 0
                ? Resolved(null, [])
                : Unresolved("QUANTITY_REQUIRED"),
            "CHANGE_DIMENSIONS" => command.WidthMillimeters is > 0 && command.HeightMillimeters is > 0
                ? Resolved(null, [])
                : Unresolved("DIMENSIONS_REQUIRED"),
            "EXCLUDE_ITEM" or "INCLUDE_ITEM" => Resolved(null, []),
            _ => Unresolved("ACTION_TYPE_UNSUPPORTED")
        };

        return new ChatActionPlanActionReadModel(
            Guid.NewGuid(),
            actionType,
            item.ItemId,
            item.Reference,
            RequestedValue(command, actionType),
            CurrentValue(item, actionType),
            resolved.Entity,
            resolved.Reasons.Count == 0 ? "VALID" : "NEEDS_CLARIFICATION",
            resolved.Reasons,
            resolved.Reasons.Count == 0,
            resolved.Options);
    }

    private static TargetResolution ResolveTarget(
        PlanRequirementChatActionCommand command,
        RequirementTechnicalProposalReadModel proposal,
        string scope)
    {
        var explicitItemId = command.ContextTechnicalProposalItemId
            ?? command.TargetTechnicalProposalItemId;
        if (explicitItemId is { } itemId)
        {
            var item = proposal.Items.SingleOrDefault(value => value.ItemId == itemId);
            return item is null
                ? new("NEEDS_CLARIFICATION", null, ["TARGET_ITEM_NOT_FOUND"], TargetOptions(proposal.Items))
                : new("VALID", item, [], []);
        }

        if (scope == "ITEM")
        {
            return new("NEEDS_CLARIFICATION", null, ["ITEM_SCOPE_REQUIRES_ITEM_ID"], TargetOptions(proposal.Items));
        }

        if (string.IsNullOrWhiteSpace(command.TargetReference))
        {
            return new("NEEDS_CLARIFICATION", null, ["TARGET_REQUIRED"], TargetOptions(proposal.Items));
        }

        var matches = proposal.Items
            .Where(item => string.Equals(
                item.Reference?.Trim(),
                command.TargetReference.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1
            ? new("VALID", matches[0], [], [])
            : new("NEEDS_CLARIFICATION", null,
                matches.Length == 0
                    ? ["TARGET_REFERENCE_NOT_FOUND"]
                    : ["TARGET_REFERENCE_AMBIGUOUS"],
                matches.Length == 0 ? TargetOptions(proposal.Items) : TargetOptions(matches));
    }

    private async Task<CatalogResolution> ResolveSystemAsync(
        string? requestedValue,
        CancellationToken cancellationToken)
    {
        var systems = await productSystems.ListActiveSelectableAsync(cancellationToken);
        return ResolveCatalog(
            requestedValue,
            systems,
            system => system.Id,
            system => system.Code,
            system => system.Name,
            "SYSTEM");
    }

    private async Task<CatalogResolution> ResolveGlassAsync(
        string? requestedValue,
        CancellationToken cancellationToken)
    {
        var glasses = await glassTypes.GetActiveWithCurrentPriceRangesAsync(cancellationToken);
        return ResolveCatalog(
            requestedValue,
            glasses.Where(glass => glass.IsActive && glass.IsSelectable).ToArray(),
            glass => glass.GlassTypeId,
            glass => glass.Code,
            glass => glass.Name,
            "GLASS");
    }

    private async Task<CatalogResolution> ResolveFinishAsync(
        string? requestedValue,
        CancellationToken cancellationToken)
    {
        var values = await finishes.ListActiveAsync(cancellationToken);
        return ResolveCatalog(
            requestedValue,
            values.Where(finish => finish.IsActive && finish.IsSelectable).ToArray(),
            finish => finish.Id,
            finish => finish.Code,
            finish => finish.Name,
            "FINISH");
    }

    private static CatalogResolution ResolveCatalog<T>(
        string? requestedValue,
        IReadOnlyList<T> values,
        Func<T, Guid> id,
        Func<T, string> code,
        Func<T, string> name,
        string entityType)
    {
        if (string.IsNullOrWhiteSpace(requestedValue))
        {
            return Unresolved($"{entityType}_VALUE_REQUIRED", Options(values, id, code, name, entityType));
        }

        var normalized = requestedValue.Trim();
        var matches = values.Where(value =>
                Guid.TryParse(normalized, out var parsed) && id(value) == parsed
                || string.Equals(code(value), normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name(value), normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            return Unresolved(
                matches.Length == 0
                    ? $"{entityType}_NOT_FOUND"
                    : $"{entityType}_AMBIGUOUS",
                Options(matches.Length == 0 ? values : matches, id, code, name, entityType));
        }

        var match = matches[0];
        return Resolved(
            new ChatActionResolvedCatalogEntityReadModel(
                id(match),
                code(match),
                name(match),
                entityType),
            []);
    }

    private static CatalogResolution Resolved(
        ChatActionResolvedCatalogEntityReadModel? entity,
        IReadOnlyList<ChatActionOptionReadModel> options) =>
        new(entity, [], options);

    private static CatalogResolution Unresolved(
        string reason,
        IReadOnlyList<ChatActionOptionReadModel>? options = null) =>
        new(null, [reason], options ?? []);

    private static IReadOnlyList<ChatActionOptionReadModel> Options<T>(
        IReadOnlyList<T> values,
        Func<T, Guid> id,
        Func<T, string> code,
        Func<T, string> name,
        string optionType) =>
        values.Take(20)
            .Select(value => new ChatActionOptionReadModel(
                id(value),
                code(value),
                name(value),
                optionType))
            .ToArray();

    private static IReadOnlyList<ChatActionOptionReadModel> TargetOptions(
        IEnumerable<RequirementTechnicalProposalItemReadModel> items) =>
        items.Take(20)
            .Select(item => new ChatActionOptionReadModel(
                item.ItemId,
                item.Reference,
                $"{item.Sequence}. {item.Reference ?? item.Description}",
                "ITEM"))
            .ToArray();

    private static ChatActionPlanActionReadModel Invalid(
        string actionType,
        PlanRequirementChatActionCommand command,
        string reason) =>
        new(
            Guid.NewGuid(),
            actionType,
            command.TargetTechnicalProposalItemId ?? command.ContextTechnicalProposalItemId,
            command.TargetReference,
            RequestedValue(command, actionType),
            null,
            null,
            "INVALID",
            [reason],
            false,
            []);

    private static string NormalizeScope(string? value, Guid? contextItemId) =>
        contextItemId is not null
            ? "ITEM"
            : string.Equals(value?.Trim(), "ITEM", StringComparison.OrdinalIgnoreCase)
                ? "ITEM"
                : "REQUIREMENT";

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string? RequestedValue(
        PlanRequirementChatActionCommand command,
        string actionType) =>
        actionType switch
        {
            "CHANGE_QUANTITY" => command.Quantity?.ToString(),
            "CHANGE_DIMENSIONS" => command.WidthMillimeters is null && command.HeightMillimeters is null
                ? null
                : $"{command.WidthMillimeters}x{command.HeightMillimeters}",
            "EXCLUDE_ITEM" => "false",
            "INCLUDE_ITEM" => "true",
            _ => command.RequestedValue
        };

    private static string? CurrentValue(
        RequirementTechnicalProposalItemReadModel item,
        string actionType) =>
        actionType switch
        {
            "CHANGE_SYSTEM" => item.Selected?.System?.Code ?? item.Suggested.System?.Code,
            "CHANGE_GLASS" => item.Selected?.Glass?.Code ?? item.Suggested.Glass?.Code,
            "CHANGE_FINISH" => item.Selected?.Finish?.Code ?? item.Suggested.Finish?.Code,
            "CHANGE_QUANTITY" => item.EffectiveQuantity?.ToString(),
            "CHANGE_DIMENSIONS" => $"{item.EffectiveWidthMm}x{item.EffectiveHeightMm}",
            "EXCLUDE_ITEM" or "INCLUDE_ITEM" => item.IsIncluded.ToString(),
            _ => null
        };

    private static RequirementChatActionFailure Map(GetRequirementTechnicalProposalFailure failure) =>
        failure switch
        {
            GetRequirementTechnicalProposalFailure.InvalidRequest => RequirementChatActionFailure.InvalidRequest,
            GetRequirementTechnicalProposalFailure.Unauthorized => RequirementChatActionFailure.Unauthorized,
            GetRequirementTechnicalProposalFailure.InactiveUser => RequirementChatActionFailure.InactiveUser,
            GetRequirementTechnicalProposalFailure.RequirementNotFound => RequirementChatActionFailure.RequirementNotFound,
            GetRequirementTechnicalProposalFailure.PreQuoteNotFound => RequirementChatActionFailure.PreQuoteNotFound,
            GetRequirementTechnicalProposalFailure.ProjectNotFound => RequirementChatActionFailure.ProjectNotFound,
            GetRequirementTechnicalProposalFailure.InactiveProject => RequirementChatActionFailure.InactiveProject,
            GetRequirementTechnicalProposalFailure.ClientNotFound => RequirementChatActionFailure.ClientNotFound,
            GetRequirementTechnicalProposalFailure.InactiveClient => RequirementChatActionFailure.InactiveClient,
            GetRequirementTechnicalProposalFailure.TechnicalProposalNotFound => RequirementChatActionFailure.TechnicalProposalNotFound,
            _ => RequirementChatActionFailure.QueryError
        };

    private sealed record TargetResolution(
        string State,
        RequirementTechnicalProposalItemReadModel? Item,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<ChatActionOptionReadModel> Options);

    private sealed record CatalogResolution(
        ChatActionResolvedCatalogEntityReadModel? Entity,
        IReadOnlyList<string> Reasons,
        IReadOnlyList<ChatActionOptionReadModel> Options);
}

public sealed class ConfirmRequirementChatActionService(
    IRequirementChatActionPlanStore store,
    IRequirementRepository requirementRepository,
    IRequirementChatSelectionExecutor selectionExecutor,
    IRequirementChatInclusionExecutor inclusionExecutor,
    IRequirementChatPricingExecutor pricingExecutor,
    IRequirementChatTechnicalProposalReader technicalProposalReader)
{
    public async Task<RequirementChatActionPlanResult> ExecuteAsync(
        ConfirmRequirementChatActionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty || command.PlanId == Guid.Empty)
        {
            return RequirementChatActionPlanResult.Failed(
                RequirementChatActionFailure.InvalidRequest);
        }

        var plan = store.StartExecution(command.RequirementId, command.PlanId);
        if (plan is null)
        {
            return RequirementChatActionPlanResult.Failed(
                RequirementChatActionFailure.PlanNotFound);
        }

        if (plan.Status is "EXECUTED" or "EXECUTED_WITH_PRICING_PENDING" or "EXPIRED" or "EXECUTING")
        {
            return RequirementChatActionPlanResult.Success(plan);
        }

        if (plan.Status != "READY_FOR_CONFIRMATION")
        {
            return RequirementChatActionPlanResult.Success(plan);
        }

        var action = plan.Actions.Single();
        var pricingExisted = await requirementRepository.GetCurrentPricingSnapshotAsync(
            command.RequirementId,
            cancellationToken) is not null;
        var pricingStatus = pricingExisted ? "PRICING_UPDATED" : "NOT_YET_PRICED";
        var reasons = new List<string>();
        var executed = false;

        if (action.ActionType is "CHANGE_SYSTEM" or "CHANGE_GLASS" or "CHANGE_FINISH" or "CHANGE_QUANTITY" or "CHANGE_DIMENSIONS")
        {
            if (pricingExisted)
            {
                var reprice = await pricingExecutor.RepriceItemAsync(
                    ToRepriceCommand(command.RequirementId, action),
                    cancellationToken);
                if (reprice.IsSuccess)
                {
                    executed = true;
                }
                else
                {
                    reasons.Add($"REPRICE_FAILED_{reprice.Failure}");
                    pricingStatus = "PRICING_PENDING";
                    var selection = await selectionExecutor.ExecuteAsync(
                        ToSelectionCommand(plan.TechnicalProposalId, action),
                        cancellationToken);
                    executed = selection.IsSuccess;
                    if (!selection.IsSuccess)
                    {
                        reasons.Add($"SELECTION_FAILED_{selection.Failure}");
                    }
                }
            }
            else
            {
                var selection = await selectionExecutor.ExecuteAsync(
                    ToSelectionCommand(plan.TechnicalProposalId, action),
                    cancellationToken);
                executed = selection.IsSuccess;
                if (!selection.IsSuccess)
                {
                    reasons.Add($"SELECTION_FAILED_{selection.Failure}");
                }
            }
        }
        else if (action.ActionType is "EXCLUDE_ITEM" or "INCLUDE_ITEM")
        {
            var inclusion = await inclusionExecutor.ExecuteAsync(
                new UpdateRequirementTechnicalProposalItemInclusionCommand(
                    command.RequirementId,
                    action.TargetTechnicalProposalItemId!.Value,
                    action.ActionType == "INCLUDE_ITEM",
                    "CHAT_ACTION"),
                cancellationToken);
            executed = inclusion.IsSuccess;
            if (!inclusion.IsSuccess)
            {
                reasons.Add($"INCLUSION_FAILED_{inclusion.Failure}");
            }
            else if (pricingExisted)
            {
                var pricing = await pricingExecutor.PriceRequirementAsync(
                    new PriceRequirementTechnicalProposalCommand(command.RequirementId),
                    cancellationToken);
                if (!pricing.IsSuccess)
                {
                    pricingStatus = "PRICING_PENDING";
                    reasons.Add($"PRICING_FAILED_{pricing.Failure}");
                }
            }
        }

        var updatedProposal = await technicalProposalReader.GetAsync(
            command.RequirementId,
            cancellationToken);
        if (!updatedProposal.IsSuccess)
        {
            reasons.Add($"REFRESH_FAILED_{updatedProposal.Failure}");
        }

        var status = executed
            ? pricingStatus == "PRICING_PENDING"
                ? "EXECUTED_WITH_PRICING_PENDING"
                : "EXECUTED"
            : "INVALID";
        var updated = plan with
        {
            Status = status,
            RequiresConfirmation = false,
            PricingStatus = pricingStatus,
            ExecutionReasons = reasons.Distinct(StringComparer.Ordinal).ToArray()
        };
        store.Save(updated);
        return RequirementChatActionPlanResult.Success(updated);
    }

    private static UpdateRequirementTechnicalProposalItemSelectionCommand ToSelectionCommand(
        Guid technicalProposalId,
        ChatActionPlanActionReadModel action) =>
        new(
            technicalProposalId,
            action.TargetTechnicalProposalItemId!.Value,
            false,
            action.ActionType == "CHANGE_SYSTEM" ? action.ResolvedCatalogEntity!.Id : null,
            action.ActionType == "CHANGE_GLASS" ? action.ResolvedCatalogEntity!.Id : null,
            action.ActionType == "CHANGE_FINISH" ? action.ResolvedCatalogEntity!.Id : null,
            action.ActionType == "CHANGE_QUANTITY" ? int.Parse(action.RequestedValue!) : null,
            action.ActionType == "CHANGE_DIMENSIONS" ? ParseDimension(action.RequestedValue!, 0) : null,
            action.ActionType == "CHANGE_DIMENSIONS" ? ParseDimension(action.RequestedValue!, 1) : null);

    private static RepriceRequirementTechnicalProposalItemCommand ToRepriceCommand(
        Guid requirementId,
        ChatActionPlanActionReadModel action) =>
        new(
            requirementId,
            action.TargetTechnicalProposalItemId!.Value,
            action.ActionType == "CHANGE_SYSTEM" ? action.ResolvedCatalogEntity!.Id : null,
            action.ActionType == "CHANGE_GLASS" ? action.ResolvedCatalogEntity!.Id : null,
            action.ActionType == "CHANGE_FINISH" ? action.ResolvedCatalogEntity!.Id : null,
            action.ActionType == "CHANGE_QUANTITY" ? int.Parse(action.RequestedValue!) : null,
            action.ActionType == "CHANGE_DIMENSIONS" ? ParseDimension(action.RequestedValue!, 0) : null,
            action.ActionType == "CHANGE_DIMENSIONS" ? ParseDimension(action.RequestedValue!, 1) : null);

    private static int ParseDimension(string value, int index) =>
        int.Parse(value.Split('x', StringSplitOptions.TrimEntries)[index]);
}

