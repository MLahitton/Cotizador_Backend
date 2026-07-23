using Application.Authentication.GetCurrentUser;
using Application.Authentication.GoogleSignIn;
using Application.Clients.CreateClient;
using Application.Clients.GetClients;
using Application.Projects.CreateProject;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(
            typeof(DependencyInjection).Assembly);

        services.AddScoped<GoogleSignInService>();
        services.AddScoped<GetCurrentUserService>();
        services.AddScoped<CreateClientService>();
        services.AddScoped<GetClientsService>();
        services.AddScoped<CreateProjectService>();

        return services;
    }
}
