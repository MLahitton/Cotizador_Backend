using Application.Authentication.GetCurrentUser;
using Application.Authentication.GoogleSignIn;
using Application.Clients.CreateClient;
using Application.Clients.GetClientById;
using Application.Clients.GetClients;
using Application.Clients.SetClientActivation;
using Application.Clients.UpdateClient;
using Application.Projects.CreateProject;
using Application.Projects.GetClientProjects;
using Application.Projects.GetProjectById;
using Application.Projects.UpdateProject;
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
        services.AddScoped<GetClientByIdService>();
        services.AddScoped<GetClientsService>();
        services.AddScoped<SetClientActivationService>();
        services.AddScoped<UpdateClientService>();
        services.AddScoped<CreateProjectService>();
        services.AddScoped<GetClientProjectsService>();
        services.AddScoped<GetProjectByIdService>();
        services.AddScoped<UpdateProjectService>();

        return services;
    }
}
