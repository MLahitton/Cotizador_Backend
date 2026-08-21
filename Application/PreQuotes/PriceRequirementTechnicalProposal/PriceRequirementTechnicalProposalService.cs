using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;

namespace Application.PreQuotes.PriceRequirementTechnicalProposal;

public sealed record PriceRequirementTechnicalProposalCommand(Guid RequirementId);

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
    QueryError
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
    IHistoricalCommercialPriceEstimator commercialEstimator)
{
    private const string PricingBasis = "PUBLIC_QUOTED_ITEM_PRICES";
    private const string Currency = "COP";

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

        try
        {
            var proposal = await requirementRepository.GetCurrentTechnicalProposalAsync(
                command.RequirementId,
                cancellationToken);
            if (proposal is null)
            {
                return PriceRequirementTechnicalProposalResult.Failed(
                    PriceRequirementTechnicalProposalFailure.TechnicalProposalNotFound);
            }

            var systems = (await productSystemCatalog.ListActiveAsync(cancellationToken))
                .ToDictionary(value => value.Id);
            var glasses = (await glassCatalog.GetActiveWithCurrentPriceRangesAsync(cancellationToken))
                .ToDictionary(value => value.GlassTypeId);
            var finishes = (await finishCatalog.ListActiveAsync(cancellationToken))
                .ToDictionary(value => value.Id);

            return PriceRequirementTechnicalProposalResult.Success(
                await PriceAsync(proposal, systems, glasses, finishes, cancellationToken));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return PriceRequirementTechnicalProposalResult.Failed(
                PriceRequirementTechnicalProposalFailure.QueryError);
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
        foreach (var item in proposal.Items.OrderBy(value => value.ExtractedItem.Sequence).ThenBy(value => value.Id))
        {
            items.Add(await PriceItemAsync(item, systems, glasses, finishes, cancellationToken));
        }

        var priced = items.Where(value => value.Status == "PRICEABLE").ToArray();
        var subtotal = new TechnicalProposalPricingMoneyRange(
            priced.Length == 0 ? null : priced.Sum(value => value.Line.Minimum!.Value),
            priced.Length == 0 ? null : priced.Sum(value => value.Line.Expected!.Value),
            priced.Length == 0 ? null : priced.Sum(value => value.Line.Maximum!.Value));
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
        IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems,
        IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses,
        IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes,
        CancellationToken cancellationToken)
    {
        var item = proposalItem.ExtractedItem;
        var missing = new List<string>();
        ProductSystemCatalogReadModel? system = null;
        GlassTypeCatalogReadModel? glass = null;
        FinishTypeCatalogReadModel? finish = null;
        if (!proposalItem.IsTechnicallyComplete || !proposalItem.IsPriceable)
        {
            missing.Add("TECHNICAL_PROPOSAL_ITEM_NOT_PRICEABLE");
        }
        if (proposalItem.SuggestedSystemId is not { } systemId || !systems.TryGetValue(systemId, out system))
        {
            missing.Add("SUGGESTED_SYSTEM_MISSING");
        }
        if (proposalItem.SuggestedGlassTypeId is not { } glassId || !glasses.TryGetValue(glassId, out glass))
        {
            missing.Add("SUGGESTED_GLASS_MISSING");
        }
        if (proposalItem.SuggestedFinishTypeId is not { } finishId || !finishes.TryGetValue(finishId, out finish))
        {
            missing.Add("SUGGESTED_FINISH_MISSING");
        }
        if (item.Quantity is not > 0)
        {
            missing.Add("QUANTITY_MISSING");
        }

        if (missing.Count > 0 || system is null || glass is null || finish is null)
        {
            return EmptyItem(proposalItem, "NOT_PRICEABLE", null, proposalItem.RequiresReview, [], missing);
        }

        var mapping = mapper.Map(proposalItem, system, glass, finish);
        if (mapping.PricingArea is not > 0)
        {
            return EmptyItem(proposalItem, "NOT_PRICEABLE", mapping.PricingArea,
                true, mapping.MappingWarnings, ["AREA_MISSING"]);
        }
        if (mapping.Quantity <= 0)
        {
            return EmptyItem(proposalItem, "NOT_PRICEABLE", mapping.PricingArea,
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
            technical.Comparables.Take(5).Select(MapComparable).ToArray());
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
            value.FinalWeight);

    private static TechnicalProposalPricingItemReadModel EmptyItem(
        RequirementTechnicalProposalItem proposalItem,
        string status,
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
            item.Quantity,
            pricingArea,
            new TechnicalProposalPricingMoneyRange(null, null, null),
            new TechnicalProposalPricingMoneyRange(null, null, null),
            null,
            null,
            requiresReview,
            warnings,
            [],
            missing,
            []);
    }

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
}
