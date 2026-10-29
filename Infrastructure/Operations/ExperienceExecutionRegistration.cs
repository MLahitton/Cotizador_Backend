using Application.PreQuotes.ProcessRequirement;
using Infrastructure.DocumentProcessing;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Operations;

public static class ExperienceExecutionRegistration
{
    public static IServiceCollection AddExperienceExecutionHost(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ExperienceExecutionHostOptions>(configuration.GetSection(ExperienceExecutionHostOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IExperienceCandidateReader, ExperienceCandidateReader>();
        services.AddScoped<IExperienceInitialExecution, ExperienceInitialExecution>();
        services.AddScoped<IExperienceAuthorizationStore, ExperienceAuthorizationStore>();
        services.AddScoped<ExperienceAdmissionService>();
        services.AddScoped<IInitialLeaseClaimStore, InitialLeaseClaimStore>();
        services.AddScoped<ClaimInitialLeaseService>();
        services.AddScoped<IAssignedJobStartStore, AssignedJobStartStore>();
        services.AddScoped<StartAssignedJobService>();
        services.AddScoped<ExecutionDocumentPreparationStore>();
        services.AddScoped<IExecutionDocumentPreparation>(sp => sp.GetRequiredService<ExecutionDocumentPreparationStore>());
        services.AddScoped<IExecutionDocumentConsumptionAuthorization>(sp => sp.GetRequiredService<ExecutionDocumentPreparationStore>());
        services.AddScoped<IVerifiedExecutionDocumentClient, SingleRequestAi2Client>();
        services.AddScoped<ConsumeExecutionDocumentsService>();
        services.AddScoped(sp => new RequirementExtractionMapper(sp.GetRequiredService<ILogger<RequirementExtractionMapper>>()));
        services.AddScoped<PrepareJobPublicationService>();
        services.AddScoped<IJobPublicationStore, JobPublicationStore>();
        services.AddScoped<PublishJobResultService>();
        services.AddScoped<IJobTerminationStore, JobTerminationStore>();
        services.AddScoped<JobTerminationService>();
        services.AddScoped<CoordinateAssignedExecutionService>();
        services.AddScoped<IAssignedExecutionObservationStore, AssignedExecutionObservationStore>();
        services.AddScoped<ObserveAssignedExecutionService>();
        services.AddScoped<IActiveLeaseRenewalStore, ActiveLeaseRenewalStore>();
        services.AddScoped<RenewActiveLeaseService>();
        services.AddScoped<SuperviseAssignedExecutionService>();
        services.AddHostedService<ExperienceExecutionHost>();
        return services;
    }
}
