using Api.Controllers;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class PreQuoteDocumentResponseMapperTests
{
    [Theory]
    [InlineData(DocumentClassification.PdfText, "PDF_TEXT")]
    [InlineData(DocumentClassification.PdfScanned, "PDF_SCANNED")]
    [InlineData(DocumentClassification.PdfMixed, "PDF_MIXED")]
    [InlineData(DocumentClassification.Xlsx, "XLSX")]
    public void Map_ResultMetadata_MapsDocumentClassification(
        DocumentClassification classification,
        string expected)
    {
        var attempt = new DocumentProcessingAttemptSummaryReadModel(
            Guid.NewGuid(),
            DocumentProcessingState.Finished,
            DocumentProcessingOutcome.Completed,
            null,
            new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            new(2026, 8, 9, 0, 0, 0, TimeSpan.Zero),
            new(2026, 8, 9, 0, 0, 1, TimeSpan.Zero),
            new DocumentExtractionResultMetadataReadModel(
                "3.0",
                classification,
                classification is DocumentClassification.PdfScanned or
                    DocumentClassification.PdfMixed,
                1,
                classification == DocumentClassification.Xlsx
                    ? "openpyxl"
                    : "pymupdf",
                100));

        var response = PreQuoteDocumentResponseMapper.Map(attempt);

        Assert.NotNull(response);
        Assert.NotNull(response.ResultMetadata);
        Assert.Equal(expected, response.ResultMetadata!.Classification);
    }
}
