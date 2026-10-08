using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.Identity;
using FluentValidation;

namespace Application.Projects.GetProjectWorkspace;

public sealed class GetProjectWorkspaceService(
    IValidator<GetProjectWorkspaceQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IPreQuoteRepository preQuoteRepository,
    IRequirementRepository requirementRepository)
{
    public async Task<GetProjectWorkspaceResult> ExecuteAsync(
        GetProjectWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.InactiveUser);
        }

        Domain.Projects.Project? project;

        try
        {
            project = await projectRepository.FindByIdAsync(
                query.ProjectId,
                cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.QueryError);
        }

        if (project is null)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.NotFound);
        }

        if (user.Role != UserRole.Admin
            && project.CreatedByUserId != userId)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.NotFound);
        }

        Domain.Clients.Client? client;

        try
        {
            client = await clientRepository.FindByIdAsync(
                project.ClientId,
                cancellationToken);
        }
        catch (ClientQueryException)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.QueryError);
        }

        if (client is null)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.NotFound);
        }

        IReadOnlyList<ProjectWorkspacePreQuoteCandidate> preQuotes;

        try
        {
            preQuotes =
                await preQuoteRepository.ListWorkspaceCandidatesByProjectIdAsync(
                    project.Id,
                    limit: 2,
                    cancellationToken);
        }
        catch (PreQuoteQueryException)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.QueryError);
        }

        var workflow = await ResolveWorkflowAsync(
            preQuotes,
            cancellationToken);

        if (workflow is null)
        {
            return GetProjectWorkspaceResult.Failed(
                GetProjectWorkspaceFailure.QueryError);
        }

        return GetProjectWorkspaceResult.Success(
            new ProjectWorkspaceResult(
                new ProjectWorkspaceProjectResult(
                    project.Id,
                    project.ClientId,
                    project.Code,
                    project.Name,
                    project.Location,
                    project.IsActive,
                    project.CreatedAtUtc,
                    project.UpdatedAtUtc),
                new ProjectWorkspaceClientResult(
                    client.Id,
                    client.ClientType.ToString(),
                    client.LegalName,
                    client.TradeName,
                    client.Email,
                    client.Phone,
                    client.City),
                workflow));
    }

    private async Task<ProjectWorkspaceWorkflowResult?> ResolveWorkflowAsync(
        IReadOnlyList<ProjectWorkspacePreQuoteCandidate> preQuotes,
        CancellationToken cancellationToken)
    {
        if (preQuotes.Count == 0)
        {
            return EmptyWorkflow(ProjectWorkspaceResolutionState.Empty);
        }

        if (preQuotes.Count > 1)
        {
            return EmptyWorkflow(ProjectWorkspaceResolutionState.Ambiguous);
        }

        var preQuoteId = preQuotes[0].Id;
        CurrentRequirementReadModel? requirement;

        try
        {
            requirement = await requirementRepository.GetCurrentByPreQuoteIdAsync(
                preQuoteId,
                cancellationToken);
        }
        catch (RequirementQueryException)
        {
            return null;
        }

        if (requirement is null)
        {
            return new ProjectWorkspaceWorkflowResult(
                ProjectWorkspaceResolutionState.Resolved,
                preQuoteId,
                RequirementId: null,
                RequirementStatus: null,
                TechnicalProposalId: null,
                HasTechnicalProposal: false);
        }

        return new ProjectWorkspaceWorkflowResult(
            ProjectWorkspaceResolutionState.Resolved,
            preQuoteId,
            requirement.RequirementId,
            requirement.Status.ToString().ToUpperInvariant(),
            requirement.TechnicalProposalId,
            requirement.HasTechnicalProposal);
    }

    private static ProjectWorkspaceWorkflowResult EmptyWorkflow(
        ProjectWorkspaceResolutionState resolutionState) =>
        new(
            resolutionState,
            PreQuoteId: null,
            RequirementId: null,
            RequirementStatus: null,
            TechnicalProposalId: null,
            HasTechnicalProposal: false);
}
