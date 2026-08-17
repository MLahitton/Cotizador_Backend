using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Api.Controllers;
using Application.Common.Abstractions.HistoricalPricing;
using Contracts.HistoricalPricing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class HistoricalPricingValidationIntegrationTests
{
    [Fact]
    public async Task Post_WithValidDecimals_PassesMvcValidationAndReachesController()
    {
        await using var host = await ControlledHost.StartAsync();
        var request = ValidRequest();

        using var response = await host.Client.PostAsJsonAsync(
            "/api/v1/historical-pricing/technical-estimate",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await host.Estimator.Received(1).EstimateAsync(
            Arg.Is<HistoricalCandidateQuery>(query =>
                query.Area == 9.35m
                && query.GlassThickness == 6m
                && query.Width == 3740m
                && query.Height == 2500m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Post_WithNonPositiveArea_ReturnsBadRequestWithoutCallingControllerService()
    {
        await using var host = await ControlledHost.StartAsync();
        var request = ValidRequest() with { AreaM2 = 0m };

        using var response = await host.Client.PostAsJsonAsync(
            "/api/v1/historical-pricing/technical-estimate",
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await host.Estimator.DidNotReceive().EstimateAsync(
            Arg.Any<HistoricalCandidateQuery>(),
            Arg.Any<CancellationToken>());
    }

    private static HistoricalTechnicalPriceEstimateRequest ValidRequest() =>
        new("PV-06", "PUERTA", "3831", "TEMPLADO", 6m, null,
            "CORREDIZA", 3740m, 2500m, 9.35m, 1m, null);

    private sealed class ControlledHost : IAsyncDisposable
    {
        private readonly WebApplication _application;

        private ControlledHost(
            WebApplication application,
            HttpClient client,
            IHistoricalTechnicalPriceEstimator estimator)
        {
            _application = application;
            Client = client;
            Estimator = estimator;
        }

        public HttpClient Client { get; }
        public IHistoricalTechnicalPriceEstimator Estimator { get; }

        public static async Task<ControlledHost> StartAsync()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(HistoricalPricingController).Assembly.GetName().Name,
                EnvironmentName = "Testing"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var estimator = Substitute.For<IHistoricalTechnicalPriceEstimator>();
            estimator.EstimateAsync(
                    Arg.Any<HistoricalCandidateQuery>(),
                    Arg.Any<CancellationToken>())
                .Returns(CreateEstimate());
            var corpus = Substitute.For<IHistoricalQuoteCorpus>();
            corpus.Current.Returns(new HistoricalCorpusSnapshot(
                true, "test", DateTimeOffset.UtcNow, [], []));

            builder.Services.AddControllers().AddApplicationPart(
                typeof(HistoricalPricingController).Assembly);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(estimator);
            builder.Services.AddSingleton(corpus);

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
            application.MapControllers();
            await application.StartAsync(TestContext.Current.CancellationToken);
            var address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new ControlledHost(
                application,
                new HttpClient { BaseAddress = new Uri(address) },
                estimator);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.StopAsync(TestContext.Current.CancellationToken);
            await _application.DisposeAsync();
        }

        private static HistoricalTechnicalPriceEstimate CreateEstimate() =>
            new("COP", 1m, 2m, 3m, 0.5m,
                HistoricalPriceConfidenceLevel.Medium,
                "HISTORICAL_COMPARABLES", 1, 1, 0, true,
                [], [], [], []);
    }
}
