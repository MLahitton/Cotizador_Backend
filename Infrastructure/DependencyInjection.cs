using System.Net.Http.Headers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Infrastructure.Authentication;
using Infrastructure.DocumentProcessing;
using Infrastructure.HistoricalPricing;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        CotizadorAuthenticationOptions authenticationOptions)
    {
        var fileStorageOptions =
            FileStorageOptions.FromConfiguration(configuration);

        var cotizadorAiOptions =
            CotizadorAiOptions.FromConfiguration(configuration);
        var cotizadorAi2Options =
            CotizadorAi2Options.FromConfiguration(configuration);
        var documentProcessingOptions =
            DocumentProcessingOptions.FromConfiguration(configuration);
        var historicalPricingOptions =
            HistoricalPricingOptions.FromConfiguration(configuration);
        var ai2SimilarityOptions =
            Ai2SimilarityOptions.FromConfiguration(configuration);
        var historicalSimilarityBatchOptions =
            new HistoricalSimilarityBatchOptions(
                ReadPositiveInt(
                    configuration["CotizadorAi2:SimilarityMaxItemGroupsPerBatch"],
                    HistoricalSimilarityBatchOptions.Default.MaxItemGroupsPerBatch),
                ReadPositiveInt(
                    configuration["CotizadorAi2:SimilarityMaxCandidatesPerBatch"],
                    HistoricalSimilarityBatchOptions.Default.MaxCandidatesPerBatch));

        var connectionString = configuration.GetConnectionString(
            "DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "La cadena de conexión 'DefaultConnection' no está configurada.");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddSingleton(authenticationOptions.Google);
        services.AddSingleton(authenticationOptions.Jwt);
        services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
        services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();
        services.AddSingleton(fileStorageOptions);
        services.AddSingleton(cotizadorAiOptions);
        services.AddSingleton(cotizadorAi2Options);
        services.AddSingleton(documentProcessingOptions);
        services.AddSingleton<Ai2RequirementExtractionAdapter>();
        services.AddSingleton<LegacyDocumentProcessingResponseAdapter>();
        services.AddSingleton(historicalPricingOptions);
        services.AddSingleton(ai2SimilarityOptions);
        services.AddSingleton(historicalSimilarityBatchOptions);
        services.AddSingleton<HistoricalWorkbookReader>();
        services.AddSingleton<IHistoricalQuoteCorpus, HistoricalQuoteCorpus>();
        services.AddSingleton<IHistoricalComparableCandidateService,
            HistoricalComparableCandidateService>();
        services.AddTransient<
            Application.Common.Abstractions.HistoricalPricing.IHistoricalSimilarityEvaluationService,
            Application.HistoricalPricing.EvaluateHistoricalSimilarityService>();
        services.AddTransient<
            Application.Common.Abstractions.HistoricalPricing.IHistoricalTechnicalPriceEstimator,
            Application.HistoricalPricing.HistoricalTechnicalPriceEstimator>();
        services.AddTransient<
            Application.Common.Abstractions.HistoricalPricing.IHistoricalCommercialPriceEstimator,
            Application.HistoricalPricing.HistoricalCommercialPriceEstimator>();
        services.AddSingleton<
            Application.Common.Abstractions.HistoricalPricing.IRequirementElementToHistoricalPricingMapper,
            Application.HistoricalPricing.RequirementElementToHistoricalPricingMapper>();
        services.AddTransient<
            Application.Common.Abstractions.HistoricalPricing.IPriceRequirementElementService,
            Application.HistoricalPricing.PriceRequirementElementService>();
        services.AddTransient<
            Application.Common.Abstractions.HistoricalPricing.IPriceRequirementExtractionService,
            Application.HistoricalPricing.PriceRequirementExtractionService>();
        services.AddHttpClient<Ai2SimilarityClient>(httpClient =>
        {
            httpClient.Timeout = TimeSpan.FromSeconds(100);
        });
        services.AddTransient<IAi2SimilarityClient>(serviceProvider =>
            serviceProvider.GetRequiredService<Ai2SimilarityClient>());
        services.AddSingleton<
            IDocumentProcessingDiagnostics,
            DocumentProcessingDiagnostics>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddHttpClient<CotizadorAiDocumentProcessingClient>(
            (serviceProvider, httpClient) =>
            {
                var options =
                    serviceProvider.GetRequiredService<CotizadorAiOptions>();

                httpClient.BaseAddress = options.BaseUri;
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/json"));
            });
        services.AddTransient<ILegacyDocumentProcessingClient>(
            serviceProvider => serviceProvider.GetRequiredService<
                CotizadorAiDocumentProcessingClient>());
        services.AddHttpClient<CotizadorAi2DocumentProcessingClient>(
            (serviceProvider, httpClient) =>
            {
                var options = serviceProvider
                    .GetRequiredService<CotizadorAi2Options>();
                httpClient.BaseAddress = options.BaseUri;
                httpClient.Timeout = Timeout.InfiniteTimeSpan;
                httpClient.DefaultRequestHeaders.Accept.Clear();
                httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            });
        services.AddTransient<IAi2DocumentProcessingClient>(
            serviceProvider => serviceProvider.GetRequiredService<
                CotizadorAi2DocumentProcessingClient>());
        services.AddTransient<
            IDocumentProcessingClient,
            DocumentProcessingProviderClient>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<
            IGlassTypeCatalogRepository,
            GlassTypeCatalogRepository>();
        services.AddScoped<
            IProductSystemCatalogRepository,
            ProductSystemCatalogRepository>();
        services.AddScoped<
            IFrameTypeCatalogRepository,
            FrameTypeCatalogRepository>();
        services.AddScoped<
            IFinishTypeCatalogRepository,
            FinishTypeCatalogRepository>();
        services.AddScoped<ICatalogAliasRepository, CatalogAliasRepository>();
        services.AddScoped<
            IDocumentProcessingRepository,
            DocumentProcessingRepository>();
        services.AddScoped<IRequirementRepository, RequirementRepository>();
        services.AddScoped<IPreQuoteRepository, PreQuoteRepository>();
        services.AddScoped<
            IPreQuoteDocumentQueryRepository,
            PreQuoteDocumentQueryRepository>();
        services.AddScoped<
            IPreQuoteStoredDocumentRepository,
            PreQuoteStoredDocumentRepository>();
        services.AddScoped<IPreQuoteDraftRepository, PreQuoteDraftRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();

        return services;
    }

    private static int ReadPositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
}
