using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.HistoricalPricing;
using Application.Authentication.GetCurrentUser;
using Application.Authentication.GoogleSignIn;
using Application.Clients.CreateClient;
using Application.Clients.GetClientById;
using Application.Clients.GetClients;
using Application.Clients.SetClientActivation;
using Application.Clients.UpdateClient;
using Application.Catalogs.GetGlassTypesCatalog;
using Application.Catalogs.GetCanonicalCatalog;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.GetDocumentProcessingAttempt;
using Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;
using Application.PreQuotes.ProcessRequirement;
using Application.PreQuotes.PriceRequirementTechnicalProposal;
using Application.PreQuotes.ResolveHistoricalTechnicalEvidence;
using Application.PreQuotes.CreatePreQuote;
using Application.PreQuotes.CreatePreQuoteDocument;
using Application.PreQuotes.GetPreQuoteById;
using Application.PreQuotes.GetProjectPreQuotes;
using Application.PreQuotes.UpdatePreQuoteName;
using Application.PreQuotes.GetPreQuoteDocuments;
using Application.PreQuotes.GetStructuredDocumentExtraction;
using Application.PreQuotes.CreatePreQuoteDraft;
using Application.PreQuotes.GetPreQuoteDraft;
using Application.PreQuotes.UpdatePreQuoteDraft;
using Application.PreQuotes.ApprovePreQuoteDraft;
using Application.PreQuotes.BuildRequirementTechnicalProposal;
using Application.PreQuotes.ConfirmRequirementTechnicalProposalSelection;
using Application.PreQuotes.CreateManualRequirementTechnicalProposalItem;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Application.PreQuotes.GetCurrentRequirement;
using Application.PreQuotes.GetRequirementDetails;
using Application.PreQuotes.ManageRequirementDocuments;
using Application.PreQuotes.RequirementChat;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemSelection;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemInclusion;
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
        services.AddScoped<GetGlassTypesCatalogService>();
        services.AddScoped<GetCanonicalCatalogService>();
        services.AddScoped<CreateDocumentProcessingAttemptService>();
        services.AddScoped<CreateRequirementService>();
        services.AddScoped<ProcessRequirementService>();
        services.AddScoped<CancelRequirementProcessingAttemptService>();
        services.AddScoped<PriceRequirementTechnicalProposalService>();
        services.AddScoped<GetDocumentProcessingAttemptService>();
        services.AddScoped<
            IClaimedDocumentProcessingService,
            ProcessClaimedDocumentProcessingAttemptService>();
        services.AddScoped<CreatePreQuoteService>();
        services.AddScoped<CreatePreQuoteDocumentService>();
        services.AddScoped<GetPreQuoteByIdService>();
        services.AddScoped<GetProjectPreQuotesService>();
        services.AddScoped<UpdatePreQuoteNameService>();
        services.AddScoped<GetPreQuoteDocumentsService>();
        services.AddScoped<GetStructuredDocumentExtractionService>();
        services.AddScoped<IHistoricalDocumentEstimatePipeline, HistoricalDocumentEstimatePipeline>();
        services.AddScoped<ResolveHistoricalTechnicalEvidenceService>();
        services.AddScoped<BuildRequirementTechnicalProposalService>();
        services.AddScoped<ConfirmRequirementTechnicalProposalSelectionService>();
        services.AddScoped<GetRequirementTechnicalProposalService>();
        services.AddScoped<GetCurrentRequirementService>();
        services.AddScoped<GetRequirementDetailsService>();
        services.AddScoped<GetRequirementChatService>();
        services.AddScoped<SendRequirementChatMessageService>();
        services.AddScoped<ManageRequirementDocumentsService>();
        services.AddScoped<
            UpdateRequirementTechnicalProposalItemSelectionService>();
        services.AddScoped<
            CreateManualRequirementTechnicalProposalItemService>();
        services.AddScoped<
            UpdateRequirementTechnicalProposalItemInclusionService>();
        services.AddScoped<EstimateStoredPreQuoteDocumentsService>();
        services.AddSingleton<ITechnicalProposalItemToHistoricalPricingMapper,
            TechnicalProposalItemToHistoricalPricingMapper>();
        services.AddScoped<ISgProductSystemConstraintEvaluator, SgProductSystemConstraintEvaluator>();
        services.AddScoped<ISgTechnicalSelector, DeterministicSgTechnicalSelector>();
        services.AddScoped<IGlassCandidateResolver, GlassCandidateResolver>();
        services.AddScoped<IFinishCandidateResolver, FinishCandidateResolver>();
        services.AddScoped<CreatePreQuoteDraftService>();
        services.AddScoped<GetPreQuoteDraftService>();
        services.AddScoped<UpdatePreQuoteDraftService>();
        services.AddScoped<ApprovePreQuoteDraftService>();
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
