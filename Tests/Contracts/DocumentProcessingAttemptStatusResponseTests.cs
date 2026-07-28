using System.Text.Json;
using Contracts.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Contracts;

public sealed class DocumentProcessingAttemptStatusResponseTests
{
    private static readonly Guid AttemptId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DocumentId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("PENDING", null)]
    [InlineData("PROCESSING", null)]
    [InlineData("FINISHED", "COMPLETED")]
    [InlineData("FINISHED", "REQUIRES_REVIEW")]
    [InlineData("FINISHED", "FAILED")]
    public void Serialization_UsesExactCasingAndAliases(
        string state,
        string? outcome)
    {
        var response = new DocumentProcessingAttemptStatusResponse(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            state,
            outcome,
            outcome == "FAILED" ? "AI_SERVICE_TIMEOUT" : null,
            new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            state == "PENDING" ? null : new DateTimeOffset(2026, 8, 1, 12, 0, 1, TimeSpan.Zero),
            state == "FINISHED" ? new DateTimeOffset(2026, 8, 1, 12, 0, 2, TimeSpan.Zero) : null,
            null);

        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(state, root.GetProperty("processingState").GetString());
        Assert.Equal(outcome, root.GetProperty("outcome").GetString());
        Assert.True(root.TryGetProperty("processingAttemptId", out _));
        Assert.False(root.TryGetProperty("storageKey", out _));
        Assert.False(root.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public void Serialization_ExposesExactlyTheNinePublicProperties()
    {
        using var document = Serialize(CreateResponse("PENDING"));
        var names = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(
            [
                "completedAtUtc",
                "createdAtUtc",
                "documentId",
                "errorCode",
                "outcome",
                "processingAttemptId",
                "processingState",
                "result",
                "startedAtUtc"
            ],
            names);
    }

    [Fact]
    public void Serialization_PreservesCompleteNestedSuccessfulResult()
    {
        using var payload = JsonDocument.Parse(
            """
            {
              "schemaVersion":"1.0",
              "document":{"pageCount":1,"classification":"TECHNICAL"},
              "pages":[{"pageNumber":1,"text":"Diseño ágil 😀","itemCount":2}],
              "processing":{"status":"COMPLETED","warnings":["REVIEW"]},
              "metadata":{"method":"TEXT_EXTRACTION","durationMs":17}
            }
            """);
        var response = CreateResponse(
            "FINISHED",
            "COMPLETED",
            result: payload.RootElement.Clone());

        using var document = Serialize(response);
        var result = document.RootElement.GetProperty("result");

        Assert.Equal(1, result.GetProperty("document").GetProperty("pageCount").GetInt32());
        Assert.Equal("TECHNICAL", result.GetProperty("document").GetProperty("classification").GetString());
        Assert.Equal("Diseño ágil 😀", result.GetProperty("pages")[0].GetProperty("text").GetString());
        Assert.Equal(2, result.GetProperty("pages")[0].GetProperty("itemCount").GetInt32());
        Assert.Equal("COMPLETED", result.GetProperty("processing").GetProperty("status").GetString());
        Assert.Equal("REVIEW", result.GetProperty("processing").GetProperty("warnings")[0].GetString());
        Assert.Equal("TEXT_EXTRACTION", result.GetProperty("metadata").GetProperty("method").GetString());
        Assert.Equal(17, result.GetProperty("metadata").GetProperty("durationMs").GetInt32());
    }

    [Fact]
    public void Pending_SerializesAllLifecycleOptionalFieldsAsNull()
    {
        using var document = Serialize(CreateResponse("PENDING"));
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("outcome").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("errorCode").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("startedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("result").ValueKind);
    }

    [Fact]
    public void Processing_SerializesStartWithoutTerminalData()
    {
        var startedAt = CreatedAt.AddSeconds(1);
        using var document = Serialize(CreateResponse(
            "PROCESSING",
            startedAtUtc: startedAt));
        var root = document.RootElement;

        Assert.Equal(startedAt, root.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("completedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("result").ValueKind);
    }

    [Fact]
    public void FinishedCompleted_SerializesResultWithoutError()
    {
        using var payload = JsonDocument.Parse("""{"status":"COMPLETED"}""");
        using var document = Serialize(CreateResponse(
            "FINISHED",
            "COMPLETED",
            result: payload.RootElement.Clone()));
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("errorCode").ValueKind);
        Assert.Equal("COMPLETED", root.GetProperty("result").GetProperty("status").GetString());
    }

    [Fact]
    public void FinishedRequiresReview_SerializesResultWithoutError()
    {
        using var payload = JsonDocument.Parse("""{"status":"REQUIRES_REVIEW"}""");
        using var document = Serialize(CreateResponse(
            "FINISHED",
            "REQUIRES_REVIEW",
            result: payload.RootElement.Clone()));
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("errorCode").ValueKind);
        Assert.Equal(
            "REQUIRES_REVIEW",
            root.GetProperty("result").GetProperty("status").GetString());
    }

    [Fact]
    public void FinishedFailed_SerializesOnlySafeErrorCode()
    {
        using var document = Serialize(CreateResponse(
            "FINISHED",
            "FAILED",
            "AI_SERVICE_TIMEOUT"));
        var root = document.RootElement;

        Assert.Equal("AI_SERVICE_TIMEOUT", root.GetProperty("errorCode").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("result").ValueKind);
    }

    [Fact]
    public void Serialization_PreservesUtcTimestampOffset()
    {
        using var document = Serialize(CreateResponse("PENDING"));
        var value = document.RootElement
            .GetProperty("createdAtUtc")
            .GetDateTimeOffset();

        Assert.Equal(TimeSpan.Zero, value.Offset);
    }

    [Fact]
    public void Serialization_PreservesResourceIdentifiers()
    {
        using var document = Serialize(CreateResponse("PENDING"));
        var root = document.RootElement;

        Assert.Equal(AttemptId, root.GetProperty("processingAttemptId").GetGuid());
        Assert.Equal(DocumentId, root.GetProperty("documentId").GetGuid());
    }

    [Fact]
    public void Serialization_DoesNotLeakInternalStorageOrWorkerData()
    {
        var json = JsonSerializer.Serialize(
            CreateResponse("PENDING"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionString", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correlationId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workerId", json, StringComparison.OrdinalIgnoreCase);
    }

    private static DocumentProcessingAttemptStatusResponse CreateResponse(
        string state,
        string? outcome = null,
        string? errorCode = null,
        DateTimeOffset? startedAtUtc = null,
        JsonElement? result = null)
    {
        return new DocumentProcessingAttemptStatusResponse(
            AttemptId,
            DocumentId,
            state,
            outcome,
            errorCode,
            CreatedAt,
            startedAtUtc ?? (state == "FINISHED" ? CreatedAt.AddSeconds(1) : null),
            state == "FINISHED" ? CreatedAt.AddSeconds(2) : null,
            result);
    }

    private static JsonDocument Serialize(
        DocumentProcessingAttemptStatusResponse response)
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
}
