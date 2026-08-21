using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.HistoricalPricing;
using Application.PreQuotes.PriceRequirementTechnicalProposal;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class PriceRequirementTechnicalProposalServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SystemNapolesId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GlassTemp6Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FinishBlackId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Mapper_UsesSuggestedCatalogValuesInsteadOfRequestedRawSystem()
    {
        var proposalItem = ProposalItem(Item(requestedSystemRaw: "3831"));
        var mapper = new TechnicalProposalItemToHistoricalPricingMapper();

        var mapping = mapper.Map(
            proposalItem,
            SystemNapoles(),
            GlassTemp6(),
            FinishBlack());

        Assert.Equal("PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES", mapping.CandidateQuery.System);
        Assert.NotEqual("3831", mapping.CandidateQuery.System);
        Assert.Equal("COMPOSICION MONOLITICO TEMPLADO 6 MM INC", mapping.CandidateQuery.Glass);
        Assert.Equal(6m, mapping.CandidateQuery.GlassThickness);
        Assert.Equal("ALUCOLOR POLIESTER NEGRO MATE PP13", mapping.CandidateQuery.Finish);
        Assert.Equal(9.35m, mapping.CandidateQuery.Area);
    }

    [Fact]
    public void Mapper_WithAreaMismatch_UsesGeometryAreaAndRequiresReview()
    {
        var proposalItem = ProposalItem(Item(
            reference: "PV-15",
            width: 5320,
            height: 2500,
            area: 1.33m));
        var mapper = new TechnicalProposalItemToHistoricalPricingMapper();

        var mapping = mapper.Map(
            proposalItem,
            SystemNapoles(),
            GlassTemp6(),
            FinishBlack());

        Assert.Equal(13.30m, mapping.CandidateQuery.Area);
        Assert.Equal(13.30m, mapping.PricingArea);
        Assert.True(mapping.RequiresReview);
        Assert.Contains(
            TechnicalProposalItemToHistoricalPricingMapper.AreaDerivedFromGeometryWarning,
            mapping.MappingWarnings);
    }

    [Fact]
    public async Task Execute_WithQuantityFour_MultipliesLineOnceAndDoesNotReapplyAiu()
    {
        HistoricalCandidateQuery? captured = null;
        var context = CreateContext(
            [ProposalItem(Item(quantity: 4))],
            TechnicalEstimate(100m, 200m, 300m),
            query => captured = query);

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Pricing!.Items);
        Assert.Equal("PRICEABLE", item.Status);
        Assert.Equal(200m, item.Unit.Expected);
        Assert.Equal(800m, item.Line.Expected);
        Assert.Equal(800m, result.Pricing.EstimatedSubtotal.Expected);
        Assert.Equal("PUBLIC_QUOTED_ITEM_PRICES", result.Pricing.PricingBasis);
        Assert.Equal(4m, captured!.Quantity);
        Assert.Equal(9.35m, captured.Area);
    }

    [Fact]
    public async Task Execute_WithQuantityMissing_DoesNotAssumeOneAndDoesNotCallEstimator()
    {
        var context = CreateContext(
            [ProposalItem(Item(quantity: null))],
            TechnicalEstimate(100m, 200m, 300m));

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Pricing!.Items);
        Assert.Equal("NOT_PRICEABLE", item.Status);
        Assert.Null(item.Unit.Expected);
        Assert.Null(item.Line.Expected);
        Assert.Contains("QUANTITY_MISSING", item.MissingData);
        Assert.False(result.Pricing.IsCompleteTotal);
        await context.TechnicalEstimator.DidNotReceive().EstimateAsync(
            Arg.Any<HistoricalCandidateQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithNoComparables_ReturnsNoEstimateWithoutZeroPrice()
    {
        var context = CreateContext(
            [ProposalItem(Item())],
            TechnicalEstimate(null, null, null));

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Pricing!.Items);
        Assert.Equal("NO_ESTIMATE", item.Status);
        Assert.Null(item.Unit.Expected);
        Assert.Null(item.Line.Expected);
        Assert.False(result.Pricing.IsCompleteTotal);
        Assert.Null(result.Pricing.EstimatedSubtotal.Expected);
    }

    [Fact]
    public async Task Execute_WithMixedItems_SumsOnlyEstimatedLinesAndMarksPartialTotal()
    {
        var context = CreateContext(
            [
                ProposalItem(Item(reference: "PV-06")),
                ProposalItem(Item(reference: "V-01", quantity: null))
            ],
            TechnicalEstimate(100m, 200m, 300m));

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Pricing!.ItemCount);
        Assert.Equal(1, result.Pricing.PricedItemCount);
        Assert.Equal(1, result.Pricing.NotPriceableItemCount);
        Assert.Equal(200m, result.Pricing.EstimatedSubtotal.Expected);
        Assert.False(result.Pricing.IsCompleteTotal);
    }

    private static Context CreateContext(
        IReadOnlyList<RequirementTechnicalProposalItem> items,
        HistoricalTechnicalPriceEstimate technicalEstimate,
        Action<HistoricalCandidateQuery>? captureQuery = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        var glasses = Substitute.For<IGlassTypeCatalogRepository>();
        var finishes = Substitute.For<IFinishTypeCatalogRepository>();
        var technicalEstimator = Substitute.For<IHistoricalTechnicalPriceEstimator>();

        var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
        var client = Client.Create(ClientType.Company, "Client", null, null, null, null, null, null, null, UserId, At);
        var project = ProjectEntity.Create(client.Id, "P-001", "Project", null, null, UserId, At);
        var preQuote = PreQuote.Create(project.Id, UserId, At);
        var requirement = Requirement.Create(preQuote.Id, UserId, At);
        var proposal = RequirementTechnicalProposal.Create(requirement.Id, Guid.NewGuid(), Guid.NewGuid(), false, At);
        foreach (var item in items)
        {
            SetPrivateProperty(item, "TechnicalProposalId", proposal.Id);
            proposal.AddItem(item);
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        requirements.FindByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(requirement);
        requirements.GetCurrentTechnicalProposalAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(proposal);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>()).Returns(new PreQuoteDetails(
            preQuote.Id, preQuote.ProjectId, 0, preQuote.CreatedAtUtc, preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        systems.ListActiveAsync(Arg.Any<CancellationToken>()).Returns([SystemNapoles()]);
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>()).Returns([GlassTemp6()]);
        finishes.ListActiveAsync(Arg.Any<CancellationToken>()).Returns([FinishBlack()]);
        technicalEstimator.EstimateAsync(Arg.Do<HistoricalCandidateQuery>(query => captureQuery?.Invoke(query)), Arg.Any<CancellationToken>())
            .Returns(technicalEstimate);

        var commercial = new HistoricalCommercialPriceEstimator(technicalEstimator);
        var service = new PriceRequirementTechnicalProposalService(
            currentUser,
            identity,
            requirements,
            preQuotes,
            projects,
            clients,
            systems,
            glasses,
            finishes,
            new TechnicalProposalItemToHistoricalPricingMapper(),
            technicalEstimator,
            commercial);

        return new Context(service, requirement, technicalEstimator);
    }

    private static RequirementExtractedItem Item(
        string reference = "PV-06",
        int? quantity = 1,
        int width = 3740,
        int height = 2500,
        decimal area = 9.35m,
        string requestedSystemRaw = "3831") =>
        RequirementExtractedItem.Create(
            Guid.NewGuid(),
            "element-" + reference,
            reference == "V-01" ? 1 : 6,
            reference,
            "Puerta vidriera",
            StructuredElementType.Door,
            quantity,
            width,
            height,
            area,
            0.95m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            "SLIDING_DOOR",
            "CORREDIZA",
            null,
            null,
            null,
            "CORREDIZA",
            null,
            null,
            [],
            null,
            requestedSystemRaw,
            requestedSystemRaw,
            "templado 6 mm",
            "templado",
            "templado",
            6m,
            null,
            null,
            null,
            null,
            "MONOLITICO",
            null,
            null,
            false,
            "negro pintura al horno",
            "PAINTED",
            null,
            "BLACK",
            null,
            "MATTE",
            null,
            false,
            At);

    private static RequirementTechnicalProposalItem ProposalItem(
        RequirementExtractedItem item)
    {
        var proposalItem = RequirementTechnicalProposalItem.Create(
            Guid.NewGuid(),
            item.Id,
            SystemNapolesId,
            GlassTemp6Id,
            FinishBlackId,
            0.90m,
            0.90m,
            0.90m,
            0.90m,
            false,
            true,
            true,
            [],
            [],
            [],
            [],
            0,
            null,
            null,
            "Completed",
            At);
        SetPrivateProperty(proposalItem, "ExtractedItem", item);
        return proposalItem;
    }

    private static ProductSystemCatalogReadModel SystemNapoles() =>
        new(
            SystemNapolesId,
            "K70",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
            "VENECIA NAPOLES",
            "SLIDING_DOOR",
            "VENECIA NAPOLES",
            "SERIE 70",
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private static GlassTypeCatalogReadModel GlassTemp6() =>
        new(
            GlassTemp6Id,
            "TEMP_6",
            "COMPOSICION MONOLITICO TEMPLADO 6 MM INC",
            null,
            true,
            null,
            Family: "MONOLITHIC",
            Composition: "MONOLITICO",
            Treatment: "TEMPLADO",
            OuterThicknessMm: 6m);

    private static FinishTypeCatalogReadModel FinishBlack() =>
        new(
            FinishBlackId,
            "BLACK_MATTE",
            "ALUCOLOR POLIESTER NEGRO MATE PP13",
            "PAINTED",
            "NEGRO MATE",
            "MATTE",
            "PAINTED",
            "PP13",
            "ALUMINUM",
            true,
            false,
            true);

    private static HistoricalTechnicalPriceEstimate TechnicalEstimate(
        decimal? minimum,
        decimal? expected,
        decimal? maximum) =>
        new(
            "COP",
            minimum,
            expected,
            maximum,
            expected is null ? 0m : 0.81m,
            expected is null ? HistoricalPriceConfidenceLevel.Low : HistoricalPriceConfidenceLevel.High,
            "HISTORICAL_COMPARABLES",
            expected is null ? 0 : 5,
            expected is null ? 0 : 5,
            expected is null ? 0 : 3,
            expected is null,
            [],
            [],
            [],
            expected is null
                ? []
                : [new HistoricalTechnicalPriceComparable(
                    "candidate-1",
                    "quote-1",
                    "PV-06",
                    expected.Value,
                    0.9m,
                    0.95m,
                    "HIGH",
                    0.85m,
                    9.35m,
                    expected.Value,
                    true,
                    false)]);

    private static void SetPrivateProperty<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private sealed record Context(
        PriceRequirementTechnicalProposalService Service,
        Requirement Requirement,
        IHistoricalTechnicalPriceEstimator TechnicalEstimator);
}
