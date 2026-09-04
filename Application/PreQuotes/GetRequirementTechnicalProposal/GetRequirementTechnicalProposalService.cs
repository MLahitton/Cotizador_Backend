using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.TechnicalProposalReadiness;
using Application.PreQuotes.VisualSystemModel;
using Domain.PreQuotes;

namespace Application.PreQuotes.GetRequirementTechnicalProposal;

public sealed record GetRequirementTechnicalProposalCommand(Guid RequirementId);

public enum GetRequirementTechnicalProposalFailure
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
    QueryError
}

public sealed record GetRequirementTechnicalProposalResult(
    bool IsSuccess,
    GetRequirementTechnicalProposalFailure Failure,
    RequirementTechnicalProposalReadModel? Proposal)
{
    public static GetRequirementTechnicalProposalResult Success(
        RequirementTechnicalProposalReadModel proposal) =>
        new(true, GetRequirementTechnicalProposalFailure.None, proposal);

    public static GetRequirementTechnicalProposalResult Failed(
        GetRequirementTechnicalProposalFailure failure) =>
        new(false, failure, null);
}

public sealed class GetRequirementTechnicalProposalService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IProductSystemCatalogRepository productSystemCatalog,
    IGlassTypeCatalogRepository glassCatalog,
    IFinishTypeCatalogRepository finishCatalog)
{
    public async Task<GetRequirementTechnicalProposalResult> ExecuteAsync(
        GetRequirementTechnicalProposalCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty)
        {
            return GetRequirementTechnicalProposalResult.Failed(
                GetRequirementTechnicalProposalFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetRequirementTechnicalProposalResult.Failed(
                GetRequirementTechnicalProposalFailure.Unauthorized);
        }

        var access = await ValidateAccessAsync(
            command.RequirementId,
            userId,
            cancellationToken);
        if (access.Failure != GetRequirementTechnicalProposalFailure.None)
        {
            return GetRequirementTechnicalProposalResult.Failed(access.Failure);
        }

        RequirementTechnicalProposal? proposal;
        IReadOnlyList<ProductSystemCatalogReadModel> systems;
        IReadOnlyList<GlassTypeCatalogReadModel> glasses;
        IReadOnlyList<FinishTypeCatalogReadModel> finishes;
        IReadOnlyList<RequirementFile> files;
        try
        {
            proposal = await requirementRepository.GetCurrentTechnicalProposalAsync(
                command.RequirementId,
                cancellationToken);
            if (proposal is null)
            {
                return GetRequirementTechnicalProposalResult.Failed(
                    GetRequirementTechnicalProposalFailure.TechnicalProposalNotFound);
            }

            systems = await productSystemCatalog.ListActiveAsync(cancellationToken);
            glasses = await glassCatalog.GetActiveWithCurrentPriceRangesAsync(
                cancellationToken);
            finishes = await finishCatalog.ListActiveAsync(cancellationToken);
            files = await requirementRepository.ListFilesByRequirementIdAsync(
                command.RequirementId,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return GetRequirementTechnicalProposalResult.Failed(
                GetRequirementTechnicalProposalFailure.QueryError);
        }

        return GetRequirementTechnicalProposalResult.Success(
            MapProposal(proposal, systems, glasses, finishes, files));
    }

    private async Task<AccessValidationResult> ValidateAccessAsync(
        Guid requirementId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        Requirement? requirement;
        try
        {
            var user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
            if (user is null)
            {
                return new(GetRequirementTechnicalProposalFailure.Unauthorized);
            }

            if (!user.IsActive)
            {
                return new(GetRequirementTechnicalProposalFailure.InactiveUser);
            }

            requirement = await requirementRepository.FindByIdAsync(
                requirementId,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new(GetRequirementTechnicalProposalFailure.QueryError);
        }

        if (requirement is null || !requirement.IsActive)
        {
            return new(GetRequirementTechnicalProposalFailure.RequirementNotFound);
        }

        return await ValidatePreQuoteAccessAsync(
            requirement.PreQuoteId,
            userId,
            cancellationToken);
    }

    private async Task<AccessValidationResult> ValidatePreQuoteAccessAsync(
        Guid preQuoteId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var preQuote = await preQuoteRepository.FindByIdAsync(
                preQuoteId,
                cancellationToken);
            if (preQuote is null)
            {
                return new(GetRequirementTechnicalProposalFailure.PreQuoteNotFound);
            }

            var project = await projectRepository.FindByIdAsync(
                preQuote.ProjectId,
                cancellationToken);
            if (project is null)
            {
                return new(GetRequirementTechnicalProposalFailure.ProjectNotFound);
            }

            if (project.CreatedByUserId != userId)
            {
                return new(GetRequirementTechnicalProposalFailure.RequirementNotFound);
            }

            if (!project.IsActive)
            {
                return new(GetRequirementTechnicalProposalFailure.InactiveProject);
            }

            var client = await clientRepository.FindByIdAsync(
                project.ClientId,
                cancellationToken);
            if (client is null)
            {
                return new(GetRequirementTechnicalProposalFailure.ClientNotFound);
            }

            return client.IsActive
                ? new(GetRequirementTechnicalProposalFailure.None)
                : new(GetRequirementTechnicalProposalFailure.InactiveClient);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new(GetRequirementTechnicalProposalFailure.QueryError);
        }
    }

    private static RequirementTechnicalProposalReadModel MapProposal(
        RequirementTechnicalProposal proposal,
        IReadOnlyList<ProductSystemCatalogReadModel> systems,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        IReadOnlyList<FinishTypeCatalogReadModel> finishes,
        IReadOnlyList<RequirementFile> files)
    {
        var systemById = systems.ToDictionary(system => system.Id);
        var glassById = glasses.ToDictionary(glass => glass.GlassTypeId);
        var finishById = finishes.ToDictionary(finish => finish.Id);
        var sourcesById = SourceMetadataById(files);
        var items = proposal.Items
            .OrderBy(item => item.Sequence)
            .ThenBy(item => item.Id)
            .Select(item => MapItem(
                item,
                systemById,
                glassById,
                finishById,
                sourcesById))
            .ToArray();
        var includedItems = items.Where(item => item.IsIncluded).ToArray();
        var readiness = TechnicalProposalReadinessEvaluator.EvaluateProposal(
            includedItems.Select(item => item.Readiness).ToArray());

        return new RequirementTechnicalProposalReadModel(
            proposal.RequirementId,
            proposal.Id,
            proposal.RequirementProcessingAttemptId,
            proposal.RequirementExtractionResultId,
            proposal.Status.ToString(),
            proposal.Requirement.CommercialLine is null
                ? null
                : ToContract(proposal.Requirement.CommercialLine.Value),
            new RequirementTechnicalProposalCommercialConfirmationReadModel(
                ToContract(proposal.CommercialConfirmationState),
                proposal.CommercialConfirmedAtUtc,
                proposal.CommercialConfirmedByUserId),
            proposal.CreatedAtUtc,
            items.Length,
            items.Count(item => item.Source == "AI_EXTRACTED"),
            items.Count(item => item.Source == "MANUAL"),
            items.Length,
            includedItems.Count(item => RequiresReview(item.Readiness)),
            includedItems.Count(item => IsTechnicallyComplete(item.Readiness)),
            includedItems.Count(item => IsPriceable(item.Readiness)),
            readiness,
            items);
    }

    private static bool IsTechnicallyComplete(
        RequirementTechnicalProposalItemReadinessReadModel readiness) =>
        readiness.PendingDefinitions.All(definition =>
            !definition.BlocksConfirmation);

    private static bool IsPriceable(
        RequirementTechnicalProposalItemReadinessReadModel readiness) =>
        readiness.PendingDefinitions.All(definition => !definition.BlocksPricing);

    private static bool RequiresReview(
        RequirementTechnicalProposalItemReadinessReadModel readiness) =>
        readiness.State != "READY"
        || readiness.BlockingCount > 0
        || readiness.WarningCount > 0
        || readiness.PendingDefinitions.Count > 0;

    private static RequirementTechnicalProposalItemReadModel MapItem(
        RequirementTechnicalProposalItem item,
        IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems,
        IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses,
        IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes,
        IReadOnlyDictionary<string, SourceMetadata> sourcesById)
    {
        var extracted = item.ExtractedItem;
        var readiness = TechnicalProposalReadinessEvaluator.EvaluateItem(item);
        var suggested = new RequirementTechnicalProposalSuggestedReadModel(
            MapSystem(item.SuggestedSystemId, systems),
            MapGlass(item.SuggestedGlassTypeId, glasses),
            MapFinish(item.SuggestedFinishTypeId, finishes));
        var selected = MapSelected(item, systems, glasses, finishes);
        var area = item.EffectiveWidthMillimeters is > 0
            && item.EffectiveHeightMillimeters is > 0
                ? item.EffectiveWidthMillimeters.Value
                    * item.EffectiveHeightMillimeters.Value
                    / 1_000_000m
                : extracted?.AreaSquareMeters;
        return new RequirementTechnicalProposalItemReadModel(
            item.Id,
            item.RequirementExtractedItemId,
            ToContract(item.Source),
            extracted?.Ai2ElementId,
            item.Sequence,
            item.Reference,
            item.Description,
            item.ElementType.ToString(),
            item.BaseQuantity,
            item.BaseWidthMillimeters,
            item.BaseHeightMillimeters,
            item.ManualQuantityOverride,
            item.ManualWidthMillimetersOverride,
            item.ManualHeightMillimetersOverride,
            item.EffectiveQuantity,
            item.EffectiveWidthMillimeters,
            item.EffectiveHeightMillimeters,
            area,
            item.IsIncluded,
            item.ExcludedAtUtc,
            item.ExcludedByUserId,
            item.ExclusionReason,
            extracted?.Confidence,
            extracted?.ExtractionStatus.ToString(),
            item.ManualNote,
            suggested,
            selected,
            SelectionState(item),
            new RequirementTechnicalProposalAlternativesReadModel(
                item.SystemAlternatives
                    .OrderBy(alternative => alternative.Rank)
                    .Select(alternative =>
                        MapSystemAlternative(alternative, systems))
                    .WhereNotNull()
                    .ToArray(),
                item.GlassAlternatives
                    .OrderBy(alternative => alternative.Rank)
                    .Select(alternative =>
                        MapGlassAlternative(alternative, glasses))
                    .WhereNotNull()
                    .ToArray(),
                item.FinishAlternatives
                    .OrderBy(alternative => alternative.Rank)
                    .Select(alternative =>
                        MapFinishAlternative(alternative, finishes))
                    .WhereNotNull()
                    .ToArray()),
            new RequirementTechnicalProposalConfidenceReadModel(
                item.OverallConfidence,
                item.SystemConfidence,
                item.GlassConfidence,
                item.FinishConfidence),
            item.RequiresReview,
            item.ReviewReasons,
            item.SystemResolutionReasons,
            item.GlassResolutionReasons,
            item.FinishResolutionReasons,
            item.IsTechnicallyComplete,
            item.IsPriceable,
            readiness,
            new RequirementTechnicalProposalHistoricalEvidenceReadModel(
                item.HistoricalSimilarityStatus,
                item.HistoricalSupportCount,
                item.HistoricalBestSimilarity,
                item.HistoricalAverageSimilarity,
                item.HistoricalExamples
                    .OrderByDescending(example => example.SimilarityScore)
                    .ThenBy(example => example.CandidateId)
                    .Select(MapHistoricalExample)
                    .ToArray()),
            RequirementVisualSystemModelBuilder.Build(
                item,
                suggested.System,
                selected),
            new RequirementTechnicalProposalTraceReadModel(
                extracted?.RequestedSystemRaw,
                extracted?.RequestedProfileRaw,
                extracted?.FunctionalType,
                extracted?.Operation,
                extracted?.GlassRawSpecification,
                extracted?.GlassTypeRaw,
                extracted?.GlassTypeNormalized,
                extracted?.GlassThicknessMm,
                extracted?.FinishRawDescription,
                extracted?.FinishNormalizedType,
                extracted?.FinishColorRaw,
                extracted?.FinishColorNormalized,
                extracted?.SpecialFeatures ?? [],
                extracted?.GeometryType),
            (extracted?.Evidence ?? [])
                .OrderBy(evidence => evidence.PageNumber ?? int.MaxValue)
                .ThenBy(evidence => evidence.SheetName)
                .ThenBy(evidence => evidence.CellRange)
                .ThenBy(evidence => evidence.Id)
                .Select(evidence => MapEvidence(evidence, sourcesById))
                .ToArray());
    }

    private static RequirementTechnicalProposalSystemAlternativeReadModel?
        MapSystemAlternative(
            RequirementTechnicalProposalSystemAlternative alternative,
            IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems) =>
        MapSystem(alternative.ProductSystemId, systems) is { } option
            ? new(option, alternative.Rank, alternative.Confidence,
                alternative.Reasons)
            : null;

    private static RequirementTechnicalProposalSelectedReadModel? MapSelected(
        RequirementTechnicalProposalItem item,
        IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems,
        IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses,
        IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes)
    {
        if (item.SelectedAtUtc is not { } selectedAtUtc
            || item.SelectedByUserId is not { } selectedByUserId)
        {
            return null;
        }

        return new RequirementTechnicalProposalSelectedReadModel(
            MapSystem(item.SelectedSystemId, systems),
            MapGlass(item.SelectedGlassTypeId, glasses),
            MapFinish(item.SelectedFinishTypeId, finishes),
            selectedAtUtc,
            selectedByUserId);
    }

    private static string SelectionState(RequirementTechnicalProposalItem item)
    {
        if (item.SelectedAtUtc is null || item.SelectedByUserId is null)
        {
            return "UNCONFIRMED";
        }

        return item.SelectedSystemId == item.SuggestedSystemId
            && item.SelectedGlassTypeId == item.SuggestedGlassTypeId
            && item.SelectedFinishTypeId == item.SuggestedFinishTypeId
                ? "CONFIRMED_AS_SUGGESTED"
                : "MODIFIED";
    }

    private static RequirementTechnicalProposalGlassAlternativeReadModel?
        MapGlassAlternative(
            RequirementTechnicalProposalGlassAlternative alternative,
            IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses) =>
        MapGlass(alternative.GlassTypeId, glasses) is { } option
            ? new(option, alternative.Rank, alternative.Confidence,
                alternative.Reasons)
            : null;

    private static RequirementTechnicalProposalFinishAlternativeReadModel?
        MapFinishAlternative(
            RequirementTechnicalProposalFinishAlternative alternative,
            IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes) =>
        MapFinish(alternative.FinishTypeId, finishes) is { } option
            ? new(option, alternative.Rank, alternative.Confidence,
                alternative.Reasons)
            : null;

    private static RequirementTechnicalProposalSystemOptionReadModel? MapSystem(
        Guid? id,
        IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems) =>
        id is { } value && systems.TryGetValue(value, out var system)
            ? new(
                system.Id,
                system.Code,
                system.Name,
                system.TechnicalName,
                system.CommercialName,
                system.FunctionalType,
                system.Family,
                system.Series,
                system.CommercialLine,
                system.Variant)
            : null;

    private static RequirementTechnicalProposalGlassOptionReadModel? MapGlass(
        Guid? id,
        IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses) =>
        id is { } value && glasses.TryGetValue(value, out var glass)
            ? new(
                glass.GlassTypeId,
                glass.Code,
                glass.Name,
                glass.Family,
                glass.Composition,
                glass.Treatment,
                glass.OuterThicknessMm,
                glass.InnerThicknessMm,
                glass.PvbThicknessMm,
                glass.PvbType,
                glass.PvbColor,
                glass.ChamberThicknessMm,
                glass.ProductLine,
                glass.ProductToken,
                glass.Pattern,
                glass.Color)
            : null;

    private static RequirementTechnicalProposalFinishOptionReadModel? MapFinish(
        Guid? id,
        IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes) =>
        id is { } value && finishes.TryGetValue(value, out var finish)
            ? new(
                finish.Id,
                finish.Code,
                finish.Name,
                finish.NormalizedType,
                finish.Color,
                finish.Texture,
                finish.Process,
                finish.CommercialCode,
                finish.Material)
            : null;

    private static RequirementTechnicalProposalHistoricalExampleReadModel
        MapHistoricalExample(RequirementTechnicalProposalHistoricalExample example) =>
        new(
            example.CandidateId,
            example.QuoteId,
            example.HistoricalReference,
            example.SimilarityScore,
            example.MatchedFeatures,
            example.Differences,
            example.TechnicalExplanation);

    private static RequirementTechnicalProposalEvidenceReadModel MapEvidence(
        RequirementExtractedItemEvidence evidence,
        IReadOnlyDictionary<string, SourceMetadata> sourcesById)
    {
        var metadata = evidence.SourceId is null
            ? null
            : sourcesById.GetValueOrDefault(evidence.SourceId);
        return
        new(
            evidence.PageNumber,
            evidence.SourceType.ToString(),
            evidence.Text,
            evidence.SheetName,
            evidence.CellRange,
            evidence.SourceId,
            metadata?.FileName,
            metadata?.ContextLabel,
            evidence.Confidence,
            evidence.Status.ToString());
    }

    private static string ToContract(TechnicalProposalItemSource source) =>
        source == TechnicalProposalItemSource.Manual
            ? "MANUAL"
            : "AI_EXTRACTED";

    private static string ToContract(RequirementCommercialLine commercialLine) =>
        commercialLine switch
        {
            RequirementCommercialLine.Classic => "CLASSIC",
            RequirementCommercialLine.Essential => "ESSENTIAL",
            RequirementCommercialLine.Bioconfort => "BIOCONFORT",
            RequirementCommercialLine.Signature => "SIGNATURE",
            _ => throw new ArgumentOutOfRangeException(nameof(commercialLine))
        };

    private static string ToContract(
        RequirementTechnicalProposalCommercialConfirmationState state) =>
        state switch
        {
            RequirementTechnicalProposalCommercialConfirmationState
                .PendingConfirmation => "PENDING_CONFIRMATION",
            RequirementTechnicalProposalCommercialConfirmationState.Confirmed =>
                "CONFIRMED",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    private static IReadOnlyDictionary<string, SourceMetadata> SourceMetadataById(
        IReadOnlyList<RequirementFile> files) =>
        files
            .OrderBy(file => file.CreatedAtUtc)
            .ThenBy(file => file.Id)
            .Select((file, index) => new
            {
                SourceId = $"source-{index + 1}",
                Metadata = new SourceMetadata(
                    file.OriginalFileName,
                    ContextLabel(file.OriginalFileName))
            })
            .ToDictionary(
                value => value.SourceId,
                value => value.Metadata,
                StringComparer.Ordinal);

    private static string? ContextLabel(string fileName)
    {
        if (fileName.Contains(
                "NIVEL 1",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Nivel 1";
        }

        if (fileName.Contains(
                "NIVEL 2",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Nivel 2";
        }

        return null;
    }

    private sealed record SourceMetadata(
        string FileName,
        string? ContextLabel);

    private sealed record AccessValidationResult(
        GetRequirementTechnicalProposalFailure Failure);
}

internal static class EnumerableNullFilteringExtensions
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> values)
        where T : class
    {
        foreach (var value in values)
        {
            if (value is not null)
            {
                yield return value;
            }
        }
    }
}

public sealed record RequirementTechnicalProposalReadModel(
    Guid RequirementId,
    Guid TechnicalProposalId,
    Guid ProcessingAttemptId,
    Guid ExtractionResultId,
    string Status,
    string? CommercialLine,
    RequirementTechnicalProposalCommercialConfirmationReadModel
        CommercialConfirmation,
    DateTimeOffset CreatedAtUtc,
    int ItemCount,
    int DetectedItemCount,
    int ManualItemCount,
    int TotalProposalItemCount,
    int ItemsRequiringReview,
    int TechnicallyCompleteItems,
    int PriceableItems,
    RequirementTechnicalProposalReadinessReadModel Readiness,
    IReadOnlyList<RequirementTechnicalProposalItemReadModel> Items);

public sealed record RequirementTechnicalProposalCommercialConfirmationReadModel(
    string State,
    DateTimeOffset? ConfirmedAtUtc,
    Guid? ConfirmedByUserId);

public sealed record RequirementTechnicalProposalItemReadModel(
    Guid ItemId,
    Guid? ExtractedItemId,
    string Source,
    string? ElementId,
    int Sequence,
    string? Reference,
    string Description,
    string ElementType,
    int? Quantity,
    int? WidthMm,
    int? HeightMm,
    int? ManualQuantityOverride,
    int? ManualWidthMmOverride,
    int? ManualHeightMmOverride,
    int? EffectiveQuantity,
    int? EffectiveWidthMm,
    int? EffectiveHeightMm,
    decimal? AreaM2,
    bool IsIncluded,
    DateTimeOffset? ExcludedAtUtc,
    Guid? ExcludedByUserId,
    string? ExclusionReason,
    decimal? ExtractionConfidence,
    string? ExtractionStatus,
    string? ManualNote,
    RequirementTechnicalProposalSuggestedReadModel Suggested,
    RequirementTechnicalProposalSelectedReadModel? Selected,
    string SelectionState,
    RequirementTechnicalProposalAlternativesReadModel Alternatives,
    RequirementTechnicalProposalConfidenceReadModel Confidence,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> SystemResolutionReasons,
    IReadOnlyList<string> GlassResolutionReasons,
    IReadOnlyList<string> FinishResolutionReasons,
    bool IsTechnicallyComplete,
    bool IsPriceable,
    RequirementTechnicalProposalItemReadinessReadModel Readiness,
    RequirementTechnicalProposalHistoricalEvidenceReadModel HistoricalEvidence,
    RequirementTechnicalProposalVisualModelReadModel VisualModel,
    RequirementTechnicalProposalTraceReadModel Trace,
    IReadOnlyList<RequirementTechnicalProposalEvidenceReadModel> Evidence);

public sealed record RequirementTechnicalProposalSuggestedReadModel(
    RequirementTechnicalProposalSystemOptionReadModel? System,
    RequirementTechnicalProposalGlassOptionReadModel? Glass,
    RequirementTechnicalProposalFinishOptionReadModel? Finish);

public sealed record RequirementTechnicalProposalSelectedReadModel(
    RequirementTechnicalProposalSystemOptionReadModel? System,
    RequirementTechnicalProposalGlassOptionReadModel? Glass,
    RequirementTechnicalProposalFinishOptionReadModel? Finish,
    DateTimeOffset SelectedAtUtc,
    Guid SelectedByUserId);

public sealed record RequirementTechnicalProposalAlternativesReadModel(
    IReadOnlyList<RequirementTechnicalProposalSystemAlternativeReadModel> Systems,
    IReadOnlyList<RequirementTechnicalProposalGlassAlternativeReadModel> Glass,
    IReadOnlyList<RequirementTechnicalProposalFinishAlternativeReadModel> Finishes);

public sealed record RequirementTechnicalProposalConfidenceReadModel(
    decimal Overall,
    decimal System,
    decimal Glass,
    decimal Finish);

public sealed record RequirementTechnicalProposalSystemOptionReadModel(
    Guid Id,
    string Code,
    string DisplayName,
    string? TechnicalName,
    string? CommercialName,
    string? FunctionalType,
    string? Family,
    string? Series,
    string? CommercialLine,
    string? Variant);

public sealed record RequirementTechnicalProposalGlassOptionReadModel(
    Guid Id,
    string Code,
    string DisplayName,
    string? Family,
    string? Composition,
    string? Treatment,
    decimal? OuterThicknessMm,
    decimal? InnerThicknessMm,
    decimal? PvbThicknessMm,
    string? PvbType,
    string? PvbColor,
    decimal? ChamberThicknessMm,
    string? ProductLine,
    string? ProductToken,
    string? Pattern,
    string? Color);

public sealed record RequirementTechnicalProposalFinishOptionReadModel(
    Guid Id,
    string Code,
    string DisplayName,
    string? NormalizedType,
    string? Color,
    string? Texture,
    string? Process,
    string? CommercialCode,
    string? Material);

public sealed record RequirementTechnicalProposalSystemAlternativeReadModel(
    RequirementTechnicalProposalSystemOptionReadModel Option,
    int Rank,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public sealed record RequirementTechnicalProposalGlassAlternativeReadModel(
    RequirementTechnicalProposalGlassOptionReadModel Option,
    int Rank,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public sealed record RequirementTechnicalProposalFinishAlternativeReadModel(
    RequirementTechnicalProposalFinishOptionReadModel Option,
    int Rank,
    decimal Confidence,
    IReadOnlyList<string> Reasons);

public sealed record RequirementTechnicalProposalHistoricalEvidenceReadModel(
    string Status,
    int SupportCount,
    decimal? BestSimilarity,
    decimal? AverageSimilarity,
    IReadOnlyList<RequirementTechnicalProposalHistoricalExampleReadModel> Examples);

public sealed record RequirementTechnicalProposalHistoricalExampleReadModel(
    string CandidateId,
    string QuoteId,
    string? HistoricalReference,
    decimal SimilarityScore,
    IReadOnlyList<string> MatchedFeatures,
    IReadOnlyList<string> Differences,
    string TechnicalExplanation);

public sealed record RequirementTechnicalProposalTraceReadModel(
    string? RequestedSystemRaw,
    string? RequestedProfileRaw,
    string? FunctionalType,
    string? Operation,
    string? GlassRawSpecification,
    string? GlassTypeRaw,
    string? GlassTypeNormalized,
    decimal? GlassThicknessMm,
    string? FinishRawDescription,
    string? FinishNormalizedType,
    string? FinishColorRaw,
    string? FinishColorNormalized,
    IReadOnlyList<string> SpecialFeatures,
    string? GeometryType);

public sealed record RequirementTechnicalProposalEvidenceReadModel(
    int? PageNumber,
    string SourceType,
    string Text,
    string? SheetName,
    string? CellRange,
    string? SourceId,
    string? SourceFileName,
    string? ContextLabel,
    decimal? Confidence,
    string Status);
