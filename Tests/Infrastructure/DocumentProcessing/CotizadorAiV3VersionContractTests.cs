using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Application.Common.Abstractions.DocumentProcessing;
using CotizadorBackend.Tests.TestDoubles;
using Infrastructure.DocumentProcessing;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class CotizadorAiV3VersionContractTests
{
    private static readonly Guid DocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AttemptId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CorrelationId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("2.0", "rule_based_v1", true)]
    [InlineData("2.0", "rule_based_v2", false)]
    [InlineData("3.0", "rule_based_v2", true)]
    [InlineData("3.0", "rule_based_v1", false)]
    [InlineData("3.0", "unknown", false)]
    public async Task ProcessAsync_ValidatesStructuredMethodBySchemaVersion(
        string schemaVersion,
        string structuredMethod,
        bool expectedSuccess)
    {
        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var result = await ExecuteAsync(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                schemaVersion: schemaVersion,
                structuredMethod: structuredMethod),
            diagnostics);

        Assert.Equal(expectedSuccess, result.IsSuccess);
        if (!expectedSuccess)
        {
            Assert.Equal(DocumentProcessingClientFailure.InvalidResponse,
                result.Failure);
            diagnostics.Received(1).ContractRejected(
                DocumentId,
                AttemptId,
                CorrelationId,
                200,
                "structured_metadata",
                "method_mismatch");
        }
    }

    [Fact]
    public async Task ProcessAsync_WithAssignedUnnormalizedGlass_AcceptsReview()
    {
        var root = JsonNode.Parse(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                classification: "PDF_MIXED",
                pageCount: 2,
                schemaVersion: "3.0",
                status: "REQUIRES_REVIEW"))!.AsObject();
        var structured = root["structuredExtraction"]!.AsObject();
        var item = structured["items"]![0]!.AsObject();
        item["requiresReview"] = true;
        item["reviewReasons"] = new JsonArray(
            "GLASS_TYPE_NOT_IDENTIFIED");
        item["glass"] = new JsonObject
        {
            ["rawSpecification"] =
                "Vidrio laminado de seguridad 12 mm",
            ["normalizedCode"] = null,
            ["assignmentScope"] = "ITEM",
            ["requiresReview"] = true,
            ["reviewReasons"] = new JsonArray(
                "GLASS_TYPE_NOT_IDENTIFIED"),
            ["sourcePages"] = new JsonArray(1),
            ["evidence"] = new JsonArray(new JsonObject
            {
                ["pageNumber"] = 1,
                ["sourceType"] = "NATIVE",
                ["text"] = "Vidrio laminado de seguridad 12 mm"
            })
        };
        var summary = structured["summary"]!.AsObject();
        summary["itemsRequiringReview"] = 1;
        summary["identifiedGlassItemCount"] = 0;
        summary["glassItemsRequiringReview"] = 1;

        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var result = await ExecuteAsync(root.ToJsonString(), diagnostics);

        diagnostics.DidNotReceive().ContractRejected(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(),
            Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>());
        Assert.True(result.IsSuccess);
        var glass = Assert.Single(
            result.Response!.StructuredExtraction!.Items).Glass;
        Assert.NotNull(glass);
        Assert.Null(glass.NormalizedCode);
        Assert.Equal(global::Domain.PreQuotes.GlassAssignmentScope.Item,
            glass.AssignmentScope);
        Assert.True(glass.RequiresReview);
    }

    [Theory]
    [InlineData("TEMP_5")]
    [InlineData("TEMP_6")]
    [InlineData("TEMP_8")]
    [InlineData("TEMP_10")]
    [InlineData("LAM_4_4")]
    [InlineData("LAM_4_4_GRAY")]
    [InlineData("LAM_5_5")]
    [InlineData("LAM_5_5_GRAY")]
    [InlineData("UNKNOWN_GLASS")]
    public async Task ProcessAsync_WithCanonicalGlassCode_AcceptsContract(
        string normalizedCode)
    {
        var root = JsonNode.Parse(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                schemaVersion: "3.0"))!.AsObject();
        var structured = root["structuredExtraction"]!.AsObject();
        var item = structured["items"]![0]!.AsObject();
        var glass = item["glass"]!.AsObject();
        glass["normalizedCode"] = normalizedCode;

        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var result = await ExecuteAsync(root.ToJsonString(), diagnostics);

        Assert.True(result.IsSuccess);
        diagnostics.DidNotReceive().ContractRejected(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<int?>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<string>?>());
        Assert.Equal(normalizedCode, Assert.Single(
            result.Response!.StructuredExtraction!.Items).Glass!.NormalizedCode);
    }

    [Fact]
    public async Task ProcessAsync_WithUnknownGlassCode_ReportsRejectedCodeAndItemSequence()
    {
        var root = JsonNode.Parse(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                schemaVersion: "3.0"))!.AsObject();
        var structured = root["structuredExtraction"]!.AsObject();
        var item = structured["items"]![0]!.AsObject();
        var glass = item["glass"]!.AsObject();
        glass["normalizedCode"] = "UNKNOWN_CODE";

        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var result = await ExecuteAsync(root.ToJsonString(), diagnostics);

        Assert.False(result.IsSuccess);
        Assert.Equal(DocumentProcessingClientFailure.InvalidResponse,
            result.Failure);
        diagnostics.Received(1).ContractRejected(
            DocumentId,
            AttemptId,
            CorrelationId,
            200,
            "glass_contract",
            "unknown_code",
            1,
            "UNKNOWN_CODE",
            Arg.Is<IReadOnlyList<string>>(codes =>
                codes != null
                && codes.Count == 9
                && codes[0] == "LAM_4_4"
                && codes[1] == "LAM_4_4_GRAY"
                && codes[2] == "LAM_5_5"
                && codes[3] == "LAM_5_5_GRAY"
                && codes[4] == "TEMP_10"
                && codes[5] == "TEMP_5"
                && codes[6] == "TEMP_6"
                && codes[7] == "TEMP_8"
                && codes[8] == "UNKNOWN_GLASS"));
    }

    [Fact]
    public async Task ProcessAsync_WithInvalidItemMeasurements_ReportsInvalidDataContext()
    {
        var root = JsonNode.Parse(
            DocumentProcessingPayloadFactory.CreateSuccess(
                DocumentId,
                AttemptId,
                schemaVersion: "3.0"))!.AsObject();
        var structured = root["structuredExtraction"]!.AsObject();
        var item = structured["items"]![0]!.AsObject();
        item["heightMillimeters"] = null;

        var diagnostics = Substitute.For<IDocumentProcessingDiagnostics>();
        var result = await ExecuteAsync(root.ToJsonString(), diagnostics);

        Assert.False(result.IsSuccess);
        Assert.Equal(DocumentProcessingClientFailure.InvalidResponse,
            result.Failure);
        diagnostics.Received(1).ContractRejected(
            DocumentId,
            AttemptId,
            CorrelationId,
            null,
            "response_contract",
            "invalid_data",
            null,
            null,
            null,
            "ResponseContractValidationException",
            "structuredExtraction item width and height must both be present or both be null.",
            "structuredExtraction.items[0].heightMillimeters",
            "structuredExtraction.items[0].heightMillimeters",
            "width=1200;height=null");
    }

    private static async Task<DocumentProcessingClientResult> ExecuteAsync(
        string payload,
        IDocumentProcessingDiagnostics? diagnostics)
    {
        using var httpClient = new HttpClient(new Handler(payload))
        {
            BaseAddress = new Uri("http://localhost:8001/")
        };
        var client = new CotizadorAiDocumentProcessingClient(
            httpClient,
            new CotizadorAiOptions(
                new Uri("http://localhost:8001/"), 30, 33_554_432, 100),
            diagnostics);
        return await client.ProcessAsync(
            new DocumentProcessingClientRequest(
                DocumentId, AttemptId, CorrelationId,
                "document.pdf", 4,
                new MemoryStream([1, 2, 3, 4])),
            TestContext.Current.CancellationToken);
    }

    private sealed class Handler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    payload, Encoding.UTF8, "application/json")
            };
            response.Headers.Add(
                "X-Correlation-ID", CorrelationId.ToString("D"));
            return Task.FromResult(response);
        }
    }
}
