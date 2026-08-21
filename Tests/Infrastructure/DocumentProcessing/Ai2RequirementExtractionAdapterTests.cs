using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Infrastructure.DocumentProcessing;
using System.Text.Json;
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
                "functional_type":{"normalized":"SLIDING_WINDOW","raw":"Ventana corrediza","status":"inferred","confidence":0.86,"evidence_ids":["ev-item"]},
                "configuration":{
                  "raw_description":"2 hojas",
                  "operation":{"normalized":"SLIDING","raw":"corrediza","status":"explicit","confidence":0.90,"evidence_ids":["ev-item"]},
                  "panel_count":{"value":2,"status":"explicit","confidence":0.88,"evidence_ids":["ev-item"]},
                  "movable_panel_count":{"value":1,"status":"explicit","confidence":0.88,"evidence_ids":["ev-item"]},
                  "fixed_panel_count":{"value":1,"status":"explicit","confidence":0.88,"evidence_ids":["ev-item"]},
                  "modulation":{"normalized":"TWO_PANELS","raw":"2 hojas","status":"explicit","confidence":0.90,"evidence_ids":["ev-item"]},
                  "opening_direction":{"normalized":"LEFT","raw":"izquierda","status":"inferred","confidence":0.70,"evidence_ids":["ev-item"]},
                  "special_features":["mosquitero","riel triple"],
                  "status":"explicit",
                  "evidence_ids":["ev-item"]
                },
                "geometry":{"normalized_type":"RECTANGULAR","status":"explicit","confidence":0.95,"evidence_ids":["ev-item"]},
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
        Assert.Equal("SLIDING_WINDOW", item.FunctionalType);
        Assert.Equal("SLIDING", item.Operation);
        Assert.Equal(2, item.PanelCount);
        Assert.Equal(1, item.MovablePanelCount);
        Assert.Equal(1, item.FixedPanelCount);
        Assert.Equal("TWO_PANELS", item.Modulation);
        Assert.Equal("LEFT", item.OpeningDirection);
        Assert.Equal(["mosquitero", "riel triple"], item.SpecialFeatures);
        Assert.Equal("RECTANGULAR", item.GeometryType);
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

    [Fact]
    public void Adapt_WithCurrentAi2Shape_AcceptsTraceableTechnicalObjects()
    {
        var file = new DocumentProcessingFile(
            Guid.NewGuid(), "casa.pdf", "application/pdf",
            10, new MemoryStream(new byte[10]));
        var request = new DocumentProcessingClientRequest(
            file.DocumentId, Guid.NewGuid(), Guid.NewGuid(), [file]);

        var result = new Ai2RequirementExtractionAdapter().Adapt(
            Ai2RequirementExtractionPayloads.RealCurrentAi2Shape,
            request);

        Assert.Equal(DocumentProcessingOutcome.RequiresReview, result.Outcome);
        Assert.Equal(0, result.ProcessingMetadata.DurationMs);
        Assert.Contains(result.Warnings, warning =>
            warning.Code == "MEASUREMENT_AREA_MISMATCH");
        var item = Assert.Single(result.StructuredExtraction!.Items);
        Assert.Equal("PV-06", item.Reference);
        Assert.Equal("SLIDING_DOOR", item.FunctionalType);
        Assert.Equal("SLIDING", item.Operation);
        Assert.Equal(4, item.PanelCount);
        Assert.Equal(2, item.MovablePanelCount);
        Assert.Equal(2, item.FixedPanelCount);
        Assert.Equal("RECTANGULAR", item.GeometryType);
        Assert.Equal(["POCKET", "LOWER_FIXED_PANEL"], item.SpecialFeatures);
        Assert.Equal("TEMP_6", item.Glass?.NormalizedCode);
        Assert.Equal("Templado 6 mm", item.Glass?.RawSpecification);
        Assert.Equal("3831", item.TechnicalClassification?.SystemCode);
        Assert.Equal("BLACK_MATTE", item.TechnicalClassification?.FinishCode);
        Assert.DoesNotContain(item.Evidence, evidence =>
            evidence.PageNumber is null
            && evidence.SheetName is null
            && evidence.CellRange is null);
    }

    [Fact]
    public void Adapt_WithRealCapturedAi2Payload_IsAcceptedByAdapter()
    {
        var payload = ReadFixture(
            "DocumentProcessing",
            "ai2-real-ventaneria-puertas.json");
        AssertRealAi2RequirementExtractionFixture(payload);

        var result = new Ai2RequirementExtractionAdapter().Adapt(
            payload,
            CreatePdfRequest());

        Assert.Equal(DocumentProcessingProvider.Ai2, result.Provider);
        Assert.Equal(15, result.StructuredExtraction?.Items.Count);
        var items = result.StructuredExtraction!.Items;
        Assert.Equal(
            [
                "V-01",
                "V-04",
                "V-05",
                "PV-06",
                "V-08",
                "PV-09",
                "V-10",
                "V-14",
                "PV-15",
                "PV-18",
                "PV-17",
                "V-22",
                "V-23",
                "PV-24",
                "V-25"
            ],
            items.Select(item => item.Reference).ToArray());

        var v01 = Assert.Single(items, item => item.Reference == "V-01");
        Assert.Equal(4, v01.Quantity);
        Assert.Equal(600, v01.WidthMillimeters);
        Assert.Equal(1800, v01.HeightMillimeters);
        Assert.Equal("PROJECTING", v01.FunctionalType);
        Assert.Equal("PROJECTING", v01.Operation);
        Assert.Equal(2, v01.PanelCount);
        Assert.Equal(1, v01.MovablePanelCount);
        Assert.Equal(1, v01.FixedPanelCount);
        Assert.Contains("LOWER_FIXED_PANEL", v01.SpecialFeatures!);
        Assert.Contains("ASSOCIATED_FIXED_PANEL", v01.SpecialFeatures!);
        Assert.Equal("templado", v01.GlassTypeNormalized);
        Assert.Equal(6m, v01.GlassThicknessMm);
        Assert.Equal("3831", v01.RequestedProfileRaw);
        Assert.Equal("negro pintura al horno", v01.FinishRawDescription);
        var glassResolution = new GlassCandidateResolver().Resolve(
            new GlassCandidateResolutionInput(
                v01.Glass?.RawSpecification,
                v01.GlassTypeRaw,
                v01.GlassTypeNormalized,
                v01.GlassThicknessMm,
                v01.GlassColorRaw,
                v01.GlassColorNormalized,
                v01.GlassTreatmentRaw,
                v01.GlassTreatmentNormalized,
                v01.GlassComposition,
                v01.GlassCoating,
                v01.GlassTransparency),
            CotizadorBackend.Tests.Application.PreQuotes
                .GlassCandidateResolverTests.Catalog());
        Assert.Equal("TEMP_6", glassResolution.Suggested?.Code);
        Assert.Equal(
            "COMPOSICION MONOLITICO TEMPLADO 6 MM INC",
            glassResolution.Suggested?.DisplayName);

        var pv15 = Assert.Single(items, item => item.Reference == "PV-15");
        Assert.Equal(1.33m, pv15.AreaSquareMeters);
        Assert.Equal(5320, pv15.WidthMillimeters);
        Assert.Equal(2500, pv15.HeightMillimeters);
        Assert.True(pv15.RequiresReview);
        Assert.Contains(
            StructuredIssueCode.MissingOrInvalidMeasurements,
            pv15.ReviewReasons);

        var v25 = Assert.Single(items, item => item.Reference == "V-25");
        Assert.Equal("CORNER", v25.GeometryType);
        Assert.Equal("SLIDING_WINDOW", v25.FunctionalType);
        Assert.Equal("8025", v25.RequestedProfileRaw);
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

    private static string ReadFixture(params string[] paths)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                [directory.FullName, "Tests", "Fixtures", .. paths]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "No se encontro el fixture requerido.",
            Path.Combine(["Tests", "Fixtures", .. paths]));
    }

    private static void AssertRealAi2RequirementExtractionFixture(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        Assert.True(
            root.TryGetProperty("requirement", out var requirement)
            && requirement.ValueKind == JsonValueKind.Object,
            "Fixture invalido: falta $.requirement. No corresponde al body bruto real de /requirements/extract.");
        Assert.True(
            root.TryGetProperty("elements", out var elements)
            && elements.ValueKind == JsonValueKind.Array,
            "Fixture invalido: falta $.elements. No corresponde al body bruto real de /requirements/extract.");
        Assert.True(
            root.TryGetProperty("evidence", out var evidence)
            && evidence.ValueKind == JsonValueKind.Array,
            "Fixture invalido: falta $.evidence. No corresponde al body bruto real de /requirements/extract.");
        Assert.True(
            root.TryGetProperty("extraction_metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object,
            "Fixture invalido: falta $.extraction_metadata. No corresponde al body bruto real de /requirements/extract.");

        Assert.Equal(15, elements.GetArrayLength());
        Assert.True(
            metadata.TryGetProperty("element_count", out var elementCount)
            && elementCount.GetInt32() == 15,
            "Fixture invalido: $.extraction_metadata.element_count debe ser 15.");
        Assert.True(
            metadata.TryGetProperty("model", out var model)
            && model.GetString() == "gemini-3.6-flash",
            "Fixture invalido: $.extraction_metadata.model debe ser gemini-3.6-flash.");

        var hasMeasurementAreaMismatch =
            root.TryGetProperty("warnings", out var warnings)
            && warnings.ValueKind == JsonValueKind.Array
            && warnings.EnumerateArray().Any(warning =>
                warning.ValueKind == JsonValueKind.Object
                && warning.TryGetProperty("code", out var code)
                && code.GetString() == "MEASUREMENT_AREA_MISMATCH");
        Assert.True(
            hasMeasurementAreaMismatch,
            "Fixture invalido: debe contener warning MEASUREMENT_AREA_MISMATCH.");
    }
}
