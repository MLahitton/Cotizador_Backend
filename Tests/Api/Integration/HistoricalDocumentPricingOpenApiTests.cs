using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using Infrastructure.DocumentProcessing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class HistoricalDocumentPricingOpenApiTests
{
    [Fact]
    public async Task OpenApi_DocumentEstimate_UsesMultipartBinaryFilesOnly()
    {
        await using var host = await ControlledHost.StartAsync();

        using var response = await host.Client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(
            TestContext.Current.CancellationToken));
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/historical-pricing/document-estimate")
            .GetProperty("post");
        var content = operation.GetProperty("requestBody").GetProperty("content");

        Assert.True(content.TryGetProperty("multipart/form-data", out var multipart));
        Assert.False(content.TryGetProperty(
            "application/x-www-form-urlencoded", out _));
        var schema = ResolveSchema(document.RootElement, multipart.GetProperty("schema"));
        Assert.True(
            schema.TryGetProperty("properties", out var properties),
            schema.GetRawText());
        var files = properties.EnumerateObject()
            .Single(property => property.Name.Equals(
                "files", StringComparison.OrdinalIgnoreCase))
            .Value;
        Assert.Equal("array", files.GetProperty("type").GetString());
        var fileItem = ResolveSchema(
            document.RootElement,
            files.GetProperty("items"));
        Assert.True(fileItem.TryGetProperty("type", out var itemType), fileItem.GetRawText());
        Assert.True(fileItem.TryGetProperty("format", out var itemFormat), fileItem.GetRawText());
        Assert.Equal("string", itemType.GetString());
        Assert.Equal("binary", itemFormat.GetString());
    }

    [Fact]
    public async Task Post_WithMultipartFile_ReachesControllerWithoutUnsupportedMediaType()
    {
        await using var host = await ControlledHost.StartAsync();
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "files", "quote.pdf");

        using var response = await host.Client.PostAsync(
            "/api/v1/historical-pricing/document-estimate",
            form,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await host.Ai2.Received(1).ProcessAsync(
            Arg.Is<DocumentProcessingClientRequest>(request =>
                request != null && request.Files.Count == 1),
            Arg.Any<CancellationToken>());
    }

    private static JsonElement ResolveSchema(
        JsonElement document,
        JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }
        var name = reference.GetString()!.Split('/').Last();
        return document.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(name);
    }

    private sealed class ControlledHost : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private ControlledHost(
            WebApplication application,
            HttpClient client,
            IAi2DocumentProcessingClient ai2)
        {
            _application = application;
            Client = client;
            Ai2 = ai2;
        }

        public HttpClient Client { get; }
        public IAi2DocumentProcessingClient Ai2 { get; }

        public static async Task<ControlledHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(HistoricalDocumentPricingController)
                    .Assembly.GetName().Name,
                EnvironmentName = "Development"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var ai2 = Substitute.For<IAi2DocumentProcessingClient>();
            ai2.ProcessAsync(
                    Arg.Any<DocumentProcessingClientRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(DocumentProcessingClientResult.Failed(
                    DocumentProcessingClientFailure.ServiceUnavailable));
            var pricing = Substitute.For<IPriceRequirementExtractionService>();
            var corpus = Substitute.For<IHistoricalQuoteCorpus>();
            corpus.Current.Returns(new HistoricalCorpusSnapshot(
                true, "test", DateTimeOffset.UtcNow, [], []));

            builder.Services.AddControllers().AddApplicationPart(
                typeof(HistoricalDocumentPricingController).Assembly);
            builder.Services.AddOpenApi();
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(ai2);
            builder.Services.AddSingleton(pricing);
            builder.Services.AddSingleton(corpus);
            builder.Services.AddSingleton<IHistoricalDocumentEstimatePipeline,
                HistoricalDocumentEstimatePipeline>();

            var application = builder.Build();
            application.UseRouting();
            application.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                    "Test"));
                await next(context);
            });
            application.UseAuthorization();
            application.MapOpenApi();
            application.MapControllers();
            await application.StartAsync(TestContext.Current.CancellationToken);
            var address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new ControlledHost(
                application,
                new HttpClient { BaseAddress = new Uri(address) },
                ai2);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.StopAsync(TestContext.Current.CancellationToken);
            await _application.DisposeAsync();
        }
    }
}
