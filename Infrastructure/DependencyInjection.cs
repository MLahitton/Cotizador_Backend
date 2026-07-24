using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Infrastructure.Authentication;
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
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IIdentityRepository, IdentityRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IPreQuoteRepository, PreQuoteRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();

        return services;
    }
}
