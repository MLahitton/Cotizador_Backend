using Application.Authentication.GetCurrentUser;
using Application.Authentication.GoogleSignIn;
using Application.Clients.CreateClient;
using Application.Clients.GetClientById;
using Application.Clients.GetClients;
using Application.Clients.SetClientActivation;
using Application.Clients.UpdateClient;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Application.PreQuotes.ClaimDocumentProcessingAttempt;
using Application.PreQuotes.GetDocumentProcessingAttempt;
using Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;
using Application.PreQuotes.CreatePreQuote;
using Application.PreQuotes.CreatePreQuoteDocument;
using Application.PreQuotes.GetPreQuoteById;
using Application.PreQuotes.GetProjectPreQuotes;
using Application.PreQuotes.GetPreQuoteDocuments;
using Application.PreQuotes.GetStructuredDocumentExtraction;
using Application.Projects.CreateProject;
using Application.Projects.GetClientProjects;
using Application.Projects.GetProjectById;
using Application.Projects.GetProjects;
using Application.Projects.SetProjectActivation;
using Application.Projects.UpdateProject;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<CreateDocumentProcessingAttemptService>();
        services.AddScoped<GetDocumentProcessingAttemptService>();
        services.AddScoped<
            IDocumentProcessingClaimService,
            ClaimNextDocumentProcessingAttemptService>();
        services.AddScoped<
            IClaimedDocumentProcessingService,
            ProcessClaimedDocumentProcessingAttemptService>();
        services.AddScoped<CreatePreQuoteService>();
        services.AddScoped<CreatePreQuoteDocumentService>();
        services.AddScoped<GetPreQuoteByIdService>();
        services.AddScoped<GetProjectPreQuotesService>();
        services.AddScoped<GetPreQuoteDocumentsService>();
        services.AddScoped<GetStructuredDocumentExtractionService>();
        services.AddScoped<CreateProjectService>();
        services.AddScoped<GetClientProjectsService>();
        services.AddScoped<GetProjectByIdService>();
        services.AddScoped<GetProjectsService>();
        services.AddScoped<SetProjectActivationService>();
        services.AddScoped<UpdateProjectService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
