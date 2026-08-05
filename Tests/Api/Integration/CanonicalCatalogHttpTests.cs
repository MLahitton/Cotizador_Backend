using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Application.Catalogs.GetCanonicalCatalog;
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

public sealed class CanonicalCatalogHttpTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ReturnsCanonicalCatalogWithoutPrices()
    {
        await using var host = await ControlledHost.StartAsync(true);

        using var response = await host.Client.GetAsync(
            "/api/v1/catalogs/canonical",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(raw);
        var systems = json.RootElement.GetProperty("systems");
        var frames = json.RootElement.GetProperty("frames");
        var finishes = json.RootElement.GetProperty("finishes");
        var aliases = json.RootElement.GetProperty("aliases");

        Assert.Equal(13, systems.GetArrayLength());
        Assert.Equal(2, frames.GetArrayLength());
        Assert.Equal(5, finishes.GetArrayLength());
        Assert.Contains(systems.EnumerateArray(), value =>
            value.GetProperty("code").GetString() == "BARANDA"
            && !value.GetProperty("priceable").GetBoolean()
            && value.GetProperty("activeForRecognition").GetBoolean());
        Assert.Contains(systems.EnumerateArray(), value =>
            value.GetProperty("code").GetString() == "DIVISION_BANO"
            && !value.GetProperty("priceable").GetBoolean()
            && value.GetProperty("futurePriceable").GetBoolean());
        Assert.Contains(aliases.EnumerateArray(), value =>
            value.GetProperty("normalizedAlias").GetString()
                == "VENECIA SERIE 40"
            && value.GetProperty("canonicalCode").GetString() == "K40");
        Assert.Contains(aliases.EnumerateArray(), value =>
            value.GetProperty("normalizedAlias").GetString() == "SG0047"
            && value.GetProperty("canonicalCode").GetString() == "MARCO_47");
        Assert.DoesNotContain(aliases.EnumerateArray(), value =>
            value.GetProperty("normalizedAlias").GetString() == "V40");
        Assert.False(json.RootElement.TryGetProperty("glassTypes", out _));
        Assert.DoesNotContain(raw, "priceRange", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_Unauthenticated_ReturnsUnauthorized()
    {
        await using var host = await ControlledHost.StartAsync(false);

        using var response = await host.Client.GetAsync(
            "/api/v1/catalogs/canonical",
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

        private WebApplication Application { get; }
        public HttpClient Client { get; }

        public static async Task<ControlledHost> StartAsync(bool authenticated)
        {
            var builder = WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    ApplicationName = typeof(CanonicalCatalogController)
                        .Assembly.GetName().Name,
                    EnvironmentName = "Testing"
                });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var currentUser = Substitute.For<ICurrentUser>();
            var identity = Substitute.For<IIdentityRepository>();
            var systems = Substitute.For<IProductSystemCatalogRepository>();
            var frames = Substitute.For<IFrameTypeCatalogRepository>();
            var finishes = Substitute.For<IFinishTypeCatalogRepository>();
            var aliases = Substitute.For<ICatalogAliasRepository>();
            currentUser.IsAuthenticated.Returns(authenticated);
            currentUser.UserId.Returns(UserId);
            identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
                .Returns(User.CreateFromGoogle(
                    "user@example.com", "Test", "User", null, At));
            systems.ListActiveAsync(Arg.Any<CancellationToken>())
                .Returns(Systems());
            frames.ListActiveAsync(Arg.Any<CancellationToken>())
                .Returns(Frames());
            finishes.ListActiveAsync(Arg.Any<CancellationToken>())
                .Returns(Finishes());
            aliases.ListActiveAsync(Arg.Any<CancellationToken>())
                .Returns(Aliases());
            builder.Services
                .AddControllers()
                .AddApplicationPart(
                    typeof(CanonicalCatalogController).Assembly);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(currentUser);
            builder.Services.AddSingleton(identity);
            builder.Services.AddSingleton(systems);
            builder.Services.AddSingleton(frames);
            builder.Services.AddSingleton(finishes);
            builder.Services.AddSingleton(aliases);
            builder.Services.AddScoped<GetCanonicalCatalogService>();
            var application = builder.Build();
            application.UseRouting();
            application.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())],
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

        private static IReadOnlyList<ProductSystemCatalogReadModel> Systems() =>
        [
            System("3890"), System("BARANDA", false, true),
            System("DIVISION_BANO", false, true), System("K100"),
            System("K40"), System("K50"), System("K55"), System("K70"),
            System("K90"), System("S35"), System("S50"), System("S80"),
            System("SG45")
        ];

        private static ProductSystemCatalogReadModel System(
            string code,
            bool priceable = true,
            bool requiresReview = false) =>
            new(Guid.NewGuid(), code, code, true, priceable, true,
                requiresReview, true);

        private static IReadOnlyList<FrameTypeCatalogReadModel> Frames() =>
        [
            new(Guid.NewGuid(), "MARCO_47", "Marco 47 mm", true),
            new(Guid.NewGuid(), "MARCO_58", "Marco 58 mm", true)
        ];

        private static IReadOnlyList<FinishTypeCatalogReadModel> Finishes() =>
        [
            new(Guid.NewGuid(), "STANDARD_NATURAL", "Natural", false, true),
            new(Guid.NewGuid(), "ANODIZED_GRAY", "Gray", false, true),
            new(Guid.NewGuid(), "BLACK_MATTE", "Black", false, true),
            new(Guid.NewGuid(), "SPECIAL", "Special", true, true),
            new(Guid.NewGuid(), "UNKNOWN", "Unknown", true, true)
        ];

        private static IReadOnlyList<CatalogAliasReadModel> Aliases() =>
        [
            Alias(CatalogAliasCategory.System, "VENECIA SERIE 40", "K40"),
            Alias(CatalogAliasCategory.System, "VENECIA SERIE 50", "K50"),
            Alias(CatalogAliasCategory.System, "VENECIA SERIE 70", "K70"),
            Alias(CatalogAliasCategory.Frame, "SG0047", "MARCO_47"),
            Alias(CatalogAliasCategory.Frame, "SG0058", "MARCO_58"),
            Alias(CatalogAliasCategory.Finish, "NEGRO MATE", "BLACK_MATTE")
        ];

        private static CatalogAliasReadModel Alias(
            CatalogAliasCategory category,
            string alias,
            string code) => new(
            Guid.NewGuid(),
            category,
            alias,
            CatalogAliasNormalizer.Normalize(alias),
            code,
            CatalogAliasMatchPolicy.TechnicalPhrase,
            category == CatalogAliasCategory.System,
            1.0m,
            true);
    }
}
