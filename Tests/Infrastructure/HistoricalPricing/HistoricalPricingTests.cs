using System.IO.Compression;
using System.Text;
using Application.Common.Abstractions.HistoricalPricing;
using Infrastructure.HistoricalPricing;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.HistoricalPricing;

public sealed class HistoricalPricingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "historical-pricing-" + Guid.NewGuid().ToString("N"));
    public HistoricalPricingTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Inspect_DetectsOoxmlOleCorruptAndMissingQuotationSheet()
    {
        var reader = new HistoricalWorkbookReader();
        var valid = CreateWorkbook("valid.xlsx", true);
        var missing = CreateWorkbook("missing.xlsx", false);
        var ole = Path.Combine(_directory, "legacy.xlsx");
        File.WriteAllBytes(ole, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);
        var corrupt = Path.Combine(_directory, "corrupt.xlsx");
        File.WriteAllText(corrupt, "not-a-workbook");
        var inspection = reader.Inspect(valid, "valid.xlsx");
        Assert.Equal(HistoricalWorkbookContainerType.Ooxml, inspection.ContainerType);
        Assert.True(inspection.IsProcessable);
        Assert.False(reader.Inspect(missing, "missing.xlsx").IsProcessable);
        Assert.Equal(HistoricalWorkbookContainerType.OleCdfV2, reader.Inspect(ole, "legacy.xlsx").ContainerType);
        Assert.Equal(HistoricalWorkbookContainerType.Unknown, reader.Inspect(corrupt, "corrupt.xlsx").ContainerType);
        Assert.Equal(64, inspection.Sha256.Length);
    }

    [Fact]
    public async Task Reload_DeduplicatesAndParsesMainAndChildRows()
    {
        var original = CreateWorkbook("SG 943 sample.xlsx", true);
        File.Copy(original, Path.Combine(_directory, "Copy of SG 943 sample.xlsx"));
        var corpus = new HistoricalQuoteCorpus(new HistoricalPricingOptions(_directory, 20), new HistoricalWorkbookReader());
        var snapshot = await corpus.ReloadAsync(TestContext.Current.CancellationToken);
        var quote = Assert.Single(snapshot.Quotes);
        Assert.Equal(500000m, quote.DocumentCommercialTotal);
        Assert.Equal(2, quote.Items.Count);
        var item = Assert.Single(quote.Items, item => item.Reference == "01");
        Assert.Equal("01", item.Reference);
        Assert.Equal("VENECIA SERIE 40", item.SystemRaw);
        Assert.Equal("LAMINADO", item.GlassFamily);
        Assert.Equal("NEGRO MATE", item.FinishRaw);
        Assert.Equal(12m, item.DerivedArea);
        Assert.Equal(150000m, item.PublicUnitPrice);
        Assert.Equal(item.PublicUnitPrice * item.Quantity, item.PublicTotal);
        var quantityOne = Assert.Single(quote.Items, item => item.Reference == "02");
        Assert.Equal(quantityOne.PublicUnitPrice, quantityOne.PublicTotal);
        Assert.Equal(1, corpus.Audit().DuplicateFiles);
    }

    [Fact]
    public async Task CandidateSearch_PrioritizesMatchingSignals()
    {
        CreateWorkbook("quote.xlsx", true);
        var options = new HistoricalPricingOptions(_directory, 20);
        var corpus = new HistoricalQuoteCorpus(options, new HistoricalWorkbookReader());
        await corpus.ReloadAsync(TestContext.Current.CancellationToken);
        var candidate = Assert.Single(new HistoricalComparableCandidateService(corpus, options).Find(
            new HistoricalCandidateQuery("VENTANA", "SERIE 40", "LAMINADO 4+4", 4m, "CORREDIZA", 3m, 2m, 6m, "NEGRO MATE", 2m)));
        Assert.Contains("category", candidate.MatchedSignals);
        Assert.Contains("system", candidate.MatchedSignals);
        Assert.Contains("glass", candidate.MatchedSignals);
        Assert.Contains("area", candidate.MatchedSignals);
        Assert.Contains("quantity", candidate.MatchedSignals);
        Assert.Empty(new HistoricalComparableCandidateService(corpus, options).Find(
            new HistoricalCandidateQuery("BARANDA", null, null, null, null, null, null, null, null, null)));
        Assert.True(new[] { candidate }.DistinctBy(value => (value.HistoricalQuoteId, value.HistoricalItemId)).Count() == 1);
    }

    [Fact]
    public async Task CandidateSearch_AppliesCandidateAndQuoteExclusionsBeforeTopK()
    {
        CreateWorkbook("quote.xlsx", true);
        CreateWorkbook("quote-two.xlsx", true, " TWO");
        var options = new HistoricalPricingOptions(_directory, 20);
        var corpus = new HistoricalQuoteCorpus(options, new HistoricalWorkbookReader());
        await corpus.ReloadAsync(TestContext.Current.CancellationToken);
        var service = new HistoricalComparableCandidateService(corpus, options);
        var query = new HistoricalCandidateQuery(
            "VENTANA", "SERIE 40", "LAMINADO 4+4", 4m, "CORREDIZA",
            3m, 2m, 6m, "NEGRO MATE", 2m, 2);
        var baseline = service.Find(query);
        Assert.Equal(2, baseline.Count);

        var withoutCandidate = service.Find(query with
        {
            ExcludedCandidateIds = [baseline[0].HistoricalItemId]
        });
        Assert.DoesNotContain(withoutCandidate, value =>
            value.HistoricalItemId == baseline[0].HistoricalItemId);
        Assert.Contains(withoutCandidate, value =>
            value.HistoricalItemId == baseline[1].HistoricalItemId);

        var recomposedTop = service.Find(query with
        {
            Top = 1,
            ExcludedCandidateIds = [baseline[0].HistoricalItemId]
        });
        Assert.Equal(baseline[1].HistoricalItemId, Assert.Single(recomposedTop).HistoricalItemId);

        var withoutQuote = service.Find(query with
        {
            ExcludedQuoteIds = [baseline[0].HistoricalQuoteId]
        });
        Assert.DoesNotContain(withoutQuote, value =>
            value.HistoricalQuoteId == baseline[0].HistoricalQuoteId);
        Assert.Contains(withoutQuote, value =>
            value.HistoricalQuoteId == baseline[1].HistoricalQuoteId);
        Assert.Equal(
            baseline.Select(value => value.HistoricalItemId),
            service.Find(query with
            {
                ExcludedCandidateIds = [],
                ExcludedQuoteIds = []
            }).Select(value => value.HistoricalItemId));
    }

    [Theory]
    [InlineData("VIDRIO TEMPLADO 8 MM", "TEMPLADO", 8)]
    [InlineData("LAMINADO 4 + PVB 0.38 + 4", "LAMINADO", 4)]
    public void Normalizer_RecognizesGlass(string raw, string family, int thickness)
    {
        Assert.Equal(family, HistoricalQuoteNormalizer.GlassFamily(raw));
        Assert.Equal(thickness, HistoricalQuoteNormalizer.GlassThickness(raw));
    }

    [Fact]
    public void Ranking_ExactGlassThicknessOutweighsAreaProximity()
    {
        Assert.True(HistoricalCandidateRankingWeights.GlassThickness > HistoricalCandidateRankingWeights.Area);
    }

    private string CreateWorkbook(string name, bool quotation, string clientSuffix = "")
    {
        var path = Path.Combine(_directory, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(archive, "xl/workbook.xml", $"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"{(quotation ? "COTIZACIÓN" : "OTRA") }\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Write(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\"/></Relationships>");
        Write(archive, "xl/worksheets/sheet1.xml", """
          <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
          <row r="1"><c r="A1" t="inlineStr"><is><t>CLIENTE</t></is></c><c r="C1" t="inlineStr"><is><t>Cliente Demo{{clientSuffix}}</t></is></c></row>
          <row r="2"><c r="A2" t="inlineStr"><is><t>ÍTEM</t></is></c><c r="B2" t="inlineStr"><is><t>UBICACIÓN</t></is></c><c r="C2" t="inlineStr"><is><t>CONFIGURACIÓN</t></is></c><c r="D2" t="inlineStr"><is><t>DESCRIPCIÓN</t></is></c><c r="J2" t="inlineStr"><is><t>ANCHO</t></is></c><c r="K2" t="inlineStr"><is><t>ALTO</t></is></c><c r="L2" t="inlineStr"><is><t>M2</t></is></c><c r="M2" t="inlineStr"><is><t>CNT</t></is></c><c r="N2" t="inlineStr"><is><t>VR UNT</t></is></c><c r="O2" t="inlineStr"><is><t>VR TOTAL</t></is></c></row>
          <row r="3"><c r="A3" t="inlineStr"><is><t>01</t></is></c><c r="B3" t="inlineStr"><is><t>VENTANA SALA</t></is></c><c r="C3" t="inlineStr"><is><t>CORREDIZA</t></is></c><c r="D3" t="inlineStr"><is><t>VENTANA CORREDIZA</t></is></c><c r="J3"><v>3</v></c><c r="K3"><v>2</v></c><c r="L3"><v>12</v></c><c r="M3"><v>2</v></c><c r="N3"><f>AL3</f><v>150000</v></c><c r="O3"><f>N3*M3</f><v>300000</v></c></row>
          <row r="4"><c r="C4" t="inlineStr"><is><t>SISTEMA</t></is></c><c r="D4" t="inlineStr"><is><t>VENECIA SERIE 40</t></is></c></row>
          <row r="5"><c r="C5" t="inlineStr"><is><t>CRISTAL</t></is></c><c r="D5" t="inlineStr"><is><t>LAMINADO 4 + PVB 0.38 + 4</t></is></c></row>
          <row r="6"><c r="C6" t="inlineStr"><is><t>ACABADO ALUMINIO</t></is></c><c r="D6" t="inlineStr"><is><t>NEGRO MATE</t></is></c></row>
          <row r="7"><c r="C7" t="inlineStr"><is><t>OBSERVACIONES</t></is></c><c r="D7" t="inlineStr"><is><t>Incluye accesorios</t></is></c></row>
          <row r="8"><c r="A8" t="inlineStr"><is><t>02</t></is></c><c r="B8" t="inlineStr"><is><t>PUERTA</t></is></c><c r="C8" t="inlineStr"><is><t>BATIENTE</t></is></c><c r="D8" t="inlineStr"><is><t>PUERTA BATIENTE</t></is></c><c r="J8"><v>1</v></c><c r="K8"><v>2</v></c><c r="L8"><v>2</v></c><c r="M8"><v>1</v></c><c r="N8"><f>AL8</f><v>200000</v></c><c r="O8"><f>N8*M8</f><v>200000</v></c></row>
          <row r="9"><c r="A9" t="inlineStr"><is><t>ANTICIPO 60%</t></is></c><c r="O9"><f>SUM(O3:O8)</f><v>500000</v></c></row>
          <row r="10"><c r="N10" t="inlineStr"><is><t>VALOR TOTAL</t></is></c><c r="O10"><f>SUM(O3:O8)</f><v>500000</v></c></row>
          </sheetData><mergeCells count="2"><mergeCell ref="N3:N7"/><mergeCell ref="O3:O7"/></mergeCells></worksheet>
          """.Replace("{{clientSuffix}}", clientSuffix, StringComparison.Ordinal));
        return path;
    }

    private static void Write(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
