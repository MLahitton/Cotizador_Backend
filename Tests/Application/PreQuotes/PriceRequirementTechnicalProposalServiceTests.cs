using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.Operations;
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
    private static readonly Guid SystemLsa9060Id = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid GlassTemp8Id = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid FinishWhiteId = Guid.Parse("77777777-7777-7777-7777-777777777777");

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
    public async Task Execute_WithPendingCommercialConfirmation_ReturnsNotConfirmed()
    {
        var context = CreateContext(
            [ProposalItem(Item())],
            TechnicalEstimate(100m, 200m, 300m),
            confirmProposal: false);

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            PriceRequirementTechnicalProposalFailure.TechnicalProposalNotConfirmed,
            result.Failure);
        await context.TechnicalEstimator.DidNotReceive().EstimateAsync(
            Arg.Any<HistoricalCandidateQuery>(),
            Arg.Any<CancellationToken>());
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
        Assert.Equal("SELECTED", item.ConfigurationSource);
        Assert.Equal(200m, item.Unit.Expected);
        Assert.Equal(800m, item.Line.Expected);
        Assert.Equal(800m, result.Pricing.EstimatedSubtotal.Expected);
        Assert.Equal("PUBLIC_QUOTED_ITEM_PRICES", result.Pricing.PricingBasis);
        Assert.Equal(4m, captured!.Quantity);
        Assert.Equal(9.35m, captured.Area);
        Assert.Equal("ESSENTIAL", captured.CommercialLine);
    }

    [Fact]
    public async Task Execute_WithSelectedSameAsSuggested_ReportsSelectedSource()
    {
        var proposalItem = ProposalItem(Item());
        proposalItem.Select(
            SystemNapolesId,
            GlassTemp6Id,
            FinishBlackId,
            UserId,
            At.AddMinutes(1));
        var context = CreateContext(
            [proposalItem],
            TechnicalEstimate(100m, 200m, 300m));

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Pricing!.Items);
        Assert.Equal("PRICEABLE", item.Status);
        Assert.Equal("SELECTED", item.ConfigurationSource);
    }

    [Fact]
    public async Task Execute_WithSelectedSystem_UsesSelectedSystemForComparables()
    {
        HistoricalCandidateQuery? captured = null;
        var proposalItem = ProposalItem(Item());
        proposalItem.Select(
            SystemLsa9060Id,
            GlassTemp6Id,
            FinishBlackId,
            UserId,
            At.AddMinutes(1));
        var context = CreateContext(
            [proposalItem],
            TechnicalEstimate(100m, 200m, 300m),
            query => captured = query);

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "PUERTA CORREDIZA LINEA PREMIUM LSA 9060",
            captured!.System);
        Assert.Equal(SystemNapolesId, proposalItem.SuggestedSystemId);
        Assert.Equal("SELECTED", Assert.Single(result.Pricing!.Items).ConfigurationSource);
    }

    [Fact]
    public async Task Execute_WithSelectedGlass_UsesSelectedGlassForComparables()
    {
        HistoricalCandidateQuery? captured = null;
        var proposalItem = ProposalItem(Item());
        proposalItem.Select(
            SystemNapolesId,
            GlassTemp8Id,
            FinishBlackId,
            UserId,
            At.AddMinutes(1));
        var context = CreateContext(
            [proposalItem],
            TechnicalEstimate(100m, 200m, 300m),
            query => captured = query);

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "COMPOSICION MONOLITICO TEMPLADO 8 MM INC",
            captured!.Glass);
        Assert.Equal(8m, captured.GlassThickness);
        Assert.Equal(GlassTemp6Id, proposalItem.SuggestedGlassTypeId);
        Assert.Equal("SELECTED", Assert.Single(result.Pricing!.Items).ConfigurationSource);
    }

    [Fact]
    public async Task Execute_WithSelectedFinish_UsesSelectedFinishForComparables()
    {
        HistoricalCandidateQuery? captured = null;
        var proposalItem = ProposalItem(Item());
        proposalItem.Select(
            SystemNapolesId,
            GlassTemp6Id,
            FinishWhiteId,
            UserId,
            At.AddMinutes(1));
        var context = CreateContext(
            [proposalItem],
            TechnicalEstimate(100m, 200m, 300m),
            query => captured = query);

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "ALUCOLOR POLIESTER BLANCO MATE",
            captured!.Finish);
        Assert.Equal(FinishBlackId, proposalItem.SuggestedFinishTypeId);
        Assert.Equal("SELECTED", Assert.Single(result.Pricing!.Items).ConfigurationSource);
    }

    [Fact]
    public async Task Execute_WithSelectedGlassOnlyFromSelectionFlow_UsesCompleteSelectedConfiguration()
    {
        HistoricalCandidateQuery? captured = null;
        var proposalItem = ProposalItem(Item());
        proposalItem.Select(
            SystemNapolesId,
            GlassTemp8Id,
            FinishBlackId,
            UserId,
            At.AddMinutes(1));
        var context = CreateContext(
            [proposalItem],
            TechnicalEstimate(100m, 200m, 300m),
            query => captured = query);

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("SELECTED", Assert.Single(result.Pricing!.Items).ConfigurationSource);
        Assert.Equal(
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
            captured!.System);
        Assert.Equal(
            "COMPOSICION MONOLITICO TEMPLADO 8 MM INC",
            captured.Glass);
        Assert.Equal("ALUCOLOR POLIESTER NEGRO MATE PP13", captured.Finish);
    }

    [Fact]
    public async Task Execute_WithInvalidSelectedCatalogReference_DoesNotFallbackToSuggested()
    {
        var proposalItem = ProposalItem(Item());
        proposalItem.Select(
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            GlassTemp6Id,
            FinishBlackId,
            UserId,
            At.AddMinutes(1));
        var context = CreateContext(
            [proposalItem],
            TechnicalEstimate(100m, 200m, 300m));

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Pricing!.Items);
        Assert.Equal("NOT_PRICEABLE", item.Status);
        Assert.Equal("SELECTED", item.ConfigurationSource);
        Assert.Contains("SELECTED_SYSTEM_MISSING", item.MissingData);
        await context.TechnicalEstimator.DidNotReceive().EstimateAsync(
            Arg.Any<HistoricalCandidateQuery>(),
            Arg.Any<CancellationToken>());
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
    public async Task Execute_WithoutSnapshot_PersistsOriginalAndCurrentPricing()
    {
        var context = CreateContext(
            [ProposalItem(Item())],
            TechnicalEstimate(100m, 200m, 300m));

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(200m, result.Pricing!.OriginalGrandTotal);
        Assert.Equal(200m, result.Pricing.CurrentGrandTotal);
        Assert.Equal(0m, result.Pricing.DeltaGrandTotal);
        context.Requirements.Received(1).AddPricingSnapshot(
            Arg.Is<RequirementPricingSnapshot>(snapshot =>
                snapshot.RequirementId == context.Requirement.Id
                && snapshot.TechnicalProposalId == context.Proposal.Id
                && snapshot.OriginalGrandTotal == 200m
                && snapshot.CurrentGrandTotal == 200m
                && snapshot.Items.Count == 1));
    }

    [Fact]
    public async Task Execute_WithExistingSnapshot_ReturnsPersistedPricingWithoutEstimator()
    {
        var proposalItem = ProposalItem(Item());
        var context = CreateContext(
            [proposalItem],
            TechnicalEstimate(100m, 200m, 300m));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(proposalItem, 100m, 100m)]);
        context.Requirements.GetCurrentPricingSnapshotAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Pricing!.OriginalGrandTotal);
        Assert.Equal(100m, result.Pricing.CurrentGrandTotal);
        Assert.Equal(0m, result.Pricing.DeltaGrandTotal);
        await context.TechnicalEstimator.DidNotReceive().EstimateAsync(
            Arg.Any<HistoricalCandidateQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithSnapshotRevisionMismatch_RecalculatesAndReplacesSnapshot()
    {
        var proposalItem = ProposalItem(Item(quantity: 2));
        var context = CreateContext(
            [proposalItem],
            TechnicalEstimate(150m, 250m, 350m));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(proposalItem, 100m, 100m)]);
        context.Proposal.MarkCommerciallyChanged();
        context.Requirements.GetCurrentPricingSnapshotAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);
        context.Requirements.FindCurrentPricingSnapshotAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.Pricing!.OriginalGrandTotal);
        Assert.Equal(500m, result.Pricing.CurrentGrandTotal);
        Assert.Equal(0m, result.Pricing.DeltaGrandTotal);
        context.Requirements.Received(1).ReplacePricingSnapshot(
            snapshot,
            Arg.Is<RequirementPricingSnapshot>(replacement =>
                replacement.TechnicalProposalId == context.Proposal.Id
                && replacement.TechnicalProposalCommercialRevision
                    == context.Proposal.CommercialRevision
                && replacement.CurrentGrandTotal == 500m));
        await context.TechnicalEstimator.Received().EstimateAsync(
            Arg.Any<HistoricalCandidateQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WhenRegisteredPricingIsCancelled_DoesNotPersistSnapshot()
    {
        var cancellation = new CancellationTokenSource();
        var context = CreateContext(
            [ProposalItem(Item())],
            TechnicalEstimate(100m, 200m, 300m));
        context.CancellationRegistry.Register(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(cancellation.Token);
        context.TechnicalEstimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<HistoricalTechnicalPriceEstimate>(
                    cancellation.Token);
            });

        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(
                context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            PriceRequirementTechnicalProposalFailure.Cancelled,
            result.Failure);
        context.Requirements.DidNotReceive().AddPricingSnapshot(
            Arg.Any<RequirementPricingSnapshot>());
    }

    [Fact]
    public async Task RepriceItem_WithCommercialChange_SynchronizesProposalAndSnapshotRevision()
    {
        var item = ProposalItem(Item(reference: "PV-06"));
        var context = CreateContext(
            [item],
            TechnicalEstimate(200m, 200m, 200m));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(item, 100m, 100m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);
        var initialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                SystemLsa9060Id,
                GlassTemp8Id,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(initialRevision + 1, context.Proposal.CommercialRevision);
        Assert.Equal(
            context.Proposal.CommercialRevision,
            snapshot.TechnicalProposalCommercialRevision);
        Assert.Equal(200m, snapshot.CurrentGrandTotal);
    }

    [Fact]
    public async Task RepriceItem_WithoutEffectiveCommercialChange_KeepsRevision()
    {
        var item = ProposalItem(Item(reference: "PV-06"));
        var context = CreateContext(
            [item],
            TechnicalEstimate(200m, 200m, 200m));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(item, 100m, 100m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);
        var initialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                item.SelectedSystemId,
                item.SelectedGlassTypeId,
                item.SelectedFinishTypeId,
                item.EffectiveQuantity,
                item.EffectiveWidthMillimeters,
                item.EffectiveHeightMillimeters),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(initialRevision, context.Proposal.CommercialRevision);
        Assert.Equal(initialRevision, snapshot.TechnicalProposalCommercialRevision);
    }

    [Fact]
    public async Task RepriceItem_UpdatesSelectedAndOnlyPricesTargetItemIncrementally()
    {
        var first = ProposalItem(Item(reference: "PV-06"));
        var second = ProposalItem(Item(reference: "V-01"));
        var context = CreateContext(
            [first, second],
            TechnicalEstimate(400m, 400m, 400m));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(first, 100m, 100m), (second, 200m, 200m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var result = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                first.Id,
                null,
                GlassTemp8Id,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(GlassTemp8Id, first.SelectedGlassTypeId);
        Assert.Equal(100m, result.Pricing!.Item.OriginalLine!.Expected);
        Assert.Equal(400m, result.Pricing.Item.CurrentLine!.Expected);
        Assert.Equal(300m, result.Pricing.Item.DeltaLine!.Expected);
        Assert.Equal(300m, result.Pricing.OriginalGrandTotal);
        Assert.Equal(600m, result.Pricing.CurrentGrandTotal);
        Assert.Equal(300m, result.Pricing.DeltaGrandTotal);
        await context.TechnicalEstimator.Received(1).EstimateAsync(
            Arg.Any<HistoricalCandidateQuery>(),
            Arg.Any<CancellationToken>());
        await context.Requirements.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepriceItem_AccumulatesMultipleItemRepricesFromSnapshotItems()
    {
        var first = ProposalItem(Item(reference: "PV-06"));
        var second = ProposalItem(Item(reference: "V-01"));
        var context = CreateContext(
            [first, second],
            TechnicalEstimate(50m, 50m, 50m));
        context.TechnicalEstimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(
                TechnicalEstimate(50m, 50m, 50m),
                TechnicalEstimate(80m, 80m, 80m));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(first, 100m, 100m), (second, 200m, 200m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var firstResult = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                first.Id,
                null,
                GlassTemp8Id,
                null),
            TestContext.Current.CancellationToken);
        var secondResult = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                second.Id,
                null,
                GlassTemp8Id,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);
        var firstSnapshot = snapshot.Items.Single(item =>
            item.TechnicalProposalItemId == first.Id);
        var secondSnapshot = snapshot.Items.Single(item =>
            item.TechnicalProposalItemId == second.Id);
        Assert.Equal(50m, firstSnapshot.CurrentLineExpected);
        Assert.Equal(80m, secondSnapshot.CurrentLineExpected);
        Assert.Equal(300m, snapshot.OriginalGrandTotal);
        Assert.Equal(130m, snapshot.CurrentGrandTotal);
        Assert.Equal(-170m, snapshot.DeltaGrandTotal);
        Assert.Equal(130m, firstSnapshot.CurrentLineExpected
            + secondSnapshot.CurrentLineExpected);
        Assert.Equal(-170m, firstSnapshot.DeltaLineExpected
            + secondSnapshot.DeltaLineExpected);
        Assert.Equal(130m, secondResult.Pricing!.CurrentGrandTotal);
        Assert.Equal(-170m, secondResult.Pricing.DeltaGrandTotal);
    }

    [Fact]
    public async Task Execute_AfterMultipleRepricesReturnsConsistentSnapshotTotals()
    {
        var first = ProposalItem(Item(reference: "PV-06"));
        var second = ProposalItem(Item(reference: "V-01"));
        var context = CreateContext(
            [first, second],
            TechnicalEstimate(50m, 50m, 50m));
        context.TechnicalEstimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(
                TechnicalEstimate(50m, 50m, 50m),
                TechnicalEstimate(80m, 80m, 80m));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(first, 100m, 100m), (second, 200m, 200m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);
        context.Requirements.GetCurrentPricingSnapshotAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                first.Id,
                null,
                GlassTemp8Id,
                null),
            TestContext.Current.CancellationToken);
        await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                second.Id,
                null,
                GlassTemp8Id,
                null),
            TestContext.Current.CancellationToken);
        var result = await context.Service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var pricing = result.Pricing!;
        Assert.Equal(50m, pricing.Items.Single(item =>
            item.ProposalItemId == first.Id).CurrentLine!.Expected);
        Assert.Equal(80m, pricing.Items.Single(item =>
            item.ProposalItemId == second.Id).CurrentLine!.Expected);
        Assert.Equal(130m, pricing.EstimatedSubtotal.Expected);
        Assert.Equal(130m, pricing.CurrentGrandTotal);
        Assert.Equal(-170m, pricing.DeltaGrandTotal);
        Assert.Equal(pricing.CurrentGrandTotal, pricing.EstimatedSubtotal.Expected);
    }

    [Fact]
    public async Task RepriceItem_MultipleRepricesSameItemKeepOriginalImmutable()
    {
        var item = ProposalItem(Item(reference: "PV-06"));
        var context = CreateContext(
            [item],
            TechnicalEstimate(80m, 80m, 80m));
        context.TechnicalEstimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(
                TechnicalEstimate(80m, 80m, 80m),
                TechnicalEstimate(60m, 60m, 60m));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(item, 100m, 100m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                null,
                GlassTemp8Id,
                null),
            TestContext.Current.CancellationToken);
        var result = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                null,
                GlassTemp6Id,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Pricing!.Item.OriginalLine!.Expected);
        Assert.Equal(60m, result.Pricing.Item.CurrentLine!.Expected);
        Assert.Equal(-40m, result.Pricing.Item.DeltaLine!.Expected);
        Assert.Equal(100m, snapshot.OriginalGrandTotal);
        Assert.Equal(60m, snapshot.CurrentGrandTotal);
        Assert.Equal(-40m, snapshot.DeltaGrandTotal);
    }
    [Fact]
    public async Task RepriceItem_WithChangedSystemAndLastValidPrice_PreservesCurrentPrice()
    {
        HistoricalCandidateQuery? captured = null;
        var item = ProposalItem(Item());
        var context = CreateContext(
            [item],
            TechnicalEstimate(
                null,
                null,
                null,
                ["SYSTEM_MATCH_REQUIRED_NO_COMPARABLES"]),
            query => captured = query);
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(item, 100m, 100m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var result = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                SystemLsa9060Id,
                null,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(captured!.RequireSystemMatchedComparable);
        Assert.Equal("PRICEABLE", result.Pricing!.Item.Status);
        Assert.Equal("LAST_VALID_CURRENT", result.Pricing.Item.PriceSource);
        Assert.Equal("NO_ESTIMATE", result.Pricing.Item.RepriceAttemptState);
        Assert.Equal(
            "SYSTEM_MATCH_REQUIRED_NO_COMPARABLES",
            result.Pricing.Item.RepriceAttemptReason);
        Assert.Contains(
            "SYSTEM_MATCH_REQUIRED_NO_COMPARABLES",
            result.Pricing.Item.MissingData);
        Assert.Contains(
            "LAST_VALID_PRICE_PRESERVED",
            result.Pricing.Item.MissingData);
        Assert.Empty(result.Pricing.Item.Comparables);
        Assert.Equal(100m, result.Pricing.Item.CurrentLine!.Expected);
        Assert.Equal(100m, result.Pricing.Item.OriginalLine!.Expected);
        Assert.Equal(100m, result.Pricing.CurrentGrandTotal);
        Assert.Equal(0m, result.Pricing.DeltaGrandTotal);
    }

    [Fact]
    public async Task RepriceItem_AfterSuccessfulThenFailedReprice_KeepsLastSuccessfulCurrent()
    {
        var item = ProposalItem(Item());
        var context = CreateContext(
            [item],
            TechnicalEstimate(
                null,
                null,
                null,
                ["SYSTEM_MATCH_REQUIRED_NO_COMPARABLES"]));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(item, 100m, 200m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var result = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                SystemLsa9060Id,
                null,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("PRICEABLE", result.Pricing!.Item.Status);
        Assert.Equal("LAST_VALID_CURRENT", result.Pricing.Item.PriceSource);
        Assert.Equal(200m, result.Pricing.Item.CurrentLine!.Expected);
        Assert.Equal(100m, result.Pricing.Item.DeltaLine!.Expected);
        Assert.Equal(200m, result.Pricing.CurrentGrandTotal);
        Assert.Equal(100m, result.Pricing.DeltaGrandTotal);
    }

    [Fact]
    public async Task RepriceItem_WithNeverPricedItemAndNoEstimate_RemainsNotEstimated()
    {
        var item = ProposalItem(Item());
        var context = CreateContext(
            [item],
            TechnicalEstimate(
                null,
                null,
                null,
                ["SYSTEM_MATCH_REQUIRED_NO_COMPARABLES"]));
        var snapshot = RequirementPricingSnapshot.Create(
            context.Requirement.Id,
            context.Proposal.Id,
            context.Proposal.CommercialRevision,
            "COP",
            "PUBLIC_QUOTED_ITEM_PRICES",
            null,
            null,
            At);
        snapshot.AddItem(RequirementPricingItemSnapshot.Create(
            snapshot.Id,
            item.Id,
            item.SuggestedSystemId,
            item.SuggestedGlassTypeId,
            item.SuggestedFinishTypeId,
            "NO_ESTIMATE",
            null,
            null,
            null,
            null,
            null,
            null,
            At));
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var result = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                SystemLsa9060Id,
                null,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("NO_ESTIMATE", result.Pricing!.Item.Status);
        Assert.Null(result.Pricing.Item.PriceSource);
        Assert.Equal("NO_ESTIMATE", result.Pricing.Item.RepriceAttemptState);
        Assert.Null(result.Pricing.Item.CurrentLine!.Expected);
        Assert.Null(result.Pricing.CurrentGrandTotal);
    }

    [Fact]
    public async Task RepriceItem_SuccessAfterFailedAttempt_UpdatesLastValidCurrent()
    {
        var item = ProposalItem(Item());
        var context = CreateContext(
            [item],
            TechnicalEstimate(
                null,
                null,
                null,
                ["SYSTEM_MATCH_REQUIRED_NO_COMPARABLES"]));
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(item, 100m, 200m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var failed = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                SystemLsa9060Id,
                null,
                null),
            TestContext.Current.CancellationToken);
        context.TechnicalEstimator.EstimateAsync(
                Arg.Any<HistoricalCandidateQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(TechnicalEstimate(300m, 400m, 500m));

        var succeeded = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                null,
                GlassTemp8Id,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(failed.IsSuccess);
        Assert.Equal(200m, failed.Pricing!.Item.CurrentLine!.Expected);
        Assert.True(succeeded.IsSuccess);
        Assert.Equal("CURRENT_ESTIMATE", succeeded.Pricing!.Item.PriceSource);
        Assert.Equal("PRICEABLE", succeeded.Pricing.Item.RepriceAttemptState);
        Assert.Equal(400m, succeeded.Pricing.Item.CurrentLine!.Expected);
        Assert.Equal(300m, succeeded.Pricing.Item.DeltaLine!.Expected);
        Assert.Equal(400m, succeeded.Pricing.CurrentGrandTotal);
        Assert.Equal(300m, succeeded.Pricing.DeltaGrandTotal);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RepriceItem_WithoutSystemChange_PreservesLegacyFallbackIntent(
        bool changeGlass,
        bool changeFinish)
    {
        HistoricalCandidateQuery? captured = null;
        var item = ProposalItem(Item());
        var context = CreateContext(
            [item],
            TechnicalEstimate(100m, 200m, 300m),
            query => captured = query);
        var snapshot = Snapshot(
            context.Requirement.Id,
            context.Proposal.Id,
            [(item, 100m, 100m)]);
        context.Requirements.FindCurrentTechnicalProposalForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Proposal);
        context.Requirements.FindCurrentPricingSnapshotForUpdateAsync(
                context.Requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var result = await context.Service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                item.Id,
                SystemNapolesId,
                changeGlass ? GlassTemp8Id : null,
                changeFinish ? FinishWhiteId : null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(captured!.RequireSystemMatchedComparable);
        Assert.Equal("PRICEABLE", result.Pricing!.Item.Status);
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
        Action<HistoricalCandidateQuery>? captureQuery = null,
        bool confirmProposal = true)
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
        var cancellationRegistry = Substitute.For<IOperationCancellationRegistry>();

        var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
        var client = Client.Create(ClientType.Company, "Client", null, null, null, null, null, null, null, UserId, At);
        var project = ProjectEntity.Create(client.Id, "P-001", "Project", null, null, UserId, At);
        var preQuote = PreQuote.Create(project.Id, UserId, "PC-2020-0001", null, At);
        var requirement = Requirement.Create(preQuote.Id, UserId, RequirementCommercialLine.Essential, At);
        var proposal = RequirementTechnicalProposal.Create(requirement.Id, Guid.NewGuid(), Guid.NewGuid(), false, At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        foreach (var item in items)
        {
            SetPrivateProperty(item, "TechnicalProposalId", proposal.Id);
            proposal.AddItem(item);
        }
        if (confirmProposal)
        {
            proposal.ConfirmCommercialSelection(UserId, At.AddMinutes(2));
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        requirements.FindByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(requirement);
        requirements.BeginPricingUpdateTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new NoopRequirementPersistenceTransaction());
        requirements.GetCurrentTechnicalProposalAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(proposal);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>()).Returns(new PreQuoteDetails(
            preQuote.Id, preQuote.ProjectId, 0, preQuote.CreatedAtUtc, preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        systems.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns([SystemNapoles(), SystemLsa9060()]);
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>())
            .Returns([SystemNapoles(), SystemLsa9060()]);
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns([GlassTemp6(), GlassTemp8()]);
        finishes.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns([FinishBlack(), FinishWhite()]);
        technicalEstimator.EstimateAsync(Arg.Do<HistoricalCandidateQuery>(query => captureQuery?.Invoke(query)), Arg.Any<CancellationToken>())
            .Returns(technicalEstimate);
        cancellationRegistry.Register(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<CancellationToken>(1));

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
            commercial,
            cancellationRegistry);

        return new Context(service, requirements, requirement, proposal,
            technicalEstimator, cancellationRegistry);
    }

    private static RequirementPricingSnapshot Snapshot(
        Guid requirementId,
        Guid proposalId,
        IReadOnlyList<(RequirementTechnicalProposalItem Item, decimal Original,
            decimal Current)> items)
    {
        var snapshot = RequirementPricingSnapshot.Create(
            requirementId,
            proposalId,
            1,
            "COP",
            "PUBLIC_QUOTED_ITEM_PRICES",
            items.Sum(item => item.Original),
            items.Sum(item => item.Current),
            At);
        foreach (var value in items)
        {
            var item = RequirementPricingItemSnapshot.Create(
                snapshot.Id,
                value.Item.Id,
                value.Item.SuggestedSystemId,
                value.Item.SuggestedGlassTypeId,
                value.Item.SuggestedFinishTypeId,
                "PRICEABLE",
                value.Original,
                value.Original,
                value.Original,
                value.Original,
                value.Original,
                value.Original,
                At);
            item.UpdateCurrent(
                value.Item.SuggestedSystemId,
                value.Item.SuggestedGlassTypeId,
                value.Item.SuggestedFinishTypeId,
                "PRICEABLE",
                value.Current,
                value.Current,
                value.Current,
                value.Current,
                value.Current,
                value.Current,
                At);
            snapshot.AddItem(item);
        }

        return snapshot;
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
            "AVAILABLE",
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

    private static ProductSystemCatalogReadModel SystemLsa9060() =>
        new(
            SystemLsa9060Id,
            "LSA_9060",
            "PUERTA CORREDIZA LINEA PREMIUM LSA 9060",
            "PUERTA CORREDIZA LINEA PREMIUM LSA 9060",
            "LSA 9060",
            "SLIDING_DOOR",
            "LSA 9060",
            "SERIE 90",
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

    private static GlassTypeCatalogReadModel GlassTemp8() =>
        new(
            GlassTemp8Id,
            "TEMP_8",
            "COMPOSICION MONOLITICO TEMPLADO 8 MM INC",
            null,
            true,
            null,
            Family: "MONOLITHIC",
            Composition: "MONOLITICO",
            Treatment: "TEMPLADO",
            OuterThicknessMm: 8m);

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

    private static FinishTypeCatalogReadModel FinishWhite() =>
        new(
            FinishWhiteId,
            "WHITE_MATTE",
            "ALUCOLOR POLIESTER BLANCO MATE",
            "PAINTED",
            "BLANCO MATE",
            "MATTE",
            "PAINTED",
            "PW01",
            "ALUMINUM",
            true,
            false,
            true);

    private static HistoricalTechnicalPriceEstimate TechnicalEstimate(
        decimal? minimum,
        decimal? expected,
        decimal? maximum,
        IReadOnlyList<string>? missingData = null) =>
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
            missingData ?? [],
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

    private sealed class NoopRequirementPersistenceTransaction
        : IRequirementPersistenceTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed record Context(
        PriceRequirementTechnicalProposalService Service,
        IRequirementRepository Requirements,
        Requirement Requirement,
        RequirementTechnicalProposal Proposal,
        IHistoricalTechnicalPriceEstimator TechnicalEstimator,
        IOperationCancellationRegistry CancellationRegistry);
}
