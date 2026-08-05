using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class PreQuoteDraftTechnicalSnapshotTests
{
    private static readonly Guid PreQuoteId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocumentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ExtractionId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset At =
        new(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithNotPriceableTechnicalSnapshot_ExcludesItemFromEconomicSummary()
    {
        var draft = PreQuoteDraft.Create(
            PreQuoteId,
            DocumentId,
            ExtractionId,
            "Project",
            "Client",
            "Bogota",
            UserId,
            At,
            [
                new PreQuoteDraftItemSource(
                    Guid.NewGuid(),
                    1,
                    "B-01",
                    "Baranda",
                    StructuredElementType.Railing,
                    "1000 x 900 mm",
                    1000,
                    900,
                    1,
                    null,
                    null,
                    new PreQuoteDraftItemTechnicalSnapshotSource(
                        Guid.NewGuid(),
                        "BARANDA",
                        null,
                        TechnicalClassificationSource.Inferred,
                        1m,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        true,
                        ["SYSTEM_NOT_CURRENTLY_PRICEABLE"]))
            ],
            [],
            [],
            [],
            []);

        var item = Assert.Single(draft.Items);
        Assert.Equal(
            PreQuoteDraftValuationStatus.NotPriceable,
            item.ValuationStatus);
        Assert.Null(item.ValuationSnapshot);
        Assert.Equal(1, draft.EconomicSummary.NotPriceableItemCount);
        Assert.True(draft.EconomicSummary.HasNotPriceableItems);
        Assert.Equal(0, draft.EconomicSummary.ValuedItemCount);
        Assert.Equal(0, draft.EconomicSummary.PendingValuationItemCount);
        Assert.False(draft.EconomicSummary.IsEconomicallyComplete);
    }
}
