using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Application.PreQuotes.PriceRequirementTechnicalProposal;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemInclusion;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemSelection;
using System.Globalization;
using System.Text;

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
    IReadOnlyList<string>? TargetReferences,
    string? RequestedValue,
    int? Quantity,
    int? WidthMillimeters,
    int? HeightMillimeters,
    string? RawUserMessage,
    RequirementChatRequestedAttributes? RequestedAttributes = null);

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
                .Where(plan => scope != "ITEM"
                    || plan!.Actions.Any(action =>
                        action.TargetTechnicalProposalItemId == technicalProposalItemId))
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
        var actions = new List<ChatActionPlanActionReadModel>();
        foreach (var targetReference in TargetReferences(command, scope))
        {
            actions.Add(await BuildActionAsync(
                command with { TargetReference = targetReference },
                proposal,
                scope,
                actionType,
                cancellationToken));
        }

        if (actions.Count == 0)
        {
            actions.Add(await BuildActionAsync(
                command,
                proposal,
                scope,
                actionType,
                cancellationToken));
        }

        var status = actions.All(action => action.ValidationState == "VALID")
            ? "READY_FOR_CONFIRMATION"
            : actions.Any(action => action.ValidationState == "INVALID")
                ? "INVALID"
                : "NEEDS_CLARIFICATION";
        var actionReferences = actions
            .Select(action => action.TargetReference)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var actionIds = actions
            .Select(action => action.TargetTechnicalProposalItemId)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        var executionReasons = new List<string>();
        if (actions.Count > 1)
        {
            executionReasons.Add($"TARGET_COUNT={actions.Count}");
            executionReasons.Add($"TARGET_REFERENCES={string.Join(",", actionReferences)}");
            executionReasons.Add($"RESOLVED_ITEM_IDS={string.Join(",", actionIds)}");
        }

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
            executionReasons,
            actions,
            command.ChatThreadId);
        store.Save(plan);
        return RequirementChatActionPlanResult.Success(plan);
    }

    private static IReadOnlyList<string?> TargetReferences(
        PlanRequirementChatActionCommand command,
        string scope)
    {
        if (scope == "ITEM" || command.ContextTechnicalProposalItemId is not null)
        {
            return [command.TargetReference];
        }

        var references = command.TargetReferences?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string?>()
            .ToArray();
        return references is { Length: > 0 }
            ? references
            : [command.TargetReference];
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
            "CHANGE_SYSTEM" => await ResolveSystemAsync(command.RequestedValue, command.RequestedAttributes?.System, item, cancellationToken),
            "CHANGE_GLASS" => await ResolveGlassAsync(command.RequestedValue, command.RequestedAttributes?.Glass, cancellationToken),
            "CHANGE_FINISH" => await ResolveFinishAsync(command.RequestedValue, command.RequestedAttributes?.Finish, cancellationToken),
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
        RequirementChatRequestedSystemAttributes? attributes,
        RequirementTechnicalProposalItemReadModel item,
        CancellationToken cancellationToken)
    {
        var systems = await productSystems.ListActiveSelectableAsync(cancellationToken);
        var requestedFunctionalType = BlankToNull(attributes?.FunctionalType);
        var requestedOperation = BlankToNull(attributes?.Operation);
        var functionalType = requestedFunctionalType
            ?? BlankToNull(item.Selected?.System?.FunctionalType)
            ?? BlankToNull(item.Suggested.System?.FunctionalType)
            ?? BlankToNull(item.VisualModel.FunctionalType)
            ?? BlankToNull(item.Trace.FunctionalType);
        var operation = requestedOperation
            ?? BlankToNull(item.VisualModel.Operation)
            ?? BlankToNull(item.Trace.Operation);
        var compatible = systems
            .Where(system => IsCompatible(system.FunctionalType, functionalType))
            .Where(system => IsCompatible(system.Series, operation)
                || IsCompatible(system.Variant, operation)
                || IsCompatible(system.TechnicalName, operation)
                || IsCompatible(system.Name, operation)
                || string.IsNullOrWhiteSpace(operation))
            .ToArray();
        if (compatible.Length == 0)
        {
            compatible = systems.ToArray();
        }

        return ResolveCatalog(
            requestedValue,
            compatible,
            system => system.Id,
            system => system.Code,
            system => system.Name,
            "SYSTEM",
            system => SystemScore(system, requestedValue, attributes),
            systems);
    }

    private async Task<CatalogResolution> ResolveGlassAsync(
        string? requestedValue,
        RequirementChatRequestedGlassAttributes? attributes,
        CancellationToken cancellationToken)
    {
        var glasses = await glassTypes.GetActiveWithCurrentPriceRangesAsync(cancellationToken);
        var selectable = glasses
            .Where(glass => glass.IsActive && glass.IsSelectable)
            .ToArray();
        var compatible = HardFilterGlass(selectable, attributes);
        if (HasExplicitPhysicalGlassAttribute(attributes))
        {
            var exactPhysical = compatible
                .Where(glass => MatchesExplicitPhysicalGlassAttributes(glass, attributes))
                .ToArray();
            if (exactPhysical.Length == 1)
            {
                var match = exactPhysical[0];
                return Resolved(
                    new ChatActionResolvedCatalogEntityReadModel(
                        match.GlassTypeId,
                        match.Code,
                        match.Name,
                        "GLASS"),
                    []);
            }

            if (exactPhysical.Length > 1)
            {
                return Unresolved(
                    "GLASS_AMBIGUOUS",
                    Options(exactPhysical, glass => glass.GlassTypeId, glass => glass.Code, glass => glass.Name, "GLASS"));
            }

            return Unresolved(
                "GLASS_NOT_FOUND",
                Options(
                    RelevantOptions(compatible, glass => GlassScore(glass, requestedValue, attributes)),
                    glass => glass.GlassTypeId,
                    glass => glass.Code,
                    glass => glass.Name,
                    "GLASS"));
        }

        return ResolveCatalog(
            requestedValue,
            compatible,
            glass => glass.GlassTypeId,
            glass => glass.Code,
            glass => glass.Name,
            "GLASS",
            glass => GlassScore(glass, requestedValue, attributes),
            compatible);
    }

    private async Task<CatalogResolution> ResolveFinishAsync(
        string? requestedValue,
        RequirementChatRequestedFinishAttributes? attributes,
        CancellationToken cancellationToken)
    {
        var values = await finishes.ListActiveAsync(cancellationToken);
        return ResolveCatalog(
            requestedValue,
            values.Where(finish => finish.IsActive && finish.IsSelectable).ToArray(),
            finish => finish.Id,
            finish => finish.Code,
            finish => finish.Name,
            "FINISH",
            finish => FinishScore(finish, requestedValue, attributes),
            values.Where(finish => finish.IsActive && finish.IsSelectable).ToArray());
    }

    private static CatalogResolution ResolveCatalog<T>(
        string? requestedValue,
        IReadOnlyList<T> values,
        Func<T, Guid> id,
        Func<T, string> code,
        Func<T, string> name,
        string entityType,
        Func<T, CatalogMatchScore> score,
        IReadOnlyList<T>? fallbackValues = null)
    {
        var normalized = requestedValue?.Trim() ?? string.Empty;
        var matches = values
            .Select(value => new ScoredCatalogValue<T>(value, score(value)))
            .Where(value => value.Score.IsMatch)
            .OrderByDescending(value => value.Score.Score)
            .ThenBy(value => name(value.Value), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (string.IsNullOrWhiteSpace(requestedValue) && matches.Length == 0)
        {
            return Unresolved($"{entityType}_VALUE_REQUIRED", Options(values, id, code, name, entityType));
        }

        var strongest = matches.Length == 0 ? [] : matches
            .Where(value => value.Score.Score == matches[0].Score.Score)
            .ToArray();
        if (strongest.Length != 1)
        {
            return Unresolved(
                strongest.Length == 0
                    ? $"{entityType}_NOT_FOUND"
                    : $"{entityType}_AMBIGUOUS",
                Options(
                    (strongest.Length == 0
                        ? RelevantOptions(fallbackValues ?? values, score)
                        : strongest.Select(value => value.Value).ToArray()),
                    id,
                    code,
                    name,
                    entityType));
        }

        var match = strongest[0].Value;
        return Resolved(
            new ChatActionResolvedCatalogEntityReadModel(
                id(match),
                code(match),
                name(match),
                entityType),
            []);
    }

    private static CatalogMatchScore SystemScore(
        ProductSystemCatalogReadModel system,
        string? requestedValue,
        RequirementChatRequestedSystemAttributes? attributes)
    {
        var exact = ExactCatalogScore(system.Id, system.Code, system.Name, requestedValue);
        var score = exact.Score;
        score += MatchText(system.Code, attributes?.Code, 120);
        score += MatchText(system.CommercialName, attributes?.CommercialName, 100);
        score += MatchText(system.Family, attributes?.Family, 80);
        score += MatchText(system.Variant, attributes?.Variant, 70);
        score += MatchText(system.CommercialLine, attributes?.CommercialLine, 40);
        score += MatchText(system.Name, requestedValue, 35);
        score += MatchText(system.TechnicalName, requestedValue, 30);
        score += ContainsText(system.Name, requestedValue, 15);
        score += ContainsText(system.TechnicalName, requestedValue, 15);
        score += ContainsText(system.CommercialName, requestedValue, 15);
        return new CatalogMatchScore(score > 0, score);
    }

    private static CatalogMatchScore GlassScore(
        GlassTypeCatalogReadModel glass,
        string? requestedValue,
        RequirementChatRequestedGlassAttributes? attributes)
    {
        var exact = ExactCatalogScore(glass.GlassTypeId, glass.Code, glass.Name, requestedValue);
        var score = exact.Score;
        score += MatchGlassText(glass.Composition, glass.Name, attributes?.Composition, 120);
        score += MatchGlassText(glass.Family, glass.Name, attributes?.Family, 90);
        score += MatchGlassText(glass.Treatment, glass.Name, attributes?.Treatment, 80);
        score += MatchNumber(glass.OuterThicknessMm, ExtractThickness(glass.Code, glass.Name), attributes?.OuterThicknessMm, 100, 80);
        score += MatchNumber(glass.InnerThicknessMm, null, attributes?.InnerThicknessMm, 80, 60);
        score += MatchNumber(glass.PvbThicknessMm, null, attributes?.PvbThicknessMm, 75, 50);
        score += MatchNumber(glass.ChamberThicknessMm, null, attributes?.ChamberThicknessMm, 75, 50);
        score += MatchText(glass.PvbType, attributes?.PvbType, 60);
        score += MatchText(glass.PvbColor, attributes?.PvbColor, 55);
        score += MatchText(glass.Color, attributes?.Color, 50);
        score += MatchText(glass.Pattern, attributes?.Pattern, 50);
        score += MatchText(glass.ProductLine, attributes?.ProductLine, 45);
        score += MatchText(glass.ProductToken, attributes?.ProductToken, 45);
        score += ContainsText(glass.Name, requestedValue, 20);
        score += ContainsText(glass.Code, requestedValue, 20);
        return new CatalogMatchScore(score > 0, score);
    }

    private static bool HasExplicitPhysicalGlassAttribute(RequirementChatRequestedGlassAttributes? attributes) =>
        attributes?.OuterThicknessMm is not null
        || attributes?.InnerThicknessMm is not null
        || attributes?.PvbThicknessMm is not null
        || attributes?.ChamberThicknessMm is not null;

    private static bool MatchesExplicitPhysicalGlassAttributes(
        GlassTypeCatalogReadModel glass,
        RequirementChatRequestedGlassAttributes? attributes) =>
        attributes is null
        || MatchesNumber(glass.OuterThicknessMm ?? ExtractThickness(glass.Code, glass.Name), attributes.OuterThicknessMm)
        && MatchesNumber(glass.InnerThicknessMm, attributes.InnerThicknessMm)
        && MatchesNumber(glass.PvbThicknessMm, attributes.PvbThicknessMm)
        && MatchesNumber(glass.ChamberThicknessMm, attributes.ChamberThicknessMm);

    private static bool MatchesNumber(decimal? actual, decimal? expected) =>
        expected is null || actual == expected.Value;

    private static IReadOnlyList<GlassTypeCatalogReadModel> HardFilterGlass(
        IReadOnlyList<GlassTypeCatalogReadModel> values,
        RequirementChatRequestedGlassAttributes? attributes)
    {
        var compatible = values
            .Where(glass => IsGlassCompatible(glass.Composition, glass.Name, attributes?.Composition))
            .Where(glass => IsGlassCompatible(glass.Family, glass.Name, attributes?.Family))
            .Where(glass => IsGlassCompatible(glass.Treatment, glass.Name, attributes?.Treatment))
            .ToArray();
        return compatible.Length == 0 ? [] : compatible;
    }

    private static CatalogMatchScore FinishScore(
        FinishTypeCatalogReadModel finish,
        string? requestedValue,
        RequirementChatRequestedFinishAttributes? attributes)
    {
        var exact = ExactCatalogScore(finish.Id, finish.Code, finish.Name, requestedValue);
        var score = exact.Score;
        score += MatchText(finish.NormalizedType, attributes?.NormalizedType, 90);
        score += MatchText(finish.Material, attributes?.Material, 90);
        score += MatchText(finish.Color, attributes?.Color, 100);
        score += MatchText(finish.Texture, attributes?.Texture, 80);
        score += MatchText(finish.Process, attributes?.Process, 70);
        score += MatchText(finish.CommercialCode, attributes?.CommercialCode, 60);
        score += ContainsText(finish.Name, requestedValue, 20);
        score += ContainsText(finish.Code, requestedValue, 20);
        return new CatalogMatchScore(score > 0, score);
    }

    private static CatalogMatchScore ExactCatalogScore(
        Guid id,
        string code,
        string name,
        string? requestedValue)
    {
        if (string.IsNullOrWhiteSpace(requestedValue))
        {
            return new CatalogMatchScore(false, 0);
        }

        var normalized = requestedValue.Trim();
        if (Guid.TryParse(normalized, out var parsed) && id == parsed
            || string.Equals(code, normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return new CatalogMatchScore(true, 1_000);
        }

        return new CatalogMatchScore(false, 0);
    }

    private static IReadOnlyList<T> RelevantOptions<T>(
        IReadOnlyList<T> values,
        Func<T, CatalogMatchScore> score) =>
        values
            .Select(value => new ScoredCatalogValue<T>(value, score(value)))
            .OrderByDescending(value => value.Score.Score)
            .ThenBy(value => value.Score.IsMatch ? 0 : 1)
            .Select(value => value.Value)
            .ToArray();

    private static bool IsCompatible(string? actual, string? expected) =>
        string.IsNullOrWhiteSpace(expected)
        || string.Equals(NormalizeText(actual), NormalizeText(expected), StringComparison.Ordinal);

    private static int MatchText(string? actual, string? expected, int weight) =>
        !string.IsNullOrWhiteSpace(actual)
        && !string.IsNullOrWhiteSpace(expected)
        && string.Equals(NormalizeText(actual), NormalizeText(expected), StringComparison.Ordinal)
            ? weight
            : 0;

    private static int MatchGlassText(string? actual, string? displayName, string? expected, int weight)
    {
        if (MatchText(actual, expected, weight) > 0)
        {
            return weight;
        }

        var normalizedExpected = NormalizeText(expected);
        if (string.IsNullOrWhiteSpace(normalizedExpected))
        {
            return 0;
        }

        var normalizedDisplay = NormalizeText(displayName);
        return normalizedExpected switch
        {
            "tempered" or "templado" when normalizedDisplay.Contains("templado", StringComparison.Ordinal) => weight,
            "raw" or "crudo" when normalizedDisplay.Contains("crudo", StringComparison.Ordinal) => weight,
            "laminated" or "laminado" when normalizedDisplay.Contains("laminado", StringComparison.Ordinal) => weight,
            _ => normalizedDisplay.Contains(normalizedExpected, StringComparison.Ordinal) ? weight : 0
        };
    }

    private static bool IsGlassCompatible(string? actual, string? displayName, string? expected)
    {
        var normalizedExpected = NormalizeGlassToken(expected);
        if (string.IsNullOrWhiteSpace(normalizedExpected))
        {
            return true;
        }

        var normalizedActual = NormalizeGlassToken(actual);
        if (string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedDisplay = NormalizeText(displayName);
        return normalizedExpected switch
        {
            "tempered" => normalizedDisplay.Contains("monolitico templado", StringComparison.Ordinal)
                || normalizedDisplay.Contains("composicion templado", StringComparison.Ordinal),
            "raw" => normalizedDisplay.Contains("monolitico crudo", StringComparison.Ordinal),
            "laminated" => normalizedDisplay.Contains("laminado", StringComparison.Ordinal),
            "igu" or "chamber" or "dvh" => normalizedDisplay.Contains("camara", StringComparison.Ordinal),
            _ => false
        };
    }

    private static int MatchNumber(
        decimal? actual,
        decimal? inferredActual,
        decimal? expected,
        int exactWeight,
        int nearbyWeight)
    {
        if (expected is null)
        {
            return 0;
        }

        var value = actual ?? inferredActual;
        if (value is null)
        {
            return 0;
        }

        var distance = Math.Abs(value.Value - expected.Value);
        if (distance == 0)
        {
            return exactWeight;
        }

        var nearby = nearbyWeight - (int)Math.Min(nearbyWeight, distance * 10);
        return Math.Max(0, nearby);
    }

    private static string NormalizeGlassToken(string? value)
    {
        var normalized = NormalizeText(value);
        return normalized switch
        {
            "templado" or "tempered" => "tempered",
            "crudo" or "raw" => "raw",
            "laminado" or "laminated" => "laminated",
            "camara" or "dvh" or "insulated" or "igu" => "igu",
            _ => normalized
        };
    }

    private static int ContainsText(string? actual, string? expected, int weight)
    {
        var normalizedActual = NormalizeText(actual);
        var normalizedExpected = NormalizeText(expected);
        return !string.IsNullOrWhiteSpace(normalizedActual)
            && !string.IsNullOrWhiteSpace(normalizedExpected)
            && normalizedActual.Contains(normalizedExpected, StringComparison.Ordinal)
            ? weight
            : 0;
    }

    private static decimal? ExtractThickness(string code, string name)
    {
        foreach (var token in NormalizeText($"{code} {name}").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                && value is >= 3 and <= 30)
            {
                return value;
            }
        }

        return null;
    }

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = true;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
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
        values.Take(8)
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

    private sealed record CatalogMatchScore(bool IsMatch, int Score);

    private sealed record ScoredCatalogValue<T>(T Value, CatalogMatchScore Score);
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

        var actions = plan.Actions;
        if (actions.Count == 0
            || actions.Select(action => action.ActionType)
                .Distinct(StringComparer.Ordinal)
                .Count() != 1
            || actions.Any(action => action.TargetTechnicalProposalItemId is null))
        {
            var invalid = plan with
            {
                Status = "INVALID",
                RequiresConfirmation = false,
                ExecutionReasons = ["BATCH_ACTIONS_INVALID"]
            };
            store.Save(invalid);
            return RequirementChatActionPlanResult.Success(invalid);
        }

        var actionType = actions[0].ActionType;
        var isBatch = actions.Count > 1;
        var pricingExisted = await requirementRepository.GetCurrentPricingSnapshotAsync(
            command.RequirementId,
            cancellationToken) is not null;
        var pricingStatus = pricingExisted ? "PRICING_UPDATED" : "NOT_YET_PRICED";
        var reasons = new List<string>();
        var executed = false;
        reasons.Add(isBatch ? "PRICING_MODE=FULL" : "PRICING_MODE=ITEM");
        reasons.Add($"ACTION_COUNT={actions.Count}");

        if (actionType is "CHANGE_SYSTEM" or "CHANGE_GLASS" or "CHANGE_FINISH" or "CHANGE_QUANTITY" or "CHANGE_DIMENSIONS")
        {
            if (pricingExisted && !isBatch)
            {
                var action = actions[0];
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
                executed = true;
                foreach (var action in actions)
                {
                    var selection = await selectionExecutor.ExecuteAsync(
                        ToSelectionCommand(plan.TechnicalProposalId, action),
                        cancellationToken);
                    if (!selection.IsSuccess)
                    {
                        executed = false;
                        reasons.Add($"SELECTION_FAILED_{selection.Failure}");
                        break;
                    }
                }

                if (executed && pricingExisted && isBatch)
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
        }
        else if (actionType is "EXCLUDE_ITEM" or "INCLUDE_ITEM")
        {
            executed = true;
            foreach (var action in actions)
            {
                var inclusion = await inclusionExecutor.ExecuteAsync(
                    new UpdateRequirementTechnicalProposalItemInclusionCommand(
                        command.RequirementId,
                        action.TargetTechnicalProposalItemId!.Value,
                        action.ActionType == "INCLUDE_ITEM",
                        "CHAT_ACTION"),
                    cancellationToken);
                if (!inclusion.IsSuccess)
                {
                    executed = false;
                    reasons.Add($"INCLUSION_FAILED_{inclusion.Failure}");
                    break;
                }
            }

            if (executed && pricingExisted)
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

