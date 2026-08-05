using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Application.Catalogs.GetGlassTypesCatalog;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Domain.Catalogs;
using Domain.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class GlassTypesCatalogHttpTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ReturnsTypedCamelCaseCatalog()
    {
        await using var host = await ControlledHost.StartAsync(true);

        using var response = await host.Client.GetAsync(
            "/api/v1/catalogs/glass-types",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith(
            "application/json",
            response.Content.Headers.ContentType?.ToString());
        var raw = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(raw);
        var items = json.RootElement.GetProperty("items");
        Assert.Equal(9, items.GetArrayLength());
        Assert.Equal(
            [
                "LAM_4_4",
                "LAM_4_4_GRAY",
                "LAM_5_5",
                "LAM_5_5_GRAY",
                "TEMP_10",
                "TEMP_5",
                "TEMP_6",
                "TEMP_8",
                "UNKNOWN_GLASS"
            ],
            items.EnumerateArray().Select(value =>
                value.GetProperty("code").GetString()));
        foreach (var item in items.EnumerateArray())
        {
            var code = item.GetProperty("code").GetString();
            var range = item.GetProperty("currentPriceRange");
            if (code is "LAM_4_4_GRAY" or "LAM_5_5_GRAY" or "UNKNOWN_GLASS")
            {
                Assert.Equal(JsonValueKind.Null, range.ValueKind);
            }
            else
            {
                Assert.Equal(
                    JsonValueKind.Number,
                    range.GetProperty("minimumPricePerSquareMeter").ValueKind);
                Assert.Equal(
                    JsonValueKind.Number,
                    range.GetProperty("expectedAmountPerM2").ValueKind);
                Assert.Equal("COP", range.GetProperty("currency").GetString());
                Assert.Equal(
                    "PRELIMINARY",
                    range.GetProperty("status").GetString());
            }
            Assert.False(item.TryGetProperty("createdAtUtc", out _));
            Assert.False(item.TryGetProperty("priceRangeVersions", out _));
        }
    }

    [Fact]
    public async Task Get_Unauthenticated_ReturnsUnauthorized()
    {
        await using var host = await ControlledHost.StartAsync(false);

        using var response = await host.Client.GetAsync(
            "/api/v1/catalogs/glass-types",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class ControlledHost : IAsyncDisposable
    {
        private ControlledHost(WebApplication application, HttpClient client)
        {
            Application = application;
            Client = client;
        }

        public WebApplication Application { get; }
        public HttpClient Client { get; }

        public static async Task<ControlledHost> StartAsync(
            bool authenticated)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName = typeof(GlassTypesCatalogController)
                        .Assembly.GetName().Name,
                    EnvironmentName = "Testing"
                });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var currentUser = Substitute.For<ICurrentUser>();
            var identity = Substitute.For<IIdentityRepository>();
            var repository = Substitute.For<IGlassTypeCatalogRepository>();
            currentUser.IsAuthenticated.Returns(authenticated);
            currentUser.UserId.Returns(UserId);
            identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
                .Returns(User.CreateFromGoogle(
                    "user@example.com", "Test", "User", null, At));
            repository.GetActiveWithCurrentPriceRangesAsync(
                    Arg.Any<CancellationToken>())
                .Returns(Items());
            builder.Services
                .AddControllers()
                .AddApplicationPart(
                    typeof(GlassTypesCatalogController).Assembly);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(currentUser);
            builder.Services.AddSingleton(identity);
            builder.Services.AddSingleton(repository);
            builder.Services.AddScoped<GetGlassTypesCatalogService>();
            var application = builder.Build();
            application.UseRouting();
            application.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(
                        ClaimTypes.NameIdentifier,
                        UserId.ToString())],
                    "Test"));
                await next(context);
            });
            application.UseAuthorization();
            application.MapControllers();
            var started = false;
            HttpClient? client = null;
            try
            {
                await application.StartAsync();
                started = true;
                var addresses = application.Services
                    .GetRequiredService<IServer>()
                    .Features.Get<IServerAddressesFeature>()
                    ?.Addresses;
                Assert.NotNull(addresses);
                client = new HttpClient
                {
                    BaseAddress = new Uri(Assert.Single(addresses))
                };
                return new(application, client);
            }
            catch (Exception originalException)
            {
                try { client?.Dispose(); } catch { }
                try
                {
                    if (started)
                    {
                        await application.StopAsync(
                            TestContext.Current.CancellationToken);
                    }
                }
                catch { }
                finally
                {
                    try { await application.DisposeAsync(); } catch { }
                }
                ExceptionDispatchInfo.Capture(originalException).Throw();
                throw new InvalidOperationException("Unreachable.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Application.StopAsync();
            await Application.DisposeAsync();
        }

        private static IReadOnlyList<GlassTypeCatalogReadModel> Items() =>
        [
            Item("UNKNOWN_GLASS"),
            Item("TEMP_8", 90000m, 90000m, 90000m),
            Item("TEMP_6", 86000m, 86000m, 86000m),
            Item("TEMP_5", 74000m, 74000m, 74000m),
            Item("TEMP_10", 126000m, 126000m, 126000m),
            Item("LAM_5_5_GRAY"),
            Item("LAM_5_5", 120000m, 130000m, 140000m),
            Item("LAM_4_4_GRAY"),
            Item("LAM_4_4", 90000m, 100000m, 110000m)
        ];

        private static GlassTypeCatalogReadModel Item(
            string code,
            decimal? minimum = null,
            decimal? expected = null,
            decimal? maximum = null) =>
            new(
                Guid.NewGuid(), code, code, null, true,
                minimum is null || expected is null || maximum is null
                    ? null
                    : new GlassPriceRangeCatalogReadModel(
                        Guid.NewGuid(), 1, minimum.Value, expected.Value,
                        maximum.Value, "COP",
                        GlassPriceRangeStatus.Preliminary, At, null));
    }
}
