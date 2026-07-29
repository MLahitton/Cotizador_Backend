using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class StructuredDocumentExtractionTests
{
    private static readonly Guid ResultId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset CreatedAt =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("items")]
    [InlineData("requirements")]
    [InlineData("references")]
    [InlineData("issues")]
    [InlineData("conflicts")]
    public void Create_WithConsecutiveSequences_Succeeds(
        string collection)
    {
        var extraction = Create(collection, [1, 2]);

        Assert.NotNull(extraction);
    }

    [Theory]
    [InlineData("items", 1, 1)]
    [InlineData("items", 1, 3)]
    [InlineData("items", 2, 1)]
    [InlineData("requirements", 1, 1)]
    [InlineData("requirements", 1, 3)]
    [InlineData("requirements", 2, 1)]
    [InlineData("references", 1, 1)]
    [InlineData("references", 1, 3)]
    [InlineData("references", 2, 1)]
    [InlineData("issues", 1, 1)]
    [InlineData("issues", 1, 3)]
    [InlineData("issues", 2, 1)]
    [InlineData("conflicts", 1, 1)]
    [InlineData("conflicts", 1, 3)]
    [InlineData("conflicts", 2, 1)]
    public void Create_WithInvalidSequences_Throws(
        string collection,
        int first,
        int second)
    {
        Assert.Throws<ArgumentException>(
            () => Create(collection, [first, second]));
    }

    [Theory]
    [InlineData("items")]
    [InlineData("requirements")]
    [InlineData("references")]
    [InlineData("issues")]
    [InlineData("conflicts")]
    public void Create_WithEmptyCollection_Succeeds(string collection)
    {
        var extraction = Create(collection, []);

        Assert.NotNull(extraction);
    }

    [Fact]
    public void HistoricalV1Result_DoesNotRequireStructuredSnapshot()
    {
        var result = DocumentExtractionResult.Create(
            Guid.NewGuid(),
            "1.0",
            PdfClassification.PdfText,
            false,
            1,
            "pymupdf",
            1,
            """{"schemaVersion":"1.0"}""",
            CreatedAt);

        Assert.Equal("1.0", result.SchemaVersion);
        Assert.Null(result.StructuredExtraction);
        Assert.Equal(
            """{"schemaVersion":"1.0"}""",
            result.PayloadJson);
    }

    private static StructuredDocumentExtraction Create(
        string collection,
        int[] sequences)
    {
        var items = collection == "items"
            ? sequences.Select(Item).ToArray()
            : [];
        var requirements = collection == "requirements"
            ? sequences.Select(x => new StructuredRequirementInput(
                x,
                RequirementCategory.GlassSpecification,
                "Glass")).ToArray()
            : [];
        var references = collection == "references"
            ? sequences.Select(x => new StructuredDocumentReferenceInput(
                x,
                null,
                "Drawing",
                null,
                1)).ToArray()
            : [];
        var issues = collection == "issues"
            ? sequences.Select(x => new StructuredIssueInput(
                x,
                StructuredIssueCode.OcrReviewRequired,
                "Review",
                null,
                [])).ToArray()
            : [];
        var conflicts = collection == "conflicts"
            ? sequences.Select(x => new StructuredConflictInput(
                x,
                StructuredConflictCode.DuplicateItemReference,
                "Conflict",
                [],
                [])).ToArray()
            : [];
        return StructuredDocumentExtraction.Create(
            ResultId,
            StructuredExtractionStatus.RequiresReview,
            null,
            null,
            null,
            items.Length,
            references.Length,
            0,
            items.Sum(x => x.Quantity ?? 0),
            "rule_based_v1",
            1,
            items,
            requirements,
            references,
            issues,
            conflicts,
            CreatedAt);
    }

    private static StructuredItemInput Item(int sequence) => new(
        sequence,
        "W-01",
        "Window",
        StructuredElementType.Window,
        null,
        1000,
        1000,
        1,
        false);
}
