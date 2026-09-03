using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;

namespace Application.PreQuotes.PriceRequirementTechnicalProposal;

public sealed record PriceRequirementTechnicalProposalCommand(Guid RequirementId);

public sealed record RepriceRequirementTechnicalProposalItemCommand(
    Guid RequirementId,
    Guid TechnicalProposalItemId,
    Guid? SystemId,
    Guid? GlassTypeId,
    Guid? FinishTypeId,
    int? Quantity = null,
    int? WidthMillimeters = null,
    int? HeightMillimeters = null);

public enum PriceRequirementTechnicalProposalFailure
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
    TechnicalProposalNotConfirmed,
    TechnicalProposalNoIncludedItems,
    QueryError,
    Cancelled
}

public enum RepriceRequirementTechnicalProposalItemFailure
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
    TechnicalProposalNotConfirmed,
    TechnicalProposalItemNotFound,
    TechnicalProposalItemExcluded,
    InvalidSystemSelection,
    InvalidGlassSelection,
    InvalidFinishSelection,
    QueryError,
    PersistenceError
}

public sealed record PriceRequirementTechnicalProposalResult(
    bool IsSuccess,
    PriceRequirementTechnicalProposalFailure Failure,
    RequirementTechnicalProposalPricingReadModel? Pricing)
{
    public static PriceRequirementTechnicalProposalResult Success(
        RequirementTechnicalProposalPricingReadModel pricing) =>
        new(true, PriceRequirementTechnicalProposalFailure.None, pricing);

    public static PriceRequirementTechnicalProposalResult Failed(
        PriceRequirementTechnicalProposalFailure failure) =>
        new(false, failure, null);
}

public sealed record RepriceRequirementTechnicalProposalItemResult(
    bool IsSuccess,
    RepriceRequirementTechnicalProposalItemFailure Failure,
    RepriceRequirementTechnicalProposalItemReadModel? Pricing)
{
    public static RepriceRequirementTechnicalProposalItemResult Success(
        RepriceRequirementTechnicalProposalItemReadModel pricing) =>
        new(true, RepriceRequirementTechnicalProposalItemFailure.None, pricing);

    public static RepriceRequirementTechnicalProposalItemResult Failed(
        RepriceRequirementTechnicalProposalItemFailure failure) =>
        new(false, failure, null);
}

public sealed class PriceRequirementTechnicalProposalService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IProductSystemCatalogRepository productSystemCatalog,
    IGlassTypeCatalogRepository glassCatalog,
    IFinishTypeCatalogRepository finishCatalog,
    ITechnicalProposalItemToHistoricalPricingMapper mapper,
    IHistoricalTechnicalPriceEstimator technicalEstimator,
    IHistoricalCommercialPriceEstimator commercialEstimator,
    IOperationCancellationRegistry cancellationRegistry)
{
    private const string PricingBasis = "PUBLIC_QUOTED_ITEM_PRICES";
    private const string Currency = "COP";
    private const string PriceSourceCurrentEstimate = "CURRENT_ESTIMATE";
    private const string PriceSourceLastValidCurrent = "LAST_VALID_CURRENT";
    private const string LastValidPricePreservedReason = "LAST_VALID_PRICE_PRESERVED";

    public async Task<PriceRequirementTechnicalProposalResult> ExecuteAsync(
        PriceRequirementTechnicalProposalCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty)
        {
            return PriceRequirementTechnicalProposalResult.Failed(
                PriceRequirementTechnicalProposalFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return PriceRequirementTechnicalProposalResult.Failed(
                PriceRequirementTechnicalProposalFailure.Unauthorized);
        }

        var access = await ValidateAccessAsync(
            command.RequirementId,
            userId,
            cancellationToken);
        if (access != PriceRequirementTechnicalProposalFailure.None)
        {
            return PriceRequirementTechnicalProposalResult.Failed(access);
        }

        var operationKey = RequirementOperationKeys.Pricing(
            command.RequirementId);
        var operationCancellationToken = cancellationRegistry.Register(
            operationKey,
            cancellationToken);

        try
        {
            var proposal = await requirementRepository.GetCurrentTechnicalProposalAsync(
                command.RequirementId,
                operationCancellationToken);
            if (proposal is null)
            {
                return PriceRequirementTechnicalProposalResult.Failed(
                    PriceRequirementTechnicalProposalFailure.TechnicalProposalNotFound);
            }

            if (proposal.IncludedItems.Count == 0)
            {
                return PriceRequirementTechnicalProposalResult.Failed(
                    PriceRequirementTechnicalProposalFailure.TechnicalProposalNoIncludedItems);
            }

            if (!proposal.IsCommerciallyConfirmed)
            {
                return PriceRequirementTechnicalProposalResult.Failed(
                    PriceRequirementTechnicalProposalFailure.TechnicalProposalNotConfirmed);
            }

            var systems = (await productSystemCatalog.ListActiveAsync(
                    operationCancellationToken))
                .ToDictionary(value => value.Id);
            var glasses = (await glassCatalog.GetActiveWithCurrentPriceRangesAsync(
                    operationCancellationToken))
                .ToDictionary(value => value.GlassTypeId);
            var finishes = (await finishCatalog.ListActiveAsync(
                    operationCancellationToken))
                .ToDictionary(value => value.Id);
            var snapshot = await requirementRepository.GetCurrentPricingSnapshotAsync(
                command.RequirementId,
                operationCancellationToken);
            if (snapshot is not null && IsCurrentSnapshot(proposal, snapshot))
            {
                return PriceRequirementTechnicalProposalResult.Success(
                    MapSnapshot(proposal, snapshot));
            }

            var pricing = await PriceAsync(
                proposal,
                systems,
                glasses,
                finishes,
                operationCancellationToken);
            operationCancellationToken.ThrowIfCancellationRequested();
            var createdSnapshot = CreateSnapshot(
                proposal,
                pricing,
                DateTimeOffset.UtcNow);
            if (snapshot is null)
            {
                requirementRepository.AddPricingSnapshot(createdSnapshot);
            }
            else
            {
                var currentSnapshot = await requirementRepository
                    .FindCurrentPricingSnapshotAsync(
                        command.RequirementId,
                        operationCancellationToken);
                if (currentSnapshot is null)
                {
                    requirementRepository.AddPricingSnapshot(createdSnapshot);
                }
                else
                {
                    requirementRepository.ReplacePricingSnapshot(
                        currentSnapshot,
                        createdSnapshot);
                }
            }

            await requirementRepository.SaveChangesAsync(
                operationCancellationToken);

            return PriceRequirementTechnicalProposalResult.Success(
                pricing with
                {
                    OriginalGrandTotal = createdSnapshot.OriginalGrandTotal,
                    CurrentGrandTotal = createdSnapshot.CurrentGrandTotal,
                    DeltaGrandTotal = createdSnapshot.DeltaGrandTotal,
                    Items = pricing.Items.Select(item =>
                        item with
                        {
                            OriginalUnit = item.Unit,
                            CurrentUnit = item.Unit,
                            DeltaUnit = ZeroDelta(item.Unit),
                            OriginalLine = item.Line,
                            CurrentLine = item.Line,
                            DeltaLine = ZeroDelta(item.Line)
                        }).ToArray()
                });
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return PriceRequirementTechnicalProposalResult.Failed(
                PriceRequirementTechnicalProposalFailure.Cancelled);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return PriceRequirementTechnicalProposalResult.Failed(
                PriceRequirementTechnicalProposalFailure.QueryError);
        }
        finally
        {
            cancellationRegistry.Complete(operationKey);
        }
    }

    public Task<bool> CancelAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        if (requirementId == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(cancellationRegistry.TryCancel(
            RequirementOperationKeys.Pricing(requirementId)));
    }

    public async Task<RepriceRequirementTechnicalProposalItemResult> RepriceItemAsync(
        RepriceRequirementTechnicalProposalItemCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty
            || command.TechnicalProposalItemId == Guid.Empty
            || (command.SystemId is null
                && command.GlassTypeId is null
                && command.FinishTypeId is null
                && command.Quantity is null
                && command.WidthMillimeters is null
                && command.HeightMillimeters is null)
            || command.Quantity is <= 0
            || command.WidthMillimeters is <= 0
            || command.HeightMillimeters is <= 0)
        {
            return RepriceRequirementTechnicalProposalItemResult.Failed(
                RepriceRequirementTechnicalProposalItemFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return RepriceRequirementTechnicalProposalItemResult.Failed(
                RepriceRequirementTechnicalProposalItemFailure.Unauthorized);
        }

        var access = await ValidateAccessAsync(
            command.RequirementId,
            userId,
            cancellationToken);
        if (access != PriceRequirementTechnicalProposalFailure.None)
        {
            return RepriceRequirementTechnicalProposalItemResult.Failed(
                MapRepriceAccessFailure(access));
        }

        try
        {
            await using var transaction =
                await requirementRepository.BeginPricingUpdateTransactionAsync(
                    cancellationToken);
            var proposal =
                await requirementRepository.FindCurrentTechnicalProposalForUpdateAsync(
                    command.RequirementId,
                    cancellationToken);
            if (proposal is null)
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure
                        .TechnicalProposalNotFound);
            }

            var proposalItem = proposal.Items.SingleOrDefault(item =>
                item.Id == command.TechnicalProposalItemId);
            if (proposalItem is null)
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure
                        .TechnicalProposalItemNotFound);
            }

            if (!proposalItem.IsIncluded)
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure
                        .TechnicalProposalItemExcluded);
            }

            if (!proposal.IsCommerciallyConfirmed)
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure
                        .TechnicalProposalNotConfirmed);
            }

            var systems = (await productSystemCatalog.ListActiveAsync(cancellationToken))
                .ToDictionary(value => value.Id);
            var selectableSystems =
                (await productSystemCatalog.ListActiveSelectableAsync(cancellationToken))
                .ToDictionary(value => value.Id);
            var glasses = (await glassCatalog.GetActiveWithCurrentPriceRangesAsync(cancellationToken))
                .ToDictionary(value => value.GlassTypeId);
            var finishes = (await finishCatalog.ListActiveAsync(cancellationToken))
                .ToDictionary(value => value.Id);

            if (command.SystemId is { } requestedSystem
                && (!selectableSystems.TryGetValue(requestedSystem, out var selectedSystem)
                    || !selectedSystem.IsActive
                    || !selectedSystem.IsSelectable))
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure
                        .InvalidSystemSelection);
            }

            if (command.GlassTypeId is { } requestedGlass
                && (!glasses.TryGetValue(requestedGlass, out var selectedGlass)
                    || !selectedGlass.IsActive
                    || !selectedGlass.IsSelectable))
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure
                        .InvalidGlassSelection);
            }

            if (command.FinishTypeId is { } requestedFinish
                && (!finishes.TryGetValue(requestedFinish, out var selectedFinish)
                    || !selectedFinish.IsActive
                    || !selectedFinish.IsSelectable))
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure
                        .InvalidFinishSelection);
            }

            var snapshot =
                await requirementRepository.FindCurrentPricingSnapshotForUpdateAsync(
                    command.RequirementId,
                    cancellationToken);
            if (snapshot is null || !IsCurrentSnapshot(proposal, snapshot))
            {
                var initialPricing = await PriceAsync(
                    proposal,
                    systems,
                    glasses,
                    finishes,
                    cancellationToken);
                var replacement = CreateSnapshot(
                    proposal,
                    initialPricing,
                    DateTimeOffset.UtcNow);
                if (snapshot is null)
                {
                    requirementRepository.AddPricingSnapshot(replacement);
                    snapshot = replacement;
                }
                else
                {
                    requirementRepository.ReplacePricingSnapshot(snapshot, replacement);
                }
            }

            var itemSnapshot = snapshot.Items.SingleOrDefault(item =>
                item.TechnicalProposalItemId == proposalItem.Id);
            if (itemSnapshot is null)
            {
                return RepriceRequirementTechnicalProposalItemResult.Failed(
                    RepriceRequirementTechnicalProposalItemFailure.QueryError);
            }

            var previousCommercialState = CurrentCommercialState(proposalItem);
            var baseConfiguration = EffectiveConfiguration(proposalItem);
            var newSystemId = command.SystemId ?? baseConfiguration.SystemId;
            var newGlassTypeId = command.GlassTypeId ?? baseConfiguration.GlassTypeId;
            var newFinishTypeId = command.FinishTypeId ?? baseConfiguration.FinishTypeId;
            var requireSystemMatchedComparable = command.SystemId is not null
                && newSystemId != baseConfiguration.SystemId;
            var now = DateTimeOffset.UtcNow;
            proposalItem.ApplyManualDataOverride(
                command.Quantity,
                command.WidthMillimeters,
                command.HeightMillimeters);
            proposalItem.Select(
                newSystemId,
                newGlassTypeId,
                newFinishTypeId,
                userId,
                now);
            var commercialStateChanged = previousCommercialState
                != CurrentCommercialState(proposalItem);
            if (commercialStateChanged)
            {
                proposal.MarkCommerciallyChanged();
            }

            var commercialLine = proposal.Requirement?.CommercialLine is { } line
                ? line.ToString().ToUpperInvariant()
                : null;
            var repriced = await PriceItemAsync(
                proposalItem,
                commercialLine,
                systems,
                glasses,
                finishes,
                requireSystemMatchedComparable,
                cancellationToken);
            var hasNewEstimate = HasCompleteEstimate(repriced.Line);
            var hasLastValidCurrent = HasCompleteCurrentEstimate(itemSnapshot);
            TechnicalProposalPricingItemReadModel responseItem;
            if (!hasNewEstimate && hasLastValidCurrent)
            {
                itemSnapshot.UpdateCurrent(
                    newSystemId,
                    newGlassTypeId,
                    newFinishTypeId,
                    itemSnapshot.CurrentStatus,
                    itemSnapshot.CurrentUnitMinimum,
                    itemSnapshot.CurrentUnitExpected,
                    itemSnapshot.CurrentUnitMaximum,
                    itemSnapshot.CurrentLineMinimum,
                    itemSnapshot.CurrentLineExpected,
                    itemSnapshot.CurrentLineMaximum,
                    now);
                snapshot.RecalculateCurrentGrandTotal(now);
                responseItem = ApplySnapshot(
                    PreserveLastValidCurrent(repriced),
                    itemSnapshot);
            }
            else
            {
                itemSnapshot.UpdateCurrent(
                    newSystemId,
                    newGlassTypeId,
                    newFinishTypeId,
                    repriced.Status,
                    repriced.Unit.Minimum,
                    repriced.Unit.Expected,
                    repriced.Unit.Maximum,
                    repriced.Line.Minimum,
                    repriced.Line.Expected,
                    repriced.Line.Maximum,
                    now);
                snapshot.RecalculateCurrentGrandTotal(now);
                responseItem = ApplySnapshot(
                    repriced with
                    {
                        PriceSource = hasNewEstimate ? PriceSourceCurrentEstimate : null,
                        RepriceAttemptState = hasNewEstimate ? "PRICEABLE" : repriced.Status,
                        RepriceAttemptReason = hasNewEstimate
                            ? null
                            : FirstReason(repriced)
                    },
                    itemSnapshot);
            }

            if (commercialStateChanged)
            {
                snapshot.MarkForCommercialRevision(
                    proposal.CommercialRevision,
                    now);
            }

            await requirementRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return RepriceRequirementTechnicalProposalItemResult.Success(
                new RepriceRequirementTechnicalProposalItemReadModel(
                    snapshot.RequirementId,
                    snapshot.TechnicalProposalId,
                    proposalItem.Id,
                    newSystemId,
                    newGlassTypeId,
                    newFinishTypeId,
                    responseItem,
                    snapshot.OriginalGrandTotal,
                    snapshot.CurrentGrandTotal,
                    snapshot.DeltaGrandTotal));
        }
        catch (RequirementPersistenceException)
        {
            return RepriceRequirementTechnicalProposalItemResult.Failed(
                RepriceRequirementTechnicalProposalItemFailure.PersistenceError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return RepriceRequirementTechnicalProposalItemResult.Failed(
                RepriceRequirementTechnicalProposalItemFailure.QueryError);
        }
    }

    private async Task<RequirementTechnicalProposalPricingReadModel> PriceAsync(
        RequirementTechnicalProposal proposal,
        IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems,
        IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses,
        IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes,
        CancellationToken cancellationToken)
    {
        var items = new List<TechnicalProposalPricingItemReadModel>();
        var commercialLine = proposal.Requirement?.CommercialLine is { } line
            ? line.ToString().ToUpperInvariant()
            : null;
        foreach (var item in proposal.IncludedItems.OrderBy(value => value.ExtractedItem.Sequence).ThenBy(value => value.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(await PriceItemAsync(
                item,
                commercialLine,
                systems,
                glasses,
                finishes,
                false,
                cancellationToken));
        }

        var priced = items.Where(value => value.Status == "PRICEABLE").ToArray();
        var subtotal = AggregateLineRanges(priced);
        var notPriceable = items.Count - priced.Length;
        var assumptions = items.SelectMany(value => value.Assumptions)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var missing = items.SelectMany(value => value.MissingData)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        return new RequirementTechnicalProposalPricingReadModel(
            proposal.RequirementId,
            proposal.Id,
            Currency,
            PricingBasis,
            items.Count,
            priced.Length,
            notPriceable,
            items.Count(value => value.RequiresReview),
            subtotal,
            notPriceable == 0,
            items.Any(value => value.RequiresReview),
            assumptions,
            missing,
            items);
    }

    private async Task<TechnicalProposalPricingItemReadModel> PriceItemAsync(
        RequirementTechnicalProposalItem proposalItem,
        string? commercialLine,
        IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems,
        IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses,
        IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes,
        bool requireSystemMatchedComparable,
        CancellationToken cancellationToken)
    {
        var item = proposalItem.ExtractedItem;
        var missing = new List<string>();
        ProductSystemCatalogReadModel? system = null;
        GlassTypeCatalogReadModel? glass = null;
        FinishTypeCatalogReadModel? finish = null;
        var effective = EffectiveConfiguration(proposalItem);
        if (effective.SystemId is not { } systemId
            || !systems.TryGetValue(systemId, out system))
        {
            missing.Add(effective.MissingSystemCode);
        }
        if (effective.GlassTypeId is not { } glassId
            || !glasses.TryGetValue(glassId, out glass))
        {
            missing.Add(effective.MissingGlassCode);
        }
        if (effective.FinishTypeId is not { } finishId
            || !finishes.TryGetValue(finishId, out finish))
        {
            missing.Add(effective.MissingFinishCode);
        }
        if (proposalItem.EffectiveQuantity is not > 0)
        {
            missing.Add("QUANTITY_MISSING");
        }
        if (proposalItem.EffectiveWidthMillimeters is not > 0
            || proposalItem.EffectiveHeightMillimeters is not > 0)
        {
            missing.Add("MEASUREMENTS_MISSING");
        }

        if (missing.Count > 0 || system is null || glass is null || finish is null)
        {
            return EmptyItem(
                proposalItem,
                "NOT_PRICEABLE",
                effective.Source,
                null,
                proposalItem.RequiresReview,
                [],
                missing);
        }

        var mapping = WithPricingContext(
            mapper.Map(proposalItem, system, glass, finish),
            commercialLine,
            requireSystemMatchedComparable);
        if (mapping.PricingArea is not > 0)
        {
            return EmptyItem(proposalItem, "NOT_PRICEABLE", effective.Source,
                mapping.PricingArea,
                true, mapping.MappingWarnings, ["AREA_MISSING"]);
        }
        if (mapping.Quantity <= 0)
        {
            return EmptyItem(proposalItem, "NOT_PRICEABLE", effective.Source,
                mapping.PricingArea,
                true, mapping.MappingWarnings, ["QUANTITY_MISSING"]);
        }

        var technical = await technicalEstimator.EstimateAsync(
            mapping.CandidateQuery,
            cancellationToken);
        var commercial = commercialEstimator.FromTechnical(technical);
        if (commercial.PricingBasis != HistoricalPricingBasis.PublicQuotedItemPrices)
        {
            throw new InvalidDataException(
                "NEWPIPE pricing solo admite PUBLIC_QUOTED_ITEM_PRICES.");
        }

        var unit = new TechnicalProposalPricingMoneyRange(
            commercial.UnitMinimum,
            commercial.UnitExpected,
            commercial.UnitMaximum);
        var line = new TechnicalProposalPricingMoneyRange(
            Multiply(commercial.UnitMinimum, mapping.Quantity),
            Multiply(commercial.UnitExpected, mapping.Quantity),
            Multiply(commercial.UnitMaximum, mapping.Quantity));
        var hasEstimate = line.Minimum is not null
            && line.Expected is not null
            && line.Maximum is not null;
        var status = hasEstimate ? "PRICEABLE" : "NO_ESTIMATE";
        var review = proposalItem.RequiresReview
            || mapping.RequiresReview
            || commercial.RequiresReview
            || commercial.ConfidenceLevel is HistoricalPriceConfidenceLevel.Low
                or HistoricalPriceConfidenceLevel.Medium
            || !hasEstimate;

        return new TechnicalProposalPricingItemReadModel(
            proposalItem.Id,
            item.Id,
            item.Ai2ElementId,
            item.Sequence,
            item.Reference,
            item.Description,
            status,
            effective.Source,
            mapping.Quantity,
            mapping.PricingArea,
            unit,
            line,
            commercial.ConfidenceScore,
            commercial.ConfidenceLevel.ToString().ToUpperInvariant(),
            review,
            mapping.MappingWarnings,
            commercial.Assumptions,
            commercial.MissingData,
            technical.Comparables.Take(5).Select(MapComparable).ToArray(),
            unit,
            unit,
            ZeroDelta(unit),
            line,
            line,
            ZeroDelta(line),
            hasEstimate ? PriceSourceCurrentEstimate : null,
            hasEstimate ? "PRICEABLE" : status,
            hasEstimate ? null : FirstReason(commercial.MissingData));
    }

    private static TechnicalProposalPricingComparableReadModel MapComparable(
        HistoricalTechnicalPriceComparable value) =>
        new(
            value.CandidateId,
            value.HistoricalReference,
            value.PublicUnitPrice,
            value.ProjectedPrice,
            value.BackendTechnicalScore,
            value.Ai2SimilarityScore,
            value.SimilarityLevel,
            value.FinalWeight,
            value.MatchingTier,
            value.MatchedSystem,
            value.MatchedGlass,
            value.MatchedFinish,
            value.MatchedCommercialLine,
            value.FallbackReasons ?? []);

    private static TechnicalProposalPricingItemReadModel EmptyItem(
        RequirementTechnicalProposalItem proposalItem,
        string status,
        string configurationSource,
        decimal? pricingArea,
        bool requiresReview,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> missing)
    {
        var item = proposalItem.ExtractedItem;
        return new TechnicalProposalPricingItemReadModel(
            proposalItem.Id,
            item.Id,
            item.Ai2ElementId,
            item.Sequence,
            item.Reference,
            item.Description,
            status,
            configurationSource,
            proposalItem.EffectiveQuantity,
            pricingArea,
            new TechnicalProposalPricingMoneyRange(null, null, null),
            new TechnicalProposalPricingMoneyRange(null, null, null),
            null,
            null,
            requiresReview,
            warnings,
            [],
            missing,
            [],
            new TechnicalProposalPricingMoneyRange(null, null, null),
            new TechnicalProposalPricingMoneyRange(null, null, null),
            new TechnicalProposalPricingMoneyRange(null, null, null),
            new TechnicalProposalPricingMoneyRange(null, null, null),
            new TechnicalProposalPricingMoneyRange(null, null, null),
            new TechnicalProposalPricingMoneyRange(null, null, null),
            null,
            status,
            FirstReason(missing));
    }

    private static RequirementPricingSnapshot CreateSnapshot(
        RequirementTechnicalProposal proposal,
        RequirementTechnicalProposalPricingReadModel pricing,
        DateTimeOffset createdAtUtc)
    {
        var snapshot = RequirementPricingSnapshot.Create(
            proposal.RequirementId,
            proposal.Id,
            proposal.CommercialRevision,
            pricing.Currency,
            pricing.PricingBasis,
            pricing.EstimatedSubtotal.Expected,
            pricing.EstimatedSubtotal.Expected,
            createdAtUtc);
        var itemsById = proposal.Items.ToDictionary(item => item.Id);
        foreach (var pricingItem in pricing.Items)
        {
            var proposalItem = itemsById[pricingItem.ProposalItemId];
            var effective = EffectiveConfiguration(proposalItem);
            snapshot.AddItem(RequirementPricingItemSnapshot.Create(
                snapshot.Id,
                proposalItem.Id,
                effective.SystemId,
                effective.GlassTypeId,
                effective.FinishTypeId,
                pricingItem.Status,
                pricingItem.Unit.Minimum,
                pricingItem.Unit.Expected,
                pricingItem.Unit.Maximum,
                pricingItem.Line.Minimum,
                pricingItem.Line.Expected,
                pricingItem.Line.Maximum,
                createdAtUtc));
        }

        return snapshot;
    }

    private static bool IsCurrentSnapshot(
        RequirementTechnicalProposal proposal,
        RequirementPricingSnapshot snapshot) =>
        snapshot.TechnicalProposalId == proposal.Id
        && snapshot.TechnicalProposalCommercialRevision
            == proposal.CommercialRevision;

    private static RequirementTechnicalProposalPricingReadModel MapSnapshot(
        RequirementTechnicalProposal proposal,
        RequirementPricingSnapshot snapshot)
    {
        var itemsById = proposal.Items.ToDictionary(item => item.Id);
        var items = snapshot.Items
            .OrderBy(value => itemsById[value.TechnicalProposalItemId]
                .ExtractedItem.Sequence)
            .ThenBy(value => value.TechnicalProposalItemId)
            .Select(itemSnapshot =>
            {
                var proposalItem = itemsById[itemSnapshot.TechnicalProposalItemId];
                return ApplySnapshot(
                    EmptyItem(
                        proposalItem,
                        itemSnapshot.CurrentStatus,
                        "SELECTED",
                        DisplayPricingArea(proposalItem),
                        itemSnapshot.CurrentStatus != "PRICEABLE",
                        [],
                        itemSnapshot.CurrentStatus == "PRICEABLE"
                            ? []
                            : ["NO_COMPARABLES"]),
                    itemSnapshot);
            })
            .ToArray();
        var priced = items.Count(item => item.Status == "PRICEABLE");

        var estimatedSubtotal = AggregateLineRanges(items);
        var deltaGrandTotal = estimatedSubtotal.Expected is null
            || snapshot.OriginalGrandTotal is null
                ? null
                : estimatedSubtotal.Expected - snapshot.OriginalGrandTotal;

        return new RequirementTechnicalProposalPricingReadModel(
            snapshot.RequirementId,
            snapshot.TechnicalProposalId,
            snapshot.Currency,
            snapshot.PricingBasis,
            items.Length,
            priced,
            items.Length - priced,
            items.Count(item => item.RequiresReview),
            estimatedSubtotal,
            items.All(item => item.Status == "PRICEABLE"),
            items.Any(item => item.RequiresReview),
            [],
            items.Any(item => item.Status != "PRICEABLE")
                ? ["NO_COMPARABLES"]
                : [],
            items,
            snapshot.OriginalGrandTotal,
            estimatedSubtotal.Expected,
            deltaGrandTotal);
    }

    private static TechnicalProposalPricingItemReadModel ApplySnapshot(
        TechnicalProposalPricingItemReadModel item,
        RequirementPricingItemSnapshot snapshot)
    {
        var originalUnit = new TechnicalProposalPricingMoneyRange(
            snapshot.OriginalUnitMinimum,
            snapshot.OriginalUnitExpected,
            snapshot.OriginalUnitMaximum);
        var currentUnit = new TechnicalProposalPricingMoneyRange(
            snapshot.CurrentUnitMinimum,
            snapshot.CurrentUnitExpected,
            snapshot.CurrentUnitMaximum);
        var originalLine = new TechnicalProposalPricingMoneyRange(
            snapshot.OriginalLineMinimum,
            snapshot.OriginalLineExpected,
            snapshot.OriginalLineMaximum);
        var currentLine = new TechnicalProposalPricingMoneyRange(
            snapshot.CurrentLineMinimum,
            snapshot.CurrentLineExpected,
            snapshot.CurrentLineMaximum);

        return item with
        {
            Status = snapshot.CurrentStatus,
            Unit = currentUnit,
            Line = currentLine,
            OriginalUnit = originalUnit,
            CurrentUnit = currentUnit,
            DeltaUnit = Delta(currentUnit, originalUnit),
            OriginalLine = originalLine,
            CurrentLine = currentLine,
            DeltaLine = Delta(currentLine, originalLine)
        };
    }

    private static TechnicalProposalPricingItemReadModel PreserveLastValidCurrent(
        TechnicalProposalPricingItemReadModel repriced)
    {
        var missing = AppendDistinct(
            repriced.MissingData,
            [LastValidPricePreservedReason]);
        var assumptions = AppendDistinct(
            repriced.Assumptions,
            [LastValidPricePreservedReason]);

        return repriced with
        {
            Status = "PRICEABLE",
            RequiresReview = true,
            Assumptions = assumptions,
            MissingData = missing,
            PriceSource = PriceSourceLastValidCurrent,
            RepriceAttemptState = repriced.Status,
            RepriceAttemptReason = FirstReason(repriced)
        };
    }

    private static bool HasCompleteEstimate(TechnicalProposalPricingMoneyRange range) =>
        range.Minimum is not null
        && range.Expected is not null
        && range.Maximum is not null;

    private static bool HasCompleteCurrentEstimate(
        RequirementPricingItemSnapshot itemSnapshot) =>
        itemSnapshot.CurrentStatus == "PRICEABLE"
        && itemSnapshot.CurrentUnitMinimum is not null
        && itemSnapshot.CurrentUnitExpected is not null
        && itemSnapshot.CurrentUnitMaximum is not null
        && itemSnapshot.CurrentLineMinimum is not null
        && itemSnapshot.CurrentLineExpected is not null
        && itemSnapshot.CurrentLineMaximum is not null;

    private static IReadOnlyList<string> AppendDistinct(
        IReadOnlyList<string> source,
        IReadOnlyList<string> values) =>
        source.Concat(values)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string? FirstReason(TechnicalProposalPricingItemReadModel item) =>
        FirstReason(item.MissingData)
        ?? FirstReason(item.Assumptions)
        ?? item.Status;

    private static string? FirstReason(IReadOnlyList<string> values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static TechnicalProposalPricingMoneyRange AggregateLineRanges(
        IReadOnlyList<TechnicalProposalPricingItemReadModel> items)
    {
        if (items.Count == 0)
        {
            return new TechnicalProposalPricingMoneyRange(null, null, null);
        }

        var expectedValues = items.Select(item => item.Line.Expected).ToArray();
        if (expectedValues.Any(value => value is null))
        {
            return new TechnicalProposalPricingMoneyRange(null, null, null);
        }

        var expected = expectedValues.Sum(value => value!.Value);
        var downside = CombineUncertainty(
            items,
            item => item.Line.Expected is null || item.Line.Minimum is null
                ? (decimal?)null
                : item.Line.Expected.Value - item.Line.Minimum.Value,
            expected);
        var upside = CombineUncertainty(
            items,
            item => item.Line.Expected is null || item.Line.Maximum is null
                ? (decimal?)null
                : item.Line.Maximum.Value - item.Line.Expected.Value,
            expected);

        return new TechnicalProposalPricingMoneyRange(
            downside is null ? null : expected - downside.Value,
            expected,
            upside is null ? null : expected + upside.Value);
    }

    private static decimal? CombineUncertainty(
        IReadOnlyList<TechnicalProposalPricingItemReadModel> items,
        Func<TechnicalProposalPricingItemReadModel, decimal?> spreadSelector,
        decimal expectedTotal)
    {
        var values = items.Select(spreadSelector).ToArray();
        if (values.Any(value => value is null))
        {
            return null;
        }

        var sumOfSquares = values.Sum(value => value!.Value * value.Value);
        var rootSumSquare = (decimal)Math.Sqrt((double)sumOfSquares);
        var linear = values.Sum(value => value!.Value);
        var weakExpected = items
            .Where(IsWeakGlobalEvidence)
            .Sum(item => item.Line.Expected!.Value);
        var weakShare = expectedTotal <= 0m
            ? 1m
            : Math.Clamp(weakExpected / expectedTotal, 0m, 1m);
        return rootSumSquare + (linear - rootSumSquare) * weakShare;
    }

    private static bool IsWeakGlobalEvidence(
        TechnicalProposalPricingItemReadModel item) =>
        item.RequiresReview
        || string.Equals(item.ConfidenceLevel, "LOW", StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.ConfidenceLevel, "MEDIUM", StringComparison.OrdinalIgnoreCase);
    private static TechnicalProposalPricingMoneyRange ZeroDelta(
        TechnicalProposalPricingMoneyRange range) =>
        new(
            range.Minimum is null ? null : 0m,
            range.Expected is null ? null : 0m,
            range.Maximum is null ? null : 0m);

    private static TechnicalProposalPricingMoneyRange Delta(
        TechnicalProposalPricingMoneyRange current,
        TechnicalProposalPricingMoneyRange original) =>
        new(
            current.Minimum is null || original.Minimum is null
                ? null
                : current.Minimum - original.Minimum,
            current.Expected is null || original.Expected is null
                ? null
                : current.Expected - original.Expected,
            current.Maximum is null || original.Maximum is null
                ? null
                : current.Maximum - original.Maximum);

    private static RepriceRequirementTechnicalProposalItemFailure
        MapRepriceAccessFailure(PriceRequirementTechnicalProposalFailure failure) =>
        failure switch
        {
            PriceRequirementTechnicalProposalFailure.InvalidRequest =>
                RepriceRequirementTechnicalProposalItemFailure.InvalidRequest,
            PriceRequirementTechnicalProposalFailure.Unauthorized =>
                RepriceRequirementTechnicalProposalItemFailure.Unauthorized,
            PriceRequirementTechnicalProposalFailure.InactiveUser =>
                RepriceRequirementTechnicalProposalItemFailure.InactiveUser,
            PriceRequirementTechnicalProposalFailure.RequirementNotFound =>
                RepriceRequirementTechnicalProposalItemFailure.RequirementNotFound,
            PriceRequirementTechnicalProposalFailure.PreQuoteNotFound =>
                RepriceRequirementTechnicalProposalItemFailure.PreQuoteNotFound,
            PriceRequirementTechnicalProposalFailure.ProjectNotFound =>
                RepriceRequirementTechnicalProposalItemFailure.ProjectNotFound,
            PriceRequirementTechnicalProposalFailure.InactiveProject =>
                RepriceRequirementTechnicalProposalItemFailure.InactiveProject,
            PriceRequirementTechnicalProposalFailure.ClientNotFound =>
                RepriceRequirementTechnicalProposalItemFailure.ClientNotFound,
            PriceRequirementTechnicalProposalFailure.InactiveClient =>
                RepriceRequirementTechnicalProposalItemFailure.InactiveClient,
            _ => RepriceRequirementTechnicalProposalItemFailure.QueryError
        };

    private static EffectiveTechnicalConfiguration EffectiveConfiguration(
        RequirementTechnicalProposalItem proposalItem)
    {
        if (!proposalItem.HasSelectedConfiguration())
        {
            return new EffectiveTechnicalConfiguration(
                "SUGGESTED",
                proposalItem.SuggestedSystemId,
                proposalItem.SuggestedGlassTypeId,
                proposalItem.SuggestedFinishTypeId,
                "SYSTEM_NOT_RESOLVED",
                "GLASS_NOT_RESOLVED",
                "FINISH_NOT_RESOLVED");
        }

        return new EffectiveTechnicalConfiguration(
            "SELECTED",
            proposalItem.SelectedSystemId,
            proposalItem.SelectedGlassTypeId,
            proposalItem.SelectedFinishTypeId,
            "SELECTED_SYSTEM_MISSING",
            "SELECTED_GLASS_MISSING",
            "SELECTED_FINISH_MISSING");
    }

    private static TechnicalProposalItemHistoricalPricingMapping WithPricingContext(
        TechnicalProposalItemHistoricalPricingMapping mapping,
        string? commercialLine,
        bool requireSystemMatchedComparable) =>
        mapping with
        {
            CandidateQuery = mapping.CandidateQuery with
            {
                CommercialLine = string.IsNullOrWhiteSpace(commercialLine)
                    ? mapping.CandidateQuery.CommercialLine
                    : commercialLine.Trim(),
                RequireSystemMatchedComparable = requireSystemMatchedComparable
            }
        };

    private async Task<PriceRequirementTechnicalProposalFailure> ValidateAccessAsync(
        Guid requirementId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return PriceRequirementTechnicalProposalFailure.Unauthorized;
        }
        if (!user.IsActive)
        {
            return PriceRequirementTechnicalProposalFailure.InactiveUser;
        }

        var requirement = await requirementRepository.FindByIdAsync(requirementId, cancellationToken);
        if (requirement is null || !requirement.IsActive)
        {
            return PriceRequirementTechnicalProposalFailure.RequirementNotFound;
        }

        var preQuote = await preQuoteRepository.FindByIdAsync(requirement.PreQuoteId, cancellationToken);
        if (preQuote is null)
        {
            return PriceRequirementTechnicalProposalFailure.PreQuoteNotFound;
        }

        var project = await projectRepository.FindByIdAsync(preQuote.ProjectId, cancellationToken);
        if (project is null)
        {
            return PriceRequirementTechnicalProposalFailure.ProjectNotFound;
        }
        if (project.CreatedByUserId != userId)
        {
            return PriceRequirementTechnicalProposalFailure.RequirementNotFound;
        }
        if (!project.IsActive)
        {
            return PriceRequirementTechnicalProposalFailure.InactiveProject;
        }

        var client = await clientRepository.FindByIdAsync(project.ClientId, cancellationToken);
        if (client is null)
        {
            return PriceRequirementTechnicalProposalFailure.ClientNotFound;
        }

        return client.IsActive
            ? PriceRequirementTechnicalProposalFailure.None
            : PriceRequirementTechnicalProposalFailure.InactiveClient;
    }

    private static decimal? Multiply(decimal? value, decimal quantity) =>
        value is null ? null : value.Value * quantity;

    private static decimal? DisplayPricingArea(
        RequirementTechnicalProposalItem proposalItem) =>
        proposalItem.EffectiveWidthMillimeters is > 0
            && proposalItem.EffectiveHeightMillimeters is > 0
            ? proposalItem.EffectiveWidthMillimeters.Value
                * proposalItem.EffectiveHeightMillimeters.Value
                / 1_000_000m
            : proposalItem.ExtractedItem.AreaSquareMeters;

    private static CommercialState CurrentCommercialState(
        RequirementTechnicalProposalItem proposalItem)
    {
        var configuration = EffectiveConfiguration(proposalItem);
        return new CommercialState(
            configuration.SystemId,
            configuration.GlassTypeId,
            configuration.FinishTypeId,
            proposalItem.EffectiveQuantity,
            proposalItem.EffectiveWidthMillimeters,
            proposalItem.EffectiveHeightMillimeters);
    }

    private sealed record CommercialState(
        Guid? SystemId,
        Guid? GlassTypeId,
        Guid? FinishTypeId,
        int? Quantity,
        int? WidthMillimeters,
        int? HeightMillimeters);

    private sealed record EffectiveTechnicalConfiguration(
        string Source,
        Guid? SystemId,
        Guid? GlassTypeId,
        Guid? FinishTypeId,
        string MissingSystemCode,
        string MissingGlassCode,
        string MissingFinishCode);
}
