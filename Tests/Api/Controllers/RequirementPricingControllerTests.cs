using System.Reflection;
using Api.Controllers;
using Application.Common.Abstractions.HistoricalPricing;
using Contracts.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class RequirementPricingControllerTests
{
    [Fact]
    public void RepriceItemResponse_MapsCompletePriceRanges()
    {
        var item = new TechnicalProposalPricingItemReadModel(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "element-1",
            1,
            "A",
            "Ventana",
            "PRICEABLE",
            "SELECTED",
            1m,
            2m,
            new TechnicalProposalPricingMoneyRange(9m, 10m, 11m),
            new TechnicalProposalPricingMoneyRange(90m, 100m, 110m),
            0.9m,
            "HIGH",
            false,
            [],
            [],
            [],
            [],
            new TechnicalProposalPricingMoneyRange(8m, 9m, 10m),
            new TechnicalProposalPricingMoneyRange(11m, 12m, 13m),
            new TechnicalProposalPricingMoneyRange(3m, 3m, 3m),
            new TechnicalProposalPricingMoneyRange(80m, 90m, 100m),
            new TechnicalProposalPricingMoneyRange(110m, 120m, 130m),
            new TechnicalProposalPricingMoneyRange(30m, 30m, 30m),
            "HISTORICAL_COMPARABLES",
            "PRICEABLE",
            null);
        var pricing = new RepriceRequirementTechnicalProposalItemReadModel(
            Guid.NewGuid(),
            Guid.NewGuid(),
            item.ProposalItemId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            item,
            90m,
            120m,
            30m);
        var mapper = typeof(RequirementPricingController)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.ReturnType ==
                typeof(RepriceRequirementPricingItemResponse));

        var response = Assert.IsType<RepriceRequirementPricingItemResponse>(
            mapper.Invoke(null, [pricing]));

        Assert.Equal(9m, response.Pricing.OriginalUnitPrice);
        Assert.Equal(12m, response.Pricing.CurrentUnitPrice);
        Assert.Equal(3m, response.Pricing.DeltaUnitPrice);
        Assert.Equal(90m, response.Pricing.OriginalLineTotal);
        Assert.Equal(120m, response.Pricing.CurrentLineTotal);
        Assert.Equal(30m, response.Pricing.DeltaLineTotal);
        AssertRange(response.Pricing.OriginalUnit, 8m, 9m, 10m);
        AssertRange(response.Pricing.CurrentUnit, 11m, 12m, 13m);
        AssertRange(response.Pricing.DeltaUnit, 3m, 3m, 3m);
        AssertRange(response.Pricing.OriginalLine, 80m, 90m, 100m);
        AssertRange(response.Pricing.CurrentLine, 110m, 120m, 130m);
        AssertRange(response.Pricing.DeltaLine, 30m, 30m, 30m);
    }

    private static void AssertRange(
        RequirementPricingRangeResponse? range,
        decimal minimum,
        decimal expected,
        decimal maximum)
    {
        Assert.NotNull(range);
        Assert.Equal(minimum, range!.Minimum);
        Assert.Equal(expected, range.Expected);
        Assert.Equal(maximum, range.Maximum);
    }
}