using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.Identity;
using Domain.PreQuotes;

namespace Application.PreQuotes.RequirementExperience;

public interface IRequirementExperienceConfigurationResolver
{
    Task<RequirementExperienceCurrentConfiguration> ResolveAsync(
        RequirementTechnicalProposal proposal,
        CancellationToken cancellationToken);
}

public sealed class RequirementExperienceConfigurationResolver(
    IRequirementRepository requirementRepository,
    IProductSystemCatalogRepository productSystemCatalogRepository,
    IGlassTypeCatalogRepository glassTypeCatalogRepository,
    RequirementExperienceLevel2Resolver level2Resolver)
    : IRequirementExperienceConfigurationResolver
{
    public async Task<RequirementExperienceCurrentConfiguration> ResolveAsync(
        RequirementTechnicalProposal proposal,
        CancellationToken cancellationToken)
    {
        var drafts = await requirementRepository
            .ListExperienceDraftsByTechnicalProposalIdAsync(
                proposal.Id,
                cancellationToken);
        var draftsByItem = drafts.ToDictionary(value => value.TechnicalProposalItemId);
        var selections = await requirementRepository
            .ListExperienceTechnicalSelectionsByTechnicalProposalIdAsync(
                proposal.Id,
                cancellationToken);
        var selectionsByItem = selections.ToDictionary(value =>
            value.TechnicalProposalItemId);
        var systems = await productSystemCatalogRepository
            .ListActiveSelectableAsync(cancellationToken);
        var glasses = await glassTypeCatalogRepository
            .GetActiveWithCurrentPriceRangesAsync(cancellationToken);

        var items = new List<RequirementExperienceCurrentConfigurationItem>();
        foreach (var item in proposal.Items.OrderBy(value => value.Sequence)
                     .ThenBy(value => value.Id))
        {
            if (!item.IsIncluded)
            {
                items.Add(RequirementExperienceCurrentConfigurationItem.Excluded(
                    item.Id,
                    item.Reference,
                    item.Sequence));
                continue;
            }

            draftsByItem.TryGetValue(item.Id, out var draft);
            selectionsByItem.TryGetValue(item.Id, out var selection);
            var level1 = draft is null
                ? null
                : GetRequirementExperienceDraftsService.MapDraft(item.Id, draft)
                    .Level1Resolution;
            var level2 = await level2Resolver.ResolveAsync(
                proposal,
                item,
                level1,
                selection,
                systems,
                glasses,
                cancellationToken);

            items.Add(RequirementExperienceCurrentConfigurationItem.Included(
                item.Id,
                item.Reference,
                item.Sequence,
                item.EffectiveQuantity,
                item.EffectiveWidthMillimeters,
                item.EffectiveHeightMillimeters,
                CalculateArea(item),
                item.EffectiveLocation,
                level2.System.Effective?.Id,
                level2.Glass.Effective?.Id,
                finishTypeId: null,
                level2.System.State,
                level2.Glass.State,
                level2.Finish.State,
                level2.System.OverrideState,
                level2.Glass.OverrideState,
                CollectReviewReasons(level2, item.QuantityResolution),
                CollectBlockingReasons(level2),
                item.QuantityResolution.Source,
                item.QuantityResolution.ExtractionStatusCode,
                item.QuantityResolution.ExtractionConfidence));
        }

        return new RequirementExperienceCurrentConfiguration(
            proposal.RequirementId,
            proposal.Id,
            items);
    }

    private static decimal? CalculateArea(RequirementTechnicalProposalItem item) =>
        item.EffectiveWidthMillimeters is > 0
        && item.EffectiveHeightMillimeters is > 0
            ? item.EffectiveWidthMillimeters.Value
                * item.EffectiveHeightMillimeters.Value
                / 1_000_000m
            : item.ExtractedItem?.AreaSquareMeters;

    private static IReadOnlyList<string> CollectReviewReasons(
        RequirementExperienceLevel2Response level2,
        RequirementQuantityResolution quantity)
    {
        var reasons = new List<string>();
        if (quantity.ReviewReason is { } quantityReviewReason)
        {
            reasons.Add(quantityReviewReason);
        }
        reasons.AddRange(level2.System.ReviewReasons);
        reasons.AddRange(level2.Glass.ReviewReasons);
        reasons.AddRange(level2.Finish.ReviewReasons);
        if (level2.System.Effective?.CompatibilityState
            == RequirementExperienceLevel2CandidateStates.RequiresReview)
        {
            reasons.AddRange(level2.System.Effective.ReviewReasons);
        }

        if (level2.Glass.Effective?.CompatibilityState
            == RequirementExperienceLevel2CandidateStates.RequiresReview)
        {
            reasons.AddRange(level2.Glass.Effective.ReviewReasons);
        }

        return NormalizeReasons(reasons);
    }

    private static IReadOnlyList<string> CollectBlockingReasons(
        RequirementExperienceLevel2Response level2)
    {
        var reasons = new List<string>();
        reasons.AddRange(level2.System.BlockingReasons);
        reasons.AddRange(level2.Glass.BlockingReasons);
        reasons.AddRange(level2.Finish.BlockingReasons);
        if (level2.System.OverrideState
            == RequirementExperienceLevel2OverrideStates.Stale)
        {
            reasons.Add("SYSTEM_STALE");
        }

        if (level2.Glass.OverrideState
            == RequirementExperienceLevel2OverrideStates.Stale)
        {
            reasons.Add("GLASS_STALE");
        }

        return NormalizeReasons(reasons);
    }

    private static IReadOnlyList<string> NormalizeReasons(IEnumerable<string> reasons) =>
        reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => reason.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}

public sealed class RequirementExperienceConfigurationService(
    IRequirementRepository requirementRepository,
    IIdentityRepository identityRepository,
    ICurrentUser currentUser,
    IRequirementExperienceConfigurationResolver configurationResolver,
    TimeProvider timeProvider)
{
    public async Task<RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>>
        GetStatusAsync(
            Guid technicalProposalId,
            CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(
            technicalProposalId,
            cancellationToken);
        if (!context.IsSuccess)
        {
            return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
                .Failed(context.Error);
        }

        return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
            .Success(await BuildStatusAsync(context.Value!, cancellationToken));
    }

    public async Task<RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>>
        ConfirmAsync(
            Guid technicalProposalId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await ConfirmCoreAsync(technicalProposalId, cancellationToken);
        }
        catch (RequirementExperienceConflictException)
        {
            return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
                .Failed(RequirementExperienceFailure.Conflict);
        }
        catch (RequirementPersistenceException)
        {
            return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
                .Failed(RequirementExperienceFailure.PersistenceError);
        }
    }

    private async Task<RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>>
        ConfirmCoreAsync(Guid technicalProposalId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
            return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
                .Failed(RequirementExperienceFailure.Unauthorized);

        await using var transaction = await requirementRepository
            .BeginExperienceConfigurationTransactionAsync(cancellationToken);
        await requirementRepository.LockExperienceConfigurationAsync(
            technicalProposalId, cancellationToken);
        var context = await LoadContextAsync(
            technicalProposalId,
            cancellationToken,
            requireOwner: true);
        if (!context.IsSuccess)
        {
            return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
                .Failed(context.Error);
        }

        var proposal = context.Value!.Proposal;
        var current = await configurationResolver.ResolveAsync(proposal, cancellationToken);
        var latest = await requirementRepository.GetLatestExperienceConfigurationConfirmationAsync(
            proposal.Id, cancellationToken);
        var readiness = RequirementExperienceConfigurationReadinessEvaluator.Evaluate(current);
        var status = BuildResponse(current, latest, readiness,
            latest is null ? RequirementExperienceConfigurationStatuses.Draft
                : Matches(latest, current) ? RequirementExperienceConfigurationStatuses.Confirmed
                : RequirementExperienceConfigurationStatuses.Stale);
        if (status.Status == RequirementExperienceConfigurationStatuses.Confirmed)
        {
            await transaction.CommitAsync(cancellationToken);
            return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
                .Success(status);
        }

        if (!status.CanConfirm)
        {
            await transaction.CommitAsync(cancellationToken);
            return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
                .Failed(RequirementExperienceFailure.ConfigurationBlocked, status);
        }

        var revision = (latest?.Revision ?? 0) + 1;
        var confirmation = RequirementExperienceConfigurationConfirmation.Create(
            proposal.RequirementId,
            proposal.Id,
            revision,
            context.Value.User.Id,
            timeProvider.GetUtcNow());
        foreach (var item in current.Items)
        {
            confirmation.AddItem(item.ToSnapshot(confirmation.Id));
        }

        requirementRepository.AddExperienceConfigurationConfirmation(
            confirmation);

        await requirementRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var response = BuildResponse(
            current,
            confirmation,
            readiness,
            RequirementExperienceConfigurationStatuses.Confirmed);
        return RequirementExperienceResult<RequirementExperienceConfigurationStatusResponse>
            .Success(response);
    }

    private async Task<RequirementExperienceResult<ConfigurationContext>>
        LoadContextAsync(
            Guid technicalProposalId,
            CancellationToken cancellationToken,
            bool requireOwner = false)
    {
        if (technicalProposalId == Guid.Empty)
        {
            return RequirementExperienceResult<ConfigurationContext>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return RequirementExperienceResult<ConfigurationContext>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);
        if (user is null)
        {
            return RequirementExperienceResult<ConfigurationContext>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return RequirementExperienceResult<ConfigurationContext>.Failed(
                RequirementExperienceFailure.InactiveUser);
        }

        var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
            technicalProposalId,
            cancellationToken);
        if (proposal is null)
        {
            return RequirementExperienceResult<ConfigurationContext>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        if (requireOwner && proposal.Requirement.PreQuote.Project.CreatedByUserId != user.Id)
        {
            return RequirementExperienceResult<ConfigurationContext>.Failed(
                RequirementExperienceFailure.Forbidden);
        }

        if (!CanAccess(proposal.Requirement.PreQuote.Project.CreatedByUserId, user))
        {
            return RequirementExperienceResult<ConfigurationContext>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        return RequirementExperienceResult<ConfigurationContext>.Success(
            new ConfigurationContext(proposal, user));
    }

    private async Task<RequirementExperienceConfigurationStatusResponse>
        BuildStatusAsync(
            ConfigurationContext context,
            CancellationToken cancellationToken)
    {
        var current = await configurationResolver.ResolveAsync(
            context.Proposal,
            cancellationToken);
        var latest = await requirementRepository
            .GetLatestExperienceConfigurationConfirmationAsync(
                context.Proposal.Id,
                cancellationToken);
        var readiness =
            RequirementExperienceConfigurationReadinessEvaluator.Evaluate(current);
        var status = latest is null
            ? RequirementExperienceConfigurationStatuses.Draft
            : Matches(latest, current)
                ? RequirementExperienceConfigurationStatuses.Confirmed
                : RequirementExperienceConfigurationStatuses.Stale;

        return BuildResponse(current, latest, readiness, status);
    }

    private static RequirementExperienceConfigurationStatusResponse BuildResponse(
        RequirementExperienceCurrentConfiguration current,
        RequirementExperienceConfigurationConfirmation? latest,
        RequirementExperienceConfigurationReadiness readiness,
        string status)
    {
        return new RequirementExperienceConfigurationStatusResponse(
            current.TechnicalProposalId,
            status,
            readiness.CanConfirm,
            latest?.Revision,
            latest?.ConfirmedAtUtc,
            latest?.ConfirmedByUserId,
            readiness.ReviewReasons,
            readiness.BlockingReasons,
            current.Items.Select(item =>
            {
                var itemReadiness = readiness.Items.TryGetValue(
                    item.TechnicalProposalItemId,
                    out var value)
                    ? value
                    : RequirementExperienceConfigurationItemReadiness.Ready;

                return
                new RequirementExperienceConfigurationItemStatusResponse(
                    item.TechnicalProposalItemId,
                    item.Reference,
                    item.Sequence,
                    item.IsIncluded,
                    item.Quantity,
                    item.WidthMm,
                    item.HeightMm,
                    item.AreaM2,
                    item.Location,
                    item.SystemId,
                    item.GlassTypeId,
                    item.FinishTypeId,
                    item.SystemState,
                    item.GlassState,
                    item.FinishState,
                    item.SystemOverrideState,
                    item.GlassOverrideState,
                    itemReadiness.State,
                    itemReadiness.ReviewReasons,
                    itemReadiness.BlockingReasons,
                    item.QuantitySource?.ToString(),
                    item.QuantityExtractionStatus,
                    item.QuantityExtractionConfidence);
            })
                .ToArray());
    }

    private static bool Matches(
        RequirementExperienceConfigurationConfirmation snapshot,
        RequirementExperienceCurrentConfiguration current)
    {
        var snapshotItems = snapshot.Items.ToDictionary(value =>
            value.TechnicalProposalItemId);
        if (snapshotItems.Count != current.Items.Count)
        {
            return false;
        }

        foreach (var item in current.Items)
        {
            if (!snapshotItems.TryGetValue(
                    item.TechnicalProposalItemId,
                    out var snapshotItem))
            {
                return false;
            }

            if (!Matches(snapshotItem, item))
            {
                return false;
            }
        }

        return !RequirementExperienceConfigurationReadinessEvaluator
            .HasStaleTechnicalState(current);
    }

    private static bool Matches(
        RequirementExperienceConfigurationConfirmationItem snapshot,
        RequirementExperienceCurrentConfigurationItem current)
    {
        if (snapshot.IsIncluded != current.IsIncluded)
        {
            return false;
        }

        if (!current.IsIncluded)
        {
            return true;
        }

        return snapshot.Quantity == current.Quantity
            && snapshot.WidthMillimeters == current.WidthMm
            && snapshot.HeightMillimeters == current.HeightMm
            && snapshot.Location == current.Location
            && snapshot.SystemId == current.SystemId
            && snapshot.GlassTypeId == current.GlassTypeId
            && snapshot.FinishTypeId == current.FinishTypeId;
    }

    private static bool CanAccess(Guid projectOwnerUserId, User user)
    {
        return user.Role == UserRole.Admin || user.Id == projectOwnerUserId;
    }

    private sealed record ConfigurationContext(
        RequirementTechnicalProposal Proposal,
        User User);
}

public sealed record RequirementExperienceCurrentConfiguration(
    Guid RequirementId,
    Guid TechnicalProposalId,
    IReadOnlyList<RequirementExperienceCurrentConfigurationItem> Items);

public sealed record RequirementExperienceCurrentConfigurationItem(
    Guid TechnicalProposalItemId,
    string? Reference,
    int? Sequence,
    bool IsIncluded,
    int? Quantity,
    int? WidthMm,
    int? HeightMm,
    decimal? AreaM2,
    string? Location,
    Guid? SystemId,
    Guid? GlassTypeId,
    Guid? FinishTypeId,
    string? SystemState,
    string? GlassState,
    string? FinishState,
    string? SystemOverrideState,
    string? GlassOverrideState,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons,
    RequirementQuantitySource? QuantitySource = null,
    string? QuantityExtractionStatus = null,
    decimal? QuantityExtractionConfidence = null)
{
    public static RequirementExperienceCurrentConfigurationItem Excluded(
        Guid technicalProposalItemId,
        string? reference = null,
        int? sequence = null) =>
        new(
            technicalProposalItemId,
            reference,
            sequence,
            IsIncluded: false,
            Quantity: null,
            WidthMm: null,
            HeightMm: null,
            AreaM2: null,
            Location: null,
            SystemId: null,
            GlassTypeId: null,
            FinishTypeId: null,
            SystemState: null,
            GlassState: null,
            FinishState: null,
            SystemOverrideState: null,
            GlassOverrideState: null,
            ReviewReasons: [],
            BlockingReasons: []);

    public static RequirementExperienceCurrentConfigurationItem Included(
        Guid technicalProposalItemId,
        string? reference,
        int? sequence,
        int? quantity,
        int? widthMm,
        int? heightMm,
        decimal? areaM2,
        string? location,
        Guid? systemId,
        Guid? glassTypeId,
        Guid? finishTypeId,
        string systemState,
        string glassState,
        string finishState,
        string systemOverrideState,
        string glassOverrideState,
        IReadOnlyList<string> reviewReasons,
        IReadOnlyList<string> blockingReasons,
        RequirementQuantitySource? quantitySource = null,
        string? quantityExtractionStatus = null,
        decimal? quantityExtractionConfidence = null) =>
        new(
            technicalProposalItemId,
            reference,
            sequence,
            IsIncluded: true,
            quantity,
            widthMm,
            heightMm,
            areaM2,
            location,
            systemId,
            glassTypeId,
            finishTypeId,
            systemState,
            glassState,
            finishState,
            systemOverrideState,
            glassOverrideState,
            reviewReasons,
            blockingReasons,
            quantitySource,
            quantityExtractionStatus,
            quantityExtractionConfidence);

    public RequirementExperienceConfigurationConfirmationItem ToSnapshot(
        Guid confirmationId) =>
        RequirementExperienceConfigurationConfirmationItem.Create(
            confirmationId,
            TechnicalProposalItemId,
            IsIncluded,
            Quantity,
            WidthMm,
            HeightMm,
            AreaM2,
            Location,
            SystemId,
            GlassTypeId,
            FinishTypeId,
            SystemState,
            GlassState,
            FinishState,
            SystemOverrideState,
            GlassOverrideState,
            ReviewReasons,
            BlockingReasons);
}

public static class RequirementExperienceConfigurationReadinessEvaluator
{
    public static RequirementExperienceConfigurationReadiness Evaluate(
        RequirementExperienceCurrentConfiguration configuration)
    {
        var blockingReasons = new List<string>();
        var proposalLevelBlockingReasons = new List<string>();
        var included = configuration.Items
            .Where(item => item.IsIncluded)
            .ToArray();
        if (included.Length == 0)
        {
            proposalLevelBlockingReasons.Add("NO_INCLUDED_ITEMS");
        }

        var itemReadiness = included.ToDictionary(
            item => item.TechnicalProposalItemId,
            EvaluateItem);

        blockingReasons.AddRange(proposalLevelBlockingReasons);
        blockingReasons.AddRange(itemReadiness.Values
            .SelectMany(item => item.BlockingReasons));
        var blocking = Normalize(blockingReasons);
        var review = Normalize(itemReadiness.Values
            .SelectMany(item => item.ReviewReasons));
        var state = blocking.Count > 0
            ? "BLOCKED"
            : review.Count > 0
                ? "WARNING"
                : "READY";

        return new RequirementExperienceConfigurationReadiness(
            state,
            blocking.Count == 0,
            review,
            blocking,
            itemReadiness);
    }

    public static bool HasStaleTechnicalState(
        RequirementExperienceCurrentConfiguration configuration) =>
        configuration.Items.Any(item => item.IsIncluded
            && (item.SystemOverrideState
                    == RequirementExperienceLevel2OverrideStates.Stale
                || item.GlassOverrideState
                    == RequirementExperienceLevel2OverrideStates.Stale
                || item.SystemState == RequirementExperienceLevel2States.Blocked
                || item.GlassState == RequirementExperienceLevel2States.Blocked));

    private static RequirementExperienceConfigurationItemReadiness EvaluateItem(
        RequirementExperienceCurrentConfigurationItem item)
    {
        var blockingReasons = new List<string>();
        var reviewReasons = new List<string>();

        if (item.Quantity is not > 0)
        {
            blockingReasons.Add("QUANTITY_REQUIRED");
        }

        if (item.WidthMm is not > 0)
        {
            blockingReasons.Add("WIDTH_REQUIRED");
        }

        if (item.HeightMm is not > 0)
        {
            blockingReasons.Add("HEIGHT_REQUIRED");
        }

        if (item.SystemId is null)
        {
            blockingReasons.Add("SYSTEM_REQUIRED");
        }

        if (item.GlassTypeId is null)
        {
            blockingReasons.Add("GLASS_REQUIRED");
        }

        if (item.FinishTypeId is null)
        {
            blockingReasons.Add("FINISH_REQUIRED");
        }

        if (item.FinishState
            == RequirementExperienceLevel2States.PendingExternalSource)
        {
            blockingReasons.Add("FINISH_PENDING_EXTERNAL_SOURCE");
        }

        if (item.SystemOverrideState
            == RequirementExperienceLevel2OverrideStates.Stale)
        {
            blockingReasons.Add("SYSTEM_STALE");
        }

        if (item.GlassOverrideState
            == RequirementExperienceLevel2OverrideStates.Stale)
        {
            blockingReasons.Add("GLASS_STALE");
        }

        if (item.SystemState == RequirementExperienceLevel2States.Blocked)
        {
            blockingReasons.Add("SYSTEM_BLOCKED");
        }

        if (item.GlassState == RequirementExperienceLevel2States.Blocked)
        {
            blockingReasons.Add("GLASS_BLOCKED");
        }

        blockingReasons.AddRange(item.BlockingReasons);
        reviewReasons.AddRange(item.ReviewReasons);

        var blocking = Normalize(blockingReasons);
        var review = Normalize(reviewReasons);
        var state = blocking.Count > 0
            ? "BLOCKED"
            : review.Count > 0
                ? "WARNING"
                : "READY";

        return new RequirementExperienceConfigurationItemReadiness(
            state,
            review,
            blocking);
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> reasons) =>
        reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => reason.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}

public sealed record RequirementExperienceConfigurationReadiness(
    string State,
    bool CanConfirm,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyDictionary<Guid, RequirementExperienceConfigurationItemReadiness> Items);

public sealed record RequirementExperienceConfigurationItemReadiness(
    string State,
    IReadOnlyList<string> ReviewReasons,
    IReadOnlyList<string> BlockingReasons)
{
    public static RequirementExperienceConfigurationItemReadiness Ready { get; } =
        new("READY", [], []);
}
