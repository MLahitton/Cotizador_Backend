using Domain.PreQuotes;

namespace Application.PreQuotes.TechnicalProposalReadiness;

public static class TechnicalProposalReadinessEvaluator
{
    public static RequirementTechnicalProposalItemReadinessReadModel EvaluateItem(
        RequirementTechnicalProposalItem item)
    {
        var pending = new Dictionary<string, PendingDefinitionBuilder>(
            StringComparer.Ordinal);

        var pricing = EvaluatePricingReadiness(item);
        if (!pricing.HasQuantity)
        {
            AddReason(pending, "QUANTITY_REQUIRED", item);
        }

        if (!pricing.HasMeasurements)
        {
            AddReason(pending, "MissingOrInvalidMeasurements", item);
        }

        if (!pricing.HasSystem)
        {
            AddReason(pending, "SYSTEM_NOT_RESOLVED", item);
        }

        if (!pricing.HasGlass)
        {
            AddReason(pending, "GLASS_NOT_RESOLVED", item);
        }

        if (!pricing.HasFinish)
        {
            AddReason(pending, "FINISH_NOT_RESOLVED", item);
        }

        foreach (var reason in item.ReviewReasons.Distinct(StringComparer.Ordinal))
        {
            AddReviewReason(pending, reason, item, pricing);
        }

        var definitions = pending.Values
            .Select(value => value.ToReadModel())
            .OrderBy(value => SortOrder(value.Category))
            .ThenBy(value => value.Field, StringComparer.Ordinal)
            .ToArray();

        var blockingCount = definitions.Count(value => value.BlocksConfirmation);
        var warningCount = definitions.Count(value =>
            value.Severity == "WARNING" || value.Severity == "INFO");
        var pricingBlockingCount = definitions.Count(value => value.BlocksPricing);
        var state = pricingBlockingCount > 0
            ? "BLOCKED"
            : definitions.Length > 0
                ? "REVIEW_REQUIRED"
                : "READY";

        return new RequirementTechnicalProposalItemReadinessReadModel(
            state,
            blockingCount,
            warningCount,
            definitions);
    }

    public static RequirementTechnicalProposalReadinessReadModel EvaluateProposal(
        IReadOnlyList<RequirementTechnicalProposalItemReadinessReadModel> items)
    {
        var blockingItems = items.Count(value => value.BlockingCount > 0);
        var warningItems = items.Count(value =>
            value.BlockingCount == 0 && value.WarningCount > 0);
        var definitions = items.SelectMany(value => value.PendingDefinitions).ToArray();
        var categories = definitions
            .GroupBy(value => value.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var blockingDefinitions = definitions.Count(value => value.BlocksConfirmation);
        var pricingBlockingDefinitions = definitions.Count(value => value.BlocksPricing);
        var pricingBlockingItems = items.Count(item =>
            item.PendingDefinitions.Any(definition => definition.BlocksPricing));
        var warningDefinitions = definitions.Count(value =>
            value.Severity == "WARNING" || value.Severity == "INFO");
        var state = pricingBlockingDefinitions > 0
            ? "BLOCKED"
            : definitions.Length > 0
                ? "REVIEW_REQUIRED"
                : "READY";

        return new RequirementTechnicalProposalReadinessReadModel(
            state,
            blockingDefinitions == 0,
            pricingBlockingDefinitions == 0,
            blockingItems,
            warningItems,
            blockingDefinitions,
            warningDefinitions,
            pricingBlockingItems,
            pricingBlockingDefinitions,
            categories);
    }

    public static bool BlocksConfirmation(RequirementTechnicalProposal proposal) =>
        proposal.IncludedItems.Any(item => EvaluateItem(item).PendingDefinitions
            .Any(definition => definition.BlocksConfirmation));

    public static bool BlocksPricing(RequirementTechnicalProposal proposal) =>
        proposal.IncludedItems.Any(item => EvaluateItem(item).PendingDefinitions
            .Any(definition => definition.BlocksPricing));

    private static void AddReason(
        Dictionary<string, PendingDefinitionBuilder> pending,
        string reason,
        RequirementTechnicalProposalItem item)
    {
        if (!TryCreateAction(reason, item, out var action))
        {
            return;
        }

        if (!pending.TryGetValue(action.Code, out var builder))
        {
            builder = action;
            pending.Add(action.Code, builder);
        }

        builder.AddReason(reason);
    }

    private static void AddReviewReason(
        Dictionary<string, PendingDefinitionBuilder> pending,
        string reason,
        RequirementTechnicalProposalItem item,
        PricingReadiness pricing)
    {
        if (IsEconomicReason(reason))
        {
            var targetCode = reason switch
            {
                "SYSTEM_NOT_RESOLVED" => "REVIEW_SYSTEM",
                "GLASS_NOT_RESOLVED" => "REVIEW_GLASS",
                "FINISH_NOT_RESOLVED" => "REVIEW_FINISH",
                "QUANTITY_REQUIRED" => "REVIEW_QUANTITY",
                "MissingOrInvalidMeasurements" => "REVIEW_MEASUREMENTS",
                _ => null
            };
            if (targetCode is not null
                && pending.TryGetValue(targetCode, out var current))
            {
                current.AddReason(reason);
            }

            return;
        }

        if (!TryCreateReviewOnlyAction(reason, item, pricing, out var action))
        {
            return;
        }

        if (!pending.TryGetValue(action.Code, out var builder))
        {
            builder = action;
            pending.Add(action.Code, builder);
        }

        builder.AddReason(reason);
    }

    private static bool TryCreateAction(
        string reason,
        RequirementTechnicalProposalItem item,
        out PendingDefinitionBuilder action)
    {
        var created = reason switch
        {
            "SYSTEM_NOT_RESOLVED"
                =>
                PendingDefinitionBuilder.Blocking(
                    "REVIEW_SYSTEM",
                    "SYSTEM",
                    "system",
                    "Revisar sistema",
                    PricingMessage(
                        item,
                        "Falta definir un sistema para poder cotizar.",
                        "La seleccion del sistema necesita validacion."),
                    CurrentValue(item, "system"),
                    "Revisa o cambia el sistema sugerido.",
                    true),
            "GLASS_NOT_RESOLVED"
                =>
                PendingDefinitionBuilder.Blocking(
                    "REVIEW_GLASS",
                    "GLASS",
                    "glass",
                    "Revisar vidrio",
                    PricingMessage(
                        item,
                        "Falta definir un vidrio para poder cotizar.",
                        "La configuracion de vidrio necesita validacion."),
                    CurrentValue(item, "glass"),
                    "Revisa o cambia el vidrio sugerido.",
                    true),
            "FINISH_NOT_RESOLVED" =>
                PendingDefinitionBuilder.Blocking(
                    "REVIEW_FINISH",
                    "FINISH",
                    "finish",
                    "Revisar acabado",
                    PricingMessage(
                        item,
                        "Falta definir un acabado para poder cotizar.",
                        "La seleccion de acabado necesita validacion."),
                    CurrentValue(item, "finish"),
                    "Revisa o cambia el acabado sugerido.",
                    true),
            "QUANTITY_REQUIRED" =>
                PendingDefinitionBuilder.Blocking(
                    "REVIEW_QUANTITY",
                    "QUANTITY",
                    "quantity",
                    "Revisar cantidad",
                    "La cantidad no es suficiente para cotizar.",
                    item.EffectiveQuantity?.ToString(),
                    "Modifica la configuracion e indica una cantidad valida.",
                    true),
            "MissingOrInvalidMeasurements" =>
                PendingDefinitionBuilder.Blocking(
                    "REVIEW_MEASUREMENTS",
                    "MEASUREMENTS",
                    "measurements",
                    "Revisar medidas",
                    "Las medidas base no son suficientes para cotizar.",
                    MeasurementsValue(item),
                    "Modifica la configuracion e indica ancho y alto validos.",
                    true),
            "INVALID_EVIDENCE_LOCATION" =>
                PendingDefinitionBuilder.Warning(
                    "REVIEW_EVIDENCE",
                    "EVIDENCE",
                    "evidence",
                    "Revisar evidencia",
                    "La evidencia de origen tiene una ubicacion incompleta.",
                    null,
                    "Revisa la evidencia si necesitas trazabilidad exacta."),
            "SIMILARITY_UNAVAILABLE" or "NO_COMPARABLES" =>
                PendingDefinitionBuilder.Info(
                    "REVIEW_HISTORICAL_SUPPORT",
                    "COMMERCIAL",
                    "historicalEvidence",
                    reason == "NO_COMPARABLES"
                        ? "Revisar soporte historico"
                        : "Revisar similitud historica",
                    "La configuracion tecnica puede continuar, pero la evidencia historica es limitada.",
                    null,
                    "Continua con revision comercial si la configuracion tecnica es correcta."),
            _ when IsTraceOnlyReason(reason) => null,
            _ => PendingDefinitionBuilder.Warning(
                "REVIEW_REQUIRED_UNCLASSIFIED",
                "OTHER",
                "technicalProposalItem",
                "Revisar configuracion",
                "Hay informacion tecnica que requiere validacion antes de continuar.",
                null,
                "Revisa la configuracion del item.")
        };

        action = created!;
        return created is not null;
    }

    private static bool TryCreateReviewOnlyAction(
        string reason,
        RequirementTechnicalProposalItem item,
        PricingReadiness pricing,
        out PendingDefinitionBuilder action)
    {
        var created = reason switch
        {
            "TECHNICAL_SELECTION_AMBIGUOUS"
                or "PRIMARY_COMPONENT_GEOMETRY_UNRESOLVED"
                or "RULE_NOT_DEFINED_REQUIRES_REVIEW"
                or "SLIDING_WINDOW_THRESHOLD_REVIEW"
                or "BATHROOM_DIVISION_MATERIAL_UNKNOWN" =>
                pricing.HasSystem
                    ? PendingDefinitionBuilder.Warning(
                        "REVIEW_SYSTEM_WARNING",
                        "SYSTEM",
                        "system",
                        "Advertencia de sistema",
                        "La seleccion del sistema conserva una advertencia tecnica, pero no bloquea pricing.",
                        CurrentValue(item, "system"),
                        "Revisa la advertencia si necesitas mayor certeza.")
                    : null,
            "GLASS_PANE_GEOMETRY_UNRESOLVED"
                or "GLASS_PANE_HETEROGENEOUS_NEEDS" =>
                pricing.HasGlass
                    ? PendingDefinitionBuilder.Warning(
                        "REVIEW_GLASS_WARNING",
                        "GLASS",
                        "glass",
                        "Advertencia de vidrio",
                        "La configuracion de vidrio conserva una advertencia tecnica, pero no bloquea pricing.",
                        CurrentValue(item, "glass"),
                        "Revisa la advertencia si necesitas mayor certeza.")
                    : null,
            "INVALID_EVIDENCE_LOCATION" =>
                PendingDefinitionBuilder.Warning(
                    "REVIEW_EVIDENCE",
                    "EVIDENCE",
                    "evidence",
                    "Revisar evidencia",
                    "La evidencia de origen tiene una ubicacion incompleta.",
                    null,
                    "Revisa la evidencia si necesitas trazabilidad exacta."),
            "SIMILARITY_UNAVAILABLE" or "NO_COMPARABLES" =>
                PendingDefinitionBuilder.Info(
                    "REVIEW_HISTORICAL_SUPPORT",
                    "COMMERCIAL",
                    "historicalEvidence",
                    reason == "NO_COMPARABLES"
                        ? "Revisar soporte historico"
                        : "Revisar similitud historica",
                    "La configuracion tecnica puede continuar, pero la evidencia historica es limitada.",
                    null,
                    "Continua con revision comercial si la configuracion tecnica es correcta."),
            _ when IsTraceOnlyReason(reason) => null,
            _ => PendingDefinitionBuilder.Warning(
                "REVIEW_REQUIRED_UNCLASSIFIED",
                "OTHER",
                "technicalProposalItem",
                "Revisar configuracion",
                "Hay informacion tecnica no bloqueante registrada en la propuesta.",
                null,
                "Revisa la configuracion del item si necesitas mayor certeza.")
        };

        action = created!;
        return created is not null;
    }

    private static string? CurrentValue(
        RequirementTechnicalProposalItem item,
        string field) =>
        field switch
        {
            "system" => item.ExtractedItem?.RequestedSystemRaw
                ?? item.ExtractedItem?.RequestedProfileRaw,
            "glass" => item.ExtractedItem?.GlassRawSpecification
                ?? item.ExtractedItem?.GlassTypeRaw
                ?? item.ExtractedItem?.GlassTypeNormalized,
            "finish" => item.ExtractedItem?.FinishRawDescription,
            "primaryComponentGeometry" => item.ExtractedItem?.GeometryType,
            "glassPaneGeometry" => item.ExtractedItem?.GeometryType,
            _ => null
        };

    private static PricingReadiness EvaluatePricingReadiness(
        RequirementTechnicalProposalItem item)
    {
        return new PricingReadiness(
            HasEffectiveSystem(item),
            HasEffectiveGlass(item),
            HasEffectiveFinish(item),
            item.EffectiveWidthMillimeters is > 0
                && item.EffectiveHeightMillimeters is > 0,
            item.EffectiveQuantity is > 0);
    }

    private static string? MeasurementsValue(
        RequirementTechnicalProposalItem item) =>
        item.EffectiveWidthMillimeters is null
            && item.EffectiveHeightMillimeters is null
            ? null
            : $"{item.EffectiveWidthMillimeters?.ToString() ?? "sin ancho"} x {item.EffectiveHeightMillimeters?.ToString() ?? "sin alto"} mm";

    private static bool HasEffectiveSystem(RequirementTechnicalProposalItem item) =>
        item.HasSelectedConfiguration()
            ? item.SelectedSystemId is not null
            : item.SuggestedSystemId is not null;

    private static bool HasEffectiveGlass(RequirementTechnicalProposalItem item) =>
        item.HasSelectedConfiguration()
            ? item.SelectedGlassTypeId is not null
            : item.SuggestedGlassTypeId is not null;

    private static bool HasEffectiveFinish(RequirementTechnicalProposalItem item) =>
        item.HasSelectedConfiguration()
            ? item.SelectedFinishTypeId is not null
            : item.SuggestedFinishTypeId is not null;

    private static string PricingMessage(
        RequirementTechnicalProposalItem item,
        string pricingBlockedMessage,
        string reviewMessage) =>
        EvaluatePricingReadiness(item).BlocksPricing
            ? pricingBlockedMessage
            : reviewMessage;

    private static bool IsEconomicReason(string reason) =>
        reason is "SYSTEM_NOT_RESOLVED"
            or "GLASS_NOT_RESOLVED"
            or "FINISH_NOT_RESOLVED"
            or "QUANTITY_REQUIRED"
            or "MissingOrInvalidMeasurements";

    private static bool IsTraceOnlyReason(string reason) =>
        reason is "SYSTEM_FIXED_FERMO"
            or "SYSTEM_PROJECTING_SIENA"
            or "SYSTEM_SWING_DOOR_3890"
            or "SYSTEM_SLIDING_DOOR_NAPOLES"
            or "SYSTEM_SLIDING_DOOR_POCKET_NAPOLES"
            or "VENICE_WINDOW_MONZA"
            or "SYSTEM_SLIDING_WINDOW_LOW_LAGO"
            or "SYSTEM_HISTORICAL_SUPPORT"
            or "GLASS_LINE_TEMPERED"
            or "GLASS_PANE_DIMENSIONS_FROM_ELEMENT"
            or "GLASS_PANE_DIMENSIONS_FROM_SUBMODULES"
            or "JOINT_GLASS_RULE"
            or "HISTORICAL_DEFAULT_FINISH";

    private static int SortOrder(string category) =>
        category switch
        {
            "SYSTEM" => 0,
            "GLASS" => 1,
            "FINISH" => 2,
            "GEOMETRY" => 3,
            "QUANTITY" => 4,
            "EVIDENCE" => 5,
            "RULE" => 6,
            _ => 7
        };

    private static TechnicalProposalPendingDefinitionReadModel Definition(
        string code,
        string category,
        string severity,
        string field,
        string title,
        string message,
        string? currentValue,
        string requiredAction,
        bool blocksConfirmation,
        bool blocksPricing,
        IReadOnlyList<string> relatedReasonCodes) =>
        new(
            code,
            category,
            severity,
            field,
            title,
            message,
            currentValue,
            requiredAction,
            blocksConfirmation,
            blocksPricing,
            relatedReasonCodes);

    private sealed class PendingDefinitionBuilder(
        string code,
        string category,
        string severity,
        string field,
        string title,
        string message,
        string? currentValue,
        string requiredAction,
        bool blocksConfirmation,
        bool blocksPricing)
    {
        private readonly List<string> _relatedReasonCodes = [];

        public string Code { get; } = code;
        public string Category { get; } = category;
        public string Field { get; } = field;

        public static PendingDefinitionBuilder Blocking(
            string code,
            string category,
            string field,
            string title,
            string message,
            string? currentValue,
            string requiredAction,
            bool blocksPricing) =>
            new(
                code,
                category,
                "BLOCKING",
                field,
                title,
                message,
                currentValue,
                requiredAction,
                true,
                blocksPricing);

        public static PendingDefinitionBuilder Warning(
            string code,
            string category,
            string field,
            string title,
            string message,
            string? currentValue,
            string requiredAction) =>
            new(
                code,
                category,
                "WARNING",
                field,
                title,
                message,
                currentValue,
                requiredAction,
                false,
                false);

        public static PendingDefinitionBuilder Info(
            string code,
            string category,
            string field,
            string title,
            string message,
            string? currentValue,
            string requiredAction) =>
            new(
                code,
                category,
                "INFO",
                field,
                title,
                message,
                currentValue,
                requiredAction,
                false,
                false);

        public void AddReason(string reason)
        {
            if (!_relatedReasonCodes.Contains(reason, StringComparer.Ordinal))
            {
                _relatedReasonCodes.Add(reason);
            }
        }

        public TechnicalProposalPendingDefinitionReadModel ToReadModel() =>
            Definition(
                Code,
                Category,
                severity,
                Field,
                title,
                message,
                currentValue,
                requiredAction,
                blocksConfirmation,
                blocksPricing,
                _relatedReasonCodes
                    .Order(StringComparer.Ordinal)
                    .ToArray());
    }

    private sealed record PricingReadiness(
        bool HasSystem,
        bool HasGlass,
        bool HasFinish,
        bool HasMeasurements,
        bool HasQuantity)
    {
        public bool BlocksPricing =>
            !HasSystem
            || !HasGlass
            || !HasFinish
            || !HasMeasurements
            || !HasQuantity;
    }
}

public sealed record RequirementTechnicalProposalReadinessReadModel(
    string State,
    bool IsReadyForConfirmation,
    bool IsReadyForPricing,
    int BlockingItems,
    int WarningItems,
    int BlockingDefinitions,
    int WarningDefinitions,
    int PricingBlockingItems,
    int PricingBlockingDefinitions,
    IReadOnlyDictionary<string, int> Categories);

public sealed record RequirementTechnicalProposalItemReadinessReadModel(
    string State,
    int BlockingCount,
    int WarningCount,
    IReadOnlyList<TechnicalProposalPendingDefinitionReadModel> PendingDefinitions);

public sealed record TechnicalProposalPendingDefinitionReadModel(
    string Code,
    string Category,
    string Severity,
    string Field,
    string Title,
    string Message,
    string? CurrentValue,
    string RequiredAction,
    bool BlocksConfirmation,
    bool BlocksPricing,
    IReadOnlyList<string> RelatedReasonCodes);
