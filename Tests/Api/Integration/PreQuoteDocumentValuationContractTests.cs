using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Api.Controllers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetStructuredDocumentExtraction;
using Contracts.PreQuotes;
using Domain.Catalogs;
using Domain.Identity;
using Domain.PreQuotes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Integration;

public sealed class PreQuoteDocumentValuationContractTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Mapper_MapsValuedNullAndNotValuedContracts()
    {
        var mapped = PreQuoteDocumentResponseMapper.Map(CreateExtraction());

        var valued = mapped.Items[0].Valuation;
        Assert.NotNull(valued);
        Assert.Equal("VALUED", valued.Status);
        Assert.Null(valued.Reason);
        Assert.Equal("PRELIMINARY", valued.PriceRangeStatus);
        Assert.Equal("COP", valued.Currency);
        Assert.Equal(1.500000m, valued.UnitAreaSquareMeters);
        Assert.Equal(4.500000m, valued.TotalAreaSquareMeters);
        Assert.Equal(90000.00m, valued.MinimumPricePerSquareMeter);
        Assert.Equal(110000.00m, valued.MaximumPricePerSquareMeter);
        Assert.Equal(405000.00m, valued.MinimumAmount);
        Assert.Equal(495000.00m, valued.MaximumAmount);
        Assert.Equal(TimeSpan.Zero, valued.CalculatedAtUtc.Offset);
        Assert.Null(mapped.Items[1].Valuation);
        Assert.Equal("NOT_VALUED", mapped.Items[2].Valuation!.Status);
        Assert.Equal("MISSING_QUANTITY", mapped.Items[2].Valuation!.Reason);
    }

    [Fact]
    public void Json_UsesCamelCaseAndNumericDecimals()
    {
        var response = CreateResponse(CreateExtraction());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            response, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var extraction = json.RootElement.GetProperty("structuredExtraction");
        var valuation = extraction.GetProperty("items")[0]
            .GetProperty("valuation");
        foreach (var name in new[]
        {
            "status", "reason", "glassPriceRangeVersionId",
            "priceRangeVersion", "priceRangeStatus", "currency",
            "unitAreaSquareMeters", "totalAreaSquareMeters",
            "minimumPricePerSquareMeter", "maximumPricePerSquareMeter",
            "minimumAmount", "maximumAmount", "calculatedAtUtc"
        })
        {
            Assert.True(valuation.TryGetProperty(name, out _), name);
        }
        Assert.Equal(JsonValueKind.Number,
            valuation.GetProperty("minimumAmount").ValueKind);
        var summary = extraction.GetProperty("summary");
        foreach (var name in new[]
        {
            "isAggregable", "aggregationIssue", "valuedItemCount",
            "notValuedItemCount", "totalGlassAreaSquareMeters",
            "minimumGlassAmount", "maximumGlassAmount"
        })
        {
            Assert.True(summary.TryGetProperty(name, out _), name);
        }
    }

    [Fact]
    public async Task Http_AuthorizedV3_ReturnsExactValuationsAndTotals()
    {
        await using var host = await ControlledHost.StartAsync(
            authenticated: true, active: true, CreateQuery(CreateFourItems()));
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await host.Client.GetAsync(
            $"/api/v1/prequote-documents/{DocumentId}/structured-extraction",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content
            .ReadFromJsonAsync<StructuredDocumentExtractionDetailsResponse>(
                cancellationToken);
        Assert.NotNull(body?.StructuredExtraction);
        Assert.Equal(4, body.StructuredExtraction.Items.Count);
        Assert.Equal(45.080000m,
            body.StructuredExtraction.Summary.TotalGlassAreaSquareMeters);
        Assert.Equal(5022400.00m,
            body.StructuredExtraction.Summary.MinimumGlassAmount);
        Assert.Equal(5688800.00m,
            body.StructuredExtraction.Summary.MaximumGlassAmount);
        Assert.Equal("COP", body.StructuredExtraction.Summary.Currency);
        Assert.True(body.StructuredExtraction.Summary.IsAggregable);
        Assert.Null(body.StructuredExtraction.Summary.AggregationIssue);
    }

    [Theory]
    [InlineData(false, true, true, HttpStatusCode.Unauthorized)]
    [InlineData(true, false, true, HttpStatusCode.Forbidden)]
    [InlineData(true, true, false, HttpStatusCode.NotFound)]
    public async Task Http_EnforcesCurrentSecurityContract(
        bool authenticated,
        bool active,
        bool visible,
        HttpStatusCode expected)
    {
        await using var host = await ControlledHost.StartAsync(
            authenticated, active, visible ? CreateQuery(CreateExtraction()) : null);
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await host.Client.GetAsync(
            $"/api/v1/prequote-documents/{DocumentId}/structured-extraction",
            cancellationToken);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Http_HistoricalWithoutValuation_Returns200AndNull()
    {
        var extraction = CreateExtraction() with
        {
            Items = [CreateItem(1, null)],
            Summary = Summary(0, 0, 0, null, null, null, true, null)
        };
        await using var host = await ControlledHost.StartAsync(
            true, true, CreateQuery(extraction));
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await host.Client.GetAsync(
            $"/api/v1/prequote-documents/{DocumentId}/structured-extraction",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content
            .ReadFromJsonAsync<StructuredDocumentExtractionDetailsResponse>(
                cancellationToken);
        Assert.Null(Assert.Single(body!.StructuredExtraction!.Items).Valuation);
    }

    [Fact]
    public async Task Http_MixedCurrencies_ReturnsNonAggregableSummary()
    {
        var extraction = CreateExtraction() with
        {
            Items =
            [
                CreateItem(1, Valued("COP", 100m, 200m, 1m)),
                CreateItem(2, Valued("USD", 10m, 20m, 2m))
            ],
            Summary = Summary(2, 0, 3m, null, null, null, false,
                "CURRENCY_MISMATCH")
        };
        await using var host = await ControlledHost.StartAsync(
            true, true, CreateQuery(extraction));
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await host.Client.GetAsync(
            $"/api/v1/prequote-documents/{DocumentId}/structured-extraction",
            cancellationToken);
        var body = await response.Content
            .ReadFromJsonAsync<StructuredDocumentExtractionDetailsResponse>(
                cancellationToken);
        var summary = body!.StructuredExtraction!.Summary;
        Assert.False(summary.IsAggregable);
        Assert.Equal("CURRENCY_MISMATCH", summary.AggregationIssue);
        Assert.Null(summary.Currency);
        Assert.Null(summary.MinimumGlassAmount);
        Assert.Null(summary.MaximumGlassAmount);
    }

    private static StructuredDocumentExtractionQueryReadModel CreateQuery(
        StructuredExtractionDetailsReadModel extraction) => new(
            new PreQuoteDocumentReadModel(
                DocumentId, Guid.NewGuid(), "document.pdf",
                "application/pdf", 4, At),
            DocumentProcessingAvailability.AvailableCurrent,
            null, extraction);

    private static StructuredDocumentExtractionDetailsResponse CreateResponse(
        StructuredExtractionDetailsReadModel extraction) => new(
            new PreQuoteDocumentResponse(
                DocumentId, Guid.NewGuid(), "document.pdf",
                "application/pdf", 4, At),
            "AVAILABLE_CURRENT", null,
            PreQuoteDocumentResponseMapper.Map(extraction));

    private static StructuredExtractionDetailsReadModel CreateExtraction() => new(
        Guid.NewGuid(), Guid.NewGuid(), true,
        StructuredExtractionStatus.Completed,
        new StructuredProjectReadModel("Project", "Client", "Bogota", [], []),
        [],
        [
            CreateItem(1, Valued("COP", 405000m, 495000m, 4.5m)),
            CreateItem(2, null),
            CreateItem(3, new StructuredItemGlassValuationReadModel(
                GlassValuationStatus.NotValued,
                GlassValuationReason.MissingQuantity,
                null, null, null, null, null, null, null, null, null,
                null, null, At))
        ],
        [], [], [],
        Summary(1, 1, 4.5m, 405000m, 495000m, "COP", true, null),
        new StructuredProcessingMetadataReadModel("rule_based_v2", 5), At);

    private static StructuredExtractionDetailsReadModel CreateFourItems()
    {
        var items = new[]
        {
            CreateItem(1, Valued("COP", 405000m, 495000m, 4.5m)),
            CreateItem(2, Valued("COP", 1117200m, 1117200m, 11.76m)),
            CreateItem(3, Valued("COP", 2455200m, 2864400m, 20.46m)),
            CreateItem(4, Valued("COP", 1045000m, 1212200m, 8.36m))
        };
        return CreateExtraction() with
        {
            Items = items,
            Summary = Summary(4, 0, 45.08m, 5022400m, 5688800m,
                "COP", true, null)
        };
    }

    private static StructuredItemReadModel CreateItem(
        int sequence,
        StructuredItemGlassValuationReadModel? valuation) => new(
            sequence, $"V-{sequence:00}", "Window",
            StructuredElementType.Window, "1000 x 1500 mm",
            1000, 1500, 1, false, [], [], [],
            new StructuredItemGlassReadModel(
                Guid.NewGuid(), "Laminado", "LAM_4_4",
                GlassAssignmentScope.Item, false, [], [], []),
            valuation);

    private static StructuredItemGlassValuationReadModel Valued(
        string currency,
        decimal minimum,
        decimal maximum,
        decimal area) => new(
            GlassValuationStatus.Valued, null, Guid.NewGuid(), Guid.NewGuid(),
            1, GlassPriceRangeStatus.Preliminary, currency,
            1.500000m, area, 90000m, 110000m,
            minimum, maximum, At);

    private static StructuredSummaryReadModel Summary(
        int valued,
        int notValued,
        decimal area,
        decimal? minimum,
        decimal? maximum,
        string? currency,
        bool aggregable,
        string? issue) => new(
            Math.Max(valued + notValued, 1), 0, 0, 0, 0, 0, 1, 0,
            valued, notValued, area, minimum, maximum, currency,
            aggregable, issue);

    private sealed class ControlledHost : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private ControlledHost(WebApplication application, HttpClient client)
        {
            _application = application;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<ControlledHost> StartAsync(
            bool authenticated,
            bool active,
            StructuredDocumentExtractionQueryReadModel? result)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(
                    PreQuoteDocumentStructuredExtractionController)
                    .Assembly.GetName().Name,
                EnvironmentName = "Testing"
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var currentUser = Substitute.For<ICurrentUser>();
            currentUser.IsAuthenticated.Returns(authenticated);
            currentUser.UserId.Returns(authenticated ? UserId : null);
            var identity = Substitute.For<IIdentityRepository>();
            var user = User.CreateFromGoogle(
                "user@example.com", "User", null, null, At);
            if (!active)
            {
                user.Deactivate(At.AddMinutes(1));
            }
            identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
                .Returns(user);
            var repository = Substitute.For<IPreQuoteDocumentQueryRepository>();
            repository.GetStructuredExtractionAsync(
                    DocumentId, UserId, Arg.Any<CancellationToken>())
                .Returns(result);
            builder.Services.AddControllers().AddApplicationPart(
                typeof(PreQuoteDocumentStructuredExtractionController)
                    .Assembly);
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(currentUser);
            builder.Services.AddSingleton(identity);
            builder.Services.AddSingleton(repository);
            builder.Services.AddScoped<
                GetStructuredDocumentExtractionQueryValidator>();
            builder.Services.AddScoped<FluentValidation.IValidator<
                GetStructuredDocumentExtractionQuery>>(services =>
                services.GetRequiredService<
                    GetStructuredDocumentExtractionQueryValidator>());
            builder.Services.AddScoped<GetStructuredDocumentExtractionService>();
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
            try
            {
                await application.StartAsync();
                var address = application.Services
                    .GetRequiredService<IServer>().Features
                    .Get<IServerAddressesFeature>()!.Addresses.Single();
                return new ControlledHost(application,
                    new HttpClient { BaseAddress = new Uri(address) });
            }
            catch
            {
                await application.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                await _application.StopAsync();
            }
            finally
            {
                await _application.DisposeAsync();
            }
        }
    }
}
