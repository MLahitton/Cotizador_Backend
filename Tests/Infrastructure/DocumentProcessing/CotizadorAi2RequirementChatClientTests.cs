using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Application.Common.Abstractions.PreQuotes;
using Infrastructure.DocumentProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class CotizadorAi2RequirementChatClientTests
{
    [Fact]
    public async Task InterpretActionAsync_WithRequirementScope_SendsAi2Contract()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK, CreateIntentJson());
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        await client.InterpretActionAsync(
            new RequirementChatActionInterpretationRequest(
                "Dime que items les falta algun dato para sacar el precio",
                "REQUIREMENT",
                null,
                [
                    new RequirementChatAiConversationMessage(
                        "user",
                        "Hola")
                ],
                new { requirementId = Guid.NewGuid() }),
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(handler.Body);
        var root = document.RootElement;
        Assert.Equal("/chat/actions/interpret", handler.RequestUri?.AbsolutePath);
        Assert.Equal("REQUIREMENT", root.GetProperty("scope").GetString());
        Assert.Equal(
            "Dime que items les falta algun dato para sacar el precio",
            root.GetProperty("userMessage").GetString());
        Assert.True(root.TryGetProperty("conversation", out var conversation));
        Assert.Equal(1, conversation.GetArrayLength());
        Assert.True(root.TryGetProperty("context", out _));
        Assert.False(root.TryGetProperty("message", out _));
        Assert.False(root.TryGetProperty("technicalProposalItemId", out _));
    }

    [Fact]
    public async Task InterpretActionAsync_WithItemScope_SendsAi2Contract()
    {
        var itemId = Guid.NewGuid();
        var handler = new CaptureHandler(HttpStatusCode.OK, CreateIntentJson(
            isAction: true,
            actionType: "CHANGE_FINISH",
            scope: "ITEM",
            requestedValue: "inox",
            rawUserMessage: "ponlo en inox"));
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient);

        var intent = await client.InterpretActionAsync(
            new RequirementChatActionInterpretationRequest(
                "ponlo en inox",
                "ITEM",
                itemId,
                [],
                new { item = new { itemId } }),
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(handler.Body);
        var root = document.RootElement;
        Assert.True(intent.IsAction);
        Assert.Equal("CHANGE_FINISH", intent.ActionType);
        Assert.Equal("ITEM", root.GetProperty("scope").GetString());
        Assert.Equal("ponlo en inox", root.GetProperty("userMessage").GetString());
        Assert.False(root.TryGetProperty("message", out _));
        Assert.False(root.TryGetProperty("technicalProposalItemId", out _));
    }

    [Fact]
    public async Task InterpretActionAsync_WithLegacyBackendShape_WouldMissAi2RequiredUserMessage()
    {
        var json = JsonSerializer.Serialize(
            new
            {
                message = "ponlo en inox",
                scope = "ITEM",
                technicalProposalItemId = Guid.NewGuid(),
                conversation = Array.Empty<object>(),
                context = new { }
            },
            JsonSerializerOptions.Web);

        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("message", out _));
        Assert.True(document.RootElement.TryGetProperty("technicalProposalItemId", out _));
        Assert.False(document.RootElement.TryGetProperty("userMessage", out _));
    }

    [Fact]
    public async Task InterpretActionAsync_WithUnprocessableEntity_LogsAi2Detail()
    {
        var logger = new CapturingLogger();
        var detail = """
            {"detail":[{"loc":["body","userMessage"],"msg":"Field required","type":"missing"}]}
            """;
        var handler = new CaptureHandler(HttpStatusCode.UnprocessableEntity, detail);
        using var httpClient = CreateHttpClient(handler);
        var client = CreateClient(httpClient, logger);

        var exception = await Assert.ThrowsAsync<RequirementChatAiUnavailableException>(
            () => client.InterpretActionAsync(
                new RequirementChatActionInterpretationRequest(
                    "hola",
                    "REQUIREMENT",
                    null,
                    [],
                    new { }),
                TestContext.Current.CancellationToken));

        Assert.IsType<InvalidDataException>(exception.InnerException);
        Assert.Contains("StatusCode=422", logger.Message);
        Assert.Contains("/chat/actions/interpret", logger.Message);
        Assert.Contains("userMessage", logger.Message);
        Assert.Contains("Field required", logger.Message);
    }

    private static CotizadorAi2RequirementChatClient CreateClient(
        HttpClient httpClient,
        ILogger<CotizadorAi2RequirementChatClient>? logger = null) => new(
        httpClient,
        new CotizadorAi2Options(
            new Uri("http://localhost/"), 30, 1_000_000, true),
        logger ?? NullLogger<CotizadorAi2RequirementChatClient>.Instance);

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("http://localhost/")
    };

    private static string CreateIntentJson(
        bool isAction = false,
        string actionType = "UNKNOWN",
        string scope = "REQUIREMENT",
        string? requestedValue = null,
        string rawUserMessage = "hola") =>
        $$"""
        {
          "isAction": {{JsonSerializer.Serialize(isAction)}},
          "actionType": "{{actionType}}",
          "scope": "{{scope}}",
          "targetReference": null,
          "requestedValue": {{JsonSerializer.Serialize(requestedValue)}},
          "requestedQuantity": null,
          "requestedWidthMm": null,
          "requestedHeightMm": null,
          "confidence": 0.82,
          "requiresClarification": false,
          "clarificationReason": null,
          "rawUserMessage": "{{rawUserMessage}}"
        }
        """;

    private sealed class CaptureHandler(
        HttpStatusCode statusCode,
        string responseBody)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    new MediaTypeHeaderValue("application/json"))
            };
        }
    }

    private sealed class CapturingLogger :
        ILogger<CotizadorAi2RequirementChatClient>
    {
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Message = formatter(state, exception);
        }
    }
}
