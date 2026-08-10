using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class DocumentExtractionResultTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(DocumentClassification.PdfText, false)]
    [InlineData(DocumentClassification.PdfScanned, true)]
    [InlineData(DocumentClassification.PdfMixed, true)]
    public void Create_PdfClassifications_WithPageCountOne_AreValid(
        DocumentClassification classification,
        bool requiresOcr)
    {
        var result = DocumentExtractionResult.Create(
            Guid.NewGuid(),
            "3.0",
            classification,
            requiresOcr,
            1,
            "pymupdf",
            1,
            "{\"schemaVersion\":\"3.0\"}",
            CreatedAt);

        Assert.NotNull(result);
        Assert.Equal(classification, result.Classification);
    }

    [Theory]
    [InlineData(DocumentClassification.PdfText)]
    [InlineData(DocumentClassification.PdfScanned)]
    [InlineData(DocumentClassification.PdfMixed)]
    public void Create_Pdf_WithPageCountZero_Throws(DocumentClassification classification)
    {
        var expectedRequiresOcr = classification is
            DocumentClassification.PdfScanned or
            DocumentClassification.PdfMixed;

        Assert.Throws<ArgumentException>(
            () => DocumentExtractionResult.Create(
                Guid.NewGuid(),
                "3.0",
                classification,
                expectedRequiresOcr,
                0,
                "pymupdf",
                1,
                "{\"schemaVersion\":\"3.0\"}",
                CreatedAt));
    }

    [Fact]
    public void Create_Xlsx_WithPageCountZeroRequiresOcrFalseAndOpenpyxl_IsValid()
    {
        var result = DocumentExtractionResult.Create(
            Guid.NewGuid(),
            "3.0",
            DocumentClassification.Xlsx,
            false,
            0,
            "openpyxl",
            1,
            "{\"schemaVersion\":\"3.0\"}",
            CreatedAt);

        Assert.NotNull(result);
        Assert.Equal(DocumentClassification.Xlsx, result.Classification);
    }

    [Fact]
    public void Create_Xlsx_WithPageCountOne_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => DocumentExtractionResult.Create(
                Guid.NewGuid(),
                "3.0",
                DocumentClassification.Xlsx,
                false,
                1,
                "openpyxl",
                1,
                "{\"schemaVersion\":\"3.0\"}",
                CreatedAt));
    }

    [Fact]
    public void Create_Xlsx_WithRequiresOcrTrue_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => DocumentExtractionResult.Create(
                Guid.NewGuid(),
                "3.0",
                DocumentClassification.Xlsx,
                true,
                0,
                "openpyxl",
                1,
                "{\"schemaVersion\":\"3.0\"}",
                CreatedAt));
    }

    [Fact]
    public void Create_Xlsx_WithPdfMethod_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => DocumentExtractionResult.Create(
                Guid.NewGuid(),
                "3.0",
                DocumentClassification.Xlsx,
                false,
                0,
                "pymupdf",
                1,
                "{\"schemaVersion\":\"3.0\"}",
                CreatedAt));
    }
}
