using System.Net.Http.Headers;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Infrastructure.Authentication;
using Infrastructure.DocumentProcessing;
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
        var documentProcessingWorkerOptions =
            DocumentProcessingWorkerOptions.FromConfiguration(configuration);

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
        services.AddSingleton(documentProcessingWorkerOptions);
        services.AddSingleton<
            IDocumentProcessingDiagnostics,
            DocumentProcessingDiagnostics>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddHttpClient<
            IDocumentProcessingClient,
            CotizadorAiDocumentProcessingClient>(
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
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<
            IGlassTypeCatalogRepository,
            GlassTypeCatalogRepository>();
        services.AddScoped<
            IDocumentProcessingRepository,
            DocumentProcessingRepository>();
        services.AddScoped<IPreQuoteRepository, PreQuoteRepository>();
        services.AddScoped<
            IPreQuoteDocumentQueryRepository,
            PreQuoteDocumentQueryRepository>();
        services.AddScoped<IPreQuoteDraftRepository, PreQuoteDraftRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();

        if (documentProcessingWorkerOptions.Enabled)
        {
            services.AddHostedService<DocumentProcessingWorker>();
        }

        return services;
    }
}
