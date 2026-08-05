using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class StructuredExtractionTechnicalClassificationTests
{
    private static readonly DateTimeOffset At =
        new(2026, 8, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithTechnicalClassification_NormalizesCodesAndPreservesEvidence()
    {
        var extraction = StructuredDocumentExtraction.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            StructuredExtractionStatus.RequiresReview,
            "Project",
            "Client",
            "Bogota",
            1,
            0,
            1,
            1,
            "rule_based_v2",
            10,
            [
                new StructuredItemInput(
                    1,
                    "W-01",
                    "Window",
                    StructuredElementType.Window,
                    "1000 x 1000 mm",
                    1000,
                    1000,
                    1,
                    true,
                    null,
                    null,
                    new StructuredItemTechnicalClassificationInput(
                        "k40",
                        "VENECIA SERIE 40",
                        TechnicalClassificationSource.Alias,
                        0.95m,
                        "marco_47",
                        "SG0047",
                        TechnicalClassificationSource.Alias,
                        0.95m,
                        "black_matte",
                        "NEGRO MATE",
                        TechnicalClassificationSource.Alias,
                        0.95m,
                        true,
                        ["system_not_currently_priceable"]))
            ],
            [],
            [],
            [],
            [],
            At);

        var technical = Assert.Single(extraction.Items)
            .TechnicalClassification;
        Assert.NotNull(technical);
        Assert.Equal("K40", technical!.SystemCode);
        Assert.Equal("MARCO_47", technical.FrameCode);
        Assert.Equal("BLACK_MATTE", technical.FinishCode);
        Assert.Equal(["SYSTEM_NOT_CURRENTLY_PRICEABLE"],
            technical.ReviewReasons);
    }

    [Fact]
    public void Create_WithInvalidConfidence_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => StructuredDocumentExtraction.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            StructuredExtractionStatus.Completed,
            "Project",
            "Client",
            "Bogota",
            1,
            0,
            0,
            1,
            "rule_based_v2",
            10,
            [
                new StructuredItemInput(
                    1,
                    "W-01",
                    "Window",
                    StructuredElementType.Window,
                    "1000 x 1000 mm",
                    1000,
                    1000,
                    1,
                    false,
                    null,
                    null,
                    new StructuredItemTechnicalClassificationInput(
                        "K40",
                        null,
                        TechnicalClassificationSource.Explicit,
                        1.01m,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        false,
                        []))
            ],
            [],
            [],
            [],
            [],
            At));
    }
}
