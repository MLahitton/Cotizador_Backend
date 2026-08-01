using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Application.Common.Abstractions.DocumentProcessing;
using CotizadorBackend.Tests.TestDoubles;
using Infrastructure.DocumentProcessing;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class CotizadorAiV2ContractTests
{
    private static readonly Guid DocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AttemptId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CorrelationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task ProcessAsync_WithValidV2_MapsStructuredExtraction()
    {
        var execution = await ExecuteAsync(CreatePayload());

        Assert.True(execution.Result.IsSuccess);
        var requestUri = Assert.IsType<Uri>(
            execution.Request.RequestUri);
        Assert.Equal(
            "/api/v3/prequotes/document-extractions",
            requestUri.AbsolutePath);
        var structured = execution.Result.Response!.StructuredExtraction!;
        Assert.Single(structured.Items);
        Assert.Equal(1200, structured.Items[0].WidthMillimeters);
        Assert.Single(structured.Requirements);
        Assert.Single(structured.DocumentReferences);
        Assert.Equal(2, structured.KnownQuoteableUnitCount);
        Assert.Contains(
            "\"structuredExtraction\"",
            execution.Result.Response.PayloadJson);
    }

    [Theory]
    [InlineData("schema_v1")]
    [InlineData("missing_structured")]
    [InlineData("extra_nested")]
    [InlineData("element_enum")]
    [InlineData("issue_enum")]
    [InlineData("conflict_enum")]
    [InlineData("source_enum")]
    [InlineData("status_enum")]
    [InlineData("item_sequence")]
    [InlineData("reference_sequence")]
    [InlineData("source_disordered")]
    [InlineData("source_duplicate")]
    [InlineData("source_missing")]
    [InlineData("evidence_empty")]
    [InlineData("evidence_long")]
    [InlineData("evidence_missing_page")]
    [InlineData("width_only")]
    [InlineData("height_only")]
    [InlineData("width_zero")]
    [InlineData("height_negative")]
    [InlineData("quantity_zero")]
    [InlineData("quantity_negative")]
    [InlineData("summary_items")]
    [InlineData("summary_references")]
    [InlineData("summary_review")]
    [InlineData("summary_units")]
    [InlineData("document_id")]
    [InlineData("attempt_id")]
    [InlineData("method")]
    [InlineData("duration")]
    public async Task ProcessAsync_WithInvalidV2_ReturnsInvalidResponse(
        string scenario)
    {
        var root = JsonNode.Parse(CreatePayload())!.AsObject();
        Mutate(root, scenario);

        var execution = await ExecuteAsync(root.ToJsonString());

        Assert.False(execution.Result.IsSuccess);
        Assert.Equal(
            DocumentProcessingClientFailure.InvalidResponse,
            execution.Result.Failure);
    }

    [Theory]
    [InlineData("completed_review_item")]
    [InlineData("completed_summary_review")]
    [InlineData("completed_issue")]
    [InlineData("completed_conflict")]
    [InlineData("completed_ocr")]
    [InlineData("completed_project_missing")]
    [InlineData("completed_no_items")]
    [InlineData("completed_incomplete_item")]
    public async Task ProcessAsync_WithIncoherentCompletedStatus_IsInvalid(
        string scenario)
    {
        var root = JsonNode.Parse(CreatePayload())!.AsObject();
        MutateCoherence(root, scenario);

        var execution = await ExecuteAsync(root.ToJsonString());

        Assert.Equal(
            DocumentProcessingClientFailure.InvalidResponse,
            execution.Result.Failure);
    }

    private static void Mutate(JsonObject root, string scenario)
    {
        var structured = root["structuredExtraction"]!.AsObject();
        var item = structured["items"]![0]!.AsObject();
        var reference = structured["documentReferences"]![0]!.AsObject();
        var project = structured["project"]!.AsObject();
        var evidence = project["evidence"]![0]!.AsObject();
        var summary = structured["summary"]!.AsObject();
        switch (scenario)
        {
            case "schema_v1": root["schemaVersion"] = "1.0"; break;
            case "missing_structured": root.Remove("structuredExtraction"); break;
            case "extra_nested": evidence["unexpected"] = true; break;
            case "element_enum": item["elementType"] = "UNKNOWN"; break;
            case "issue_enum":
                structured["issues"] = new JsonArray(new JsonObject
                {
                    ["code"] = "UNKNOWN", ["message"] = "Issue",
                    ["itemSequence"] = 1,
                    ["pageNumbers"] = new JsonArray(1)
                }); break;
            case "conflict_enum":
                structured["conflicts"] = new JsonArray(new JsonObject
                {
                    ["code"] = "UNKNOWN", ["message"] = "Conflict",
                    ["itemSequences"] = new JsonArray(1),
                    ["pageNumbers"] = new JsonArray(1)
                }); break;
            case "source_enum": evidence["sourceType"] = "UNKNOWN"; break;
            case "status_enum": structured["status"] = "UNKNOWN"; break;
            case "item_sequence": item["sequence"] = 2; break;
            case "reference_sequence": reference["sequence"] = 2; break;
            case "source_disordered": project["sourcePages"] = new JsonArray(2, 1); break;
            case "source_duplicate": project["sourcePages"] = new JsonArray(1, 1); break;
            case "source_missing": project["sourcePages"] = new JsonArray(2); break;
            case "evidence_empty": evidence["text"] = ""; break;
            case "evidence_long": evidence["text"] = new string('x', 501); break;
            case "evidence_missing_page": evidence["pageNumber"] = 2; break;
            case "width_only": item["heightMillimeters"] = null; break;
            case "height_only": item["widthMillimeters"] = null; break;
            case "width_zero": item["widthMillimeters"] = 0; break;
            case "height_negative": item["heightMillimeters"] = -1; break;
            case "quantity_zero": item["quantity"] = 0; break;
            case "quantity_negative": item["quantity"] = -1; break;
            case "summary_items": summary["itemCount"] = 2; break;
            case "summary_references": summary["documentReferenceCount"] = 2; break;
            case "summary_review": summary["itemsRequiringReview"] = 1; break;
            case "summary_units": summary["knownQuoteableUnitCount"] = 99; break;
            case "document_id": root["documentId"] = Guid.NewGuid(); break;
            case "attempt_id": root["processingAttemptId"] = Guid.NewGuid(); break;
            case "method": structured["processingMetadata"]!["method"] = "other"; break;
            case "duration": structured["processingMetadata"]!["durationMs"] = -1; break;
        }
    }

    private static void MutateCoherence(
        JsonObject root,
        string scenario)
    {
        var structured = root["structuredExtraction"]!.AsObject();
        var item = structured["items"]![0]!.AsObject();
        var summary = structured["summary"]!.AsObject();
        switch (scenario)
        {
            case "completed_review_item":
                item["requiresReview"] = true;
                break;
            case "completed_summary_review":
                item["requiresReview"] = true;
                summary["itemsRequiringReview"] = 1;
                break;
            case "completed_issue":
                structured["issues"] = new JsonArray(new JsonObject
                {
                    ["code"] = "OCR_REVIEW_REQUIRED",
                    ["message"] = "Review",
                    ["itemSequence"] = 1,
                    ["pageNumbers"] = new JsonArray(1)
                });
                break;
            case "completed_conflict":
                structured["conflicts"] = new JsonArray(new JsonObject
                {
                    ["code"] = "DUPLICATE_ITEM_REFERENCE",
                    ["message"] = "Conflict",
                    ["itemSequences"] = new JsonArray(1),
                    ["pageNumbers"] = new JsonArray(1)
                });
                break;
            case "completed_ocr":
                root["document"]!["requiresOcr"] = true;
                break;
            case "completed_project_missing":
                structured["project"]!["name"] = null;
                break;
            case "completed_no_items":
                structured["items"] = new JsonArray();
                summary["itemCount"] = 0;
                summary["knownQuoteableUnitCount"] = 0;
                break;
            case "completed_incomplete_item":
                item["reference"] = null;
                break;
        }
    }

    private static string CreatePayload() =>
        DocumentProcessingPayloadFactory.CreateSuccess(DocumentId, AttemptId);

    private static async Task<Execution> ExecuteAsync(string payload)
    {
        var handler = new StubHttpMessageHandler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload,
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.TryAddWithoutValidation(
                "X-Correlation-ID",
                CorrelationId.ToString("D"));
            return response;
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8000/")
        };
        var client = new CotizadorAiDocumentProcessingClient(
            httpClient,
            new CotizadorAiOptions(
                new Uri("http://localhost:8000/"),
                30,
                33_554_432,
                100));
        var result = await client.ProcessAsync(
            new DocumentProcessingClientRequest(
                DocumentId,
                AttemptId,
                CorrelationId,
                "document.pdf",
                4,
                new MemoryStream([1, 2, 3, 4])),
            TestContext.Current.CancellationToken);
        return new(result, handler.LastRequest!);
    }

    private sealed record Execution(
        DocumentProcessingClientResult Result,
        CapturedHttpRequest Request);
}
