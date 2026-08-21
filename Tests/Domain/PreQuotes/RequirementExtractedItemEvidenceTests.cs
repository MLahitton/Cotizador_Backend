using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class RequirementExtractedItemEvidenceTests
{
    private static readonly Guid ItemId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_WithNativePositivePage_IsValid()
    {
        var evidence = RequirementExtractedItemEvidence.Create(
            ItemId,
            1,
            EvidenceSourceType.Native,
            "texto",
            null,
            null,
            "source-1",
            0.9m,
            RequirementExtractionValueStatus.Explicit,
            At);

        Assert.Equal(1, evidence.PageNumber);
        Assert.Equal(EvidenceSourceType.Native, evidence.SourceType);
    }

    [Fact]
    public void Create_WithOcrPositivePage_IsValid()
    {
        var evidence = RequirementExtractedItemEvidence.Create(
            ItemId,
            2,
            EvidenceSourceType.Ocr,
            "texto",
            null,
            null,
            "source-1",
            0.9m,
            RequirementExtractionValueStatus.Explicit,
            At);

        Assert.Equal(2, evidence.PageNumber);
        Assert.Equal(EvidenceSourceType.Ocr, evidence.SourceType);
    }

    [Fact]
    public void Create_WithXlsxSheetAndCellRange_IsValid()
    {
        var evidence = RequirementExtractedItemEvidence.Create(
            ItemId,
            null,
            EvidenceSourceType.Xlsx,
            "texto",
            "Cotizacion",
            "A12:H12",
            "source-1",
            0.9m,
            RequirementExtractionValueStatus.Explicit,
            At);

        Assert.Null(evidence.PageNumber);
        Assert.Equal("Cotizacion", evidence.SheetName);
        Assert.Equal("A12:H12", evidence.CellRange);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Create_WithPdfWithoutPositivePage_IsRejected(int? pageNumber)
    {
        Assert.Throws<ArgumentException>(() =>
            RequirementExtractedItemEvidence.Create(
                ItemId,
                pageNumber,
                EvidenceSourceType.Native,
                "texto",
                null,
                null,
                "source-1",
                0.9m,
                RequirementExtractionValueStatus.Explicit,
                At));
    }

    [Fact]
    public void Create_WithXlsxWithoutLocator_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            RequirementExtractedItemEvidence.Create(
                ItemId,
                null,
                EvidenceSourceType.Xlsx,
                "texto",
                null,
                null,
                "source-1",
                0.9m,
                RequirementExtractionValueStatus.Explicit,
                At));
    }
}
