using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using Infrastructure.DocumentProcessing;
using System.Text.Json.Nodes;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class Ai2RequirementExtractionAdapterTests
{
    [Fact]
    public void Adapt_MapsCanonicalRequirementSubsetAndTraceability()
    {
        const string payload =
            """
            {
              "requirement": {
                "project_name": {"value":"Proyecto Uno","status":"explicit","confidence":0.99,"evidence_ids":["ev-project"]},
                "general_technical_notes": ["Verificar medidas"]
              },
              "sources": [{"id":"s1","file_name":"cuadro.xlsx","media_type":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","sheet_names":["Cotizacion"]}],
              "elements": [{
                "id":"e1",
                "reference":{"value":"V-01","status":"explicit","confidence":0.98,"evidence_ids":["ev-item"]},
                "name":{"value":"Ventana principal","status":"explicit","confidence":0.97,"evidence_ids":["ev-item"]},
                "category":{"normalized":"WINDOW","raw":"Ventana","status":"inferred","confidence":0.80,"evidence_ids":["ev-item"]},
                "description":"Ventana principal",
                "measurements":[
                  {"type":"width","value":1.2,"unit":"m","status":"explicit","evidence_ids":["ev-item"]},
                  {"type":"height","value":1.5,"unit":"m","status":"explicit","evidence_ids":["ev-item"]},
                  {"type":"area","value":1.8,"unit":"m2","status":"inferred","evidence_ids":["ev-item"]}
                ],
                "quantity":{"value":2,"status":"explicit","evidence_ids":["ev-item"]},
                "configuration":{"raw_description":"2 hojas","status":"explicit","evidence_ids":["ev-item"]},
                "glass":[{"type":{"normalized":"TEMP_8","raw":"Templado 8 mm","status":"explicit","evidence_ids":["ev-glass"]},"status":"explicit","confidence":0.95,"evidence_ids":["ev-glass"]}],
                "profiles":[{"code":{"value":"K50","status":"explicit","evidence_ids":["ev-item"]},"status":"explicit","confidence":0.91,"evidence_ids":["ev-item"]}],
                "finish":{"normalized_type":"ANODIZED","raw_description":"Anodizado natural","status":"inferred","confidence":0.75,"evidence_ids":["ev-item"]},
                "evidence_ids":["ev-item"],
                "missing_fields":[],
                "confidence":0.90
              }],
              "evidence":[
                {"id":"ev-project","source_id":"s1","type":"cell","sheet_name":"Cotizacion","cell_range":"A1:B1","extracted_text":"Proyecto Uno","status":"explicit","confidence":0.99},
                {"id":"ev-item","source_id":"s1","type":"range","sheet_name":"Cotizacion","cell_range":"A12:H12","extracted_text":"V-01 Ventana principal","status":"explicit","confidence":0.95},
                {"id":"ev-glass","source_id":"s1","type":"cell","sheet_name":"Cotizacion","cell_range":"E12:E12","extracted_text":"Templado 8 mm","status":"explicit","confidence":0.95}
              ],
              "relationships":[],
              "conflicts":[],
              "warnings":[],
              "extraction_metadata":{"schema_version":"1.0","source_count":1,"element_count":1,"partial":false,"status":"completed","processing_time_ms":125,"pipeline_version":"ai2-v1"}
            }
            """;
        var file = new DocumentProcessingFile(
            Guid.NewGuid(), "cuadro.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            10, new MemoryStream(new byte[10]));
        var request = new DocumentProcessingClientRequest(
            file.DocumentId, Guid.NewGuid(), Guid.NewGuid(), [file]);

        var result = new Ai2RequirementExtractionAdapter().Adapt(payload, request);

        Assert.Equal(DocumentProcessingProvider.Ai2, result.Provider);
        Assert.Equal("Proyecto Uno", result.StructuredExtraction?.ProjectName);
        var item = Assert.Single(result.StructuredExtraction!.Items);
        Assert.Equal("V-01", item.Reference);
        Assert.Equal(1200, item.WidthMillimeters);
        Assert.Equal(1500, item.HeightMillimeters);
        Assert.Equal(1.8m, item.AreaSquareMeters);
        Assert.Equal(2, item.Quantity);
        Assert.Equal("TEMP_8", item.Glass?.NormalizedCode);
        Assert.Equal("K50", item.TechnicalClassification?.SystemCode);
        Assert.Equal("ANODIZED", item.TechnicalClassification?.FinishCode);
        Assert.Equal("2 hojas", item.Configuration);
        Assert.Equal(CanonicalExtractionValueStatus.Inferred, item.ExtractionStatus);
        Assert.Contains(item.Evidence, evidence =>
            evidence.SheetName == "Cotizacion"
            && evidence.CellRange == "A12:H12"
            && evidence.SourceId == "s1");
    }

    [Fact]
    public void Adapt_WithRealAi2PdfShape_UsesRawFallbacksAndPreservesReviewableArea()
    {
        var file = new DocumentProcessingFile(
            Guid.NewGuid(), "requerimiento.pdf", "application/pdf",
            10, new MemoryStream(new byte[10]));
        var request = new DocumentProcessingClientRequest(
            file.DocumentId, Guid.NewGuid(), Guid.NewGuid(), [file]);

        var result = new Ai2RequirementExtractionAdapter().Adapt(
            Ai2RequirementExtractionPayloads.RealisticPdf,
            request);

        Assert.Equal(DocumentProcessingOutcome.RequiresReview, result.Outcome);
        var items = result.StructuredExtraction!.Items;
        Assert.Equal(4, items.Count);
        Assert.Equal(StructuredElementType.Window, items[0].ElementType);
        Assert.Equal(StructuredElementType.Door, items[1].ElementType);
        Assert.Equal("3831", items[1].TechnicalClassification?.SystemCode);
        Assert.Equal("templado", items[1].Glass?.NormalizedCode);
        Assert.Equal("templado 6.0 mm monolitico", items[1].Glass?.RawSpecification);
        Assert.Equal(
            "Negro pintura al horno",
            items[1].TechnicalClassification?.FinishCode);
        Assert.Equal(1.33m, items[3].AreaSquareMeters);
        Assert.True(items[3].RequiresReview);
        Assert.Contains(
            StructuredIssueCode.MissingOrInvalidMeasurements,
            items[3].ReviewReasons);
        Assert.Contains(result.Warnings, warning =>
            warning.Code == "MEASUREMENT_AREA_MISMATCH");
        Assert.Contains(result.Warnings, warning =>
            warning.Code == "enrichment_warning");
        Assert.Equal(0, result.ProcessingMetadata.DurationMs);
    }

    [Theory]
    [InlineData("requirement")]
    [InlineData("elements")]
    [InlineData("evidence")]
    [InlineData("extraction_metadata")]
    public void Adapt_WhenRequiredRootPropertyIsMissing_ReportsExactProperty(
        string propertyName)
    {
        var root = JsonNode.Parse(
            Ai2RequirementExtractionPayloads.RealisticPdf)!.AsObject();
        root.Remove(propertyName);

        var exception = Assert.Throws<InvalidDataException>(() =>
            new Ai2RequirementExtractionAdapter().Adapt(
                root.ToJsonString(),
                CreatePdfRequest()));

        Assert.Contains($"$.{propertyName}", exception.Message);
    }

    [Fact]
    public void Adapt_WhenElementsHasWrongType_ReportsExpectedAndActualType()
    {
        var root = JsonNode.Parse(
            Ai2RequirementExtractionPayloads.RealisticPdf)!.AsObject();
        root["elements"] = new JsonObject();

        var exception = Assert.Throws<InvalidDataException>(() =>
            new Ai2RequirementExtractionAdapter().Adapt(
                root.ToJsonString(),
                CreatePdfRequest()));

        Assert.Contains("$.elements", exception.Message);
        Assert.Contains("Object", exception.Message);
        Assert.Contains("Array", exception.Message);
    }

    [Fact]
    public void Adapt_WithDuplicateReferences_PreservesBothSequences()
    {
        var root = JsonNode.Parse(
            Ai2RequirementExtractionPayloads.RealisticPdf)!.AsObject();
        var elements = root["elements"]!.AsArray();
        var duplicate = elements[0]!.DeepClone().AsObject();
        duplicate["id"] = "e-v01-duplicate";
        duplicate["name"]!["value"] = "Puerta con referencia repetida";
        duplicate["category"]!["raw"] = "PUERTA VIDRIERA";
        elements.Add(duplicate);

        var result = new Ai2RequirementExtractionAdapter().Adapt(
            root.ToJsonString(),
            CreatePdfRequest());

        var repeated = result.StructuredExtraction!.Items
            .Where(item => item.Reference == "V-01")
            .ToArray();
        Assert.Equal(2, repeated.Length);
        Assert.Equal(2, repeated.Select(item => item.Sequence).Distinct().Count());
        Assert.Contains(repeated, item => item.ElementType == StructuredElementType.Window);
        Assert.Contains(repeated, item => item.ElementType == StructuredElementType.Door);
    }

    private static DocumentProcessingClientRequest CreatePdfRequest()
    {
        var file = new DocumentProcessingFile(
            Guid.NewGuid(), "requerimiento.pdf", "application/pdf",
            10, new MemoryStream(new byte[10]));
        return new DocumentProcessingClientRequest(
            file.DocumentId, Guid.NewGuid(), Guid.NewGuid(), [file]);
    }
}
