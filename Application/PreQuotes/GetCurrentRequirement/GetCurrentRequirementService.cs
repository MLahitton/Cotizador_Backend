using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;

namespace Application.PreQuotes.GetCurrentRequirement;

public sealed record GetCurrentRequirementCommand(Guid PreQuoteId);

public enum GetCurrentRequirementFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    PreQuoteNotFound,
    ProjectNotFound,
    InactiveProject,
    ClientNotFound,
    InactiveClient,
    CurrentRequirementNotFound,
    QueryError
}

public sealed record GetCurrentRequirementResult(
    bool IsSuccess,
    GetCurrentRequirementFailure Failure,
    CurrentRequirementReadModel? Requirement)
{
    public static GetCurrentRequirementResult Success(
        CurrentRequirementReadModel requirement) =>
        new(true, GetCurrentRequirementFailure.None, requirement);

    public static GetCurrentRequirementResult Failed(
        GetCurrentRequirementFailure failure) =>
        new(false, failure, null);
}

public sealed class GetCurrentRequirementService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IRequirementRepository requirementRepository)
{
    public async Task<GetCurrentRequirementResult> ExecuteAsync(
        GetCurrentRequirementCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PreQuoteId == Guid.Empty)
        {
            return GetCurrentRequirementResult.Failed(
                GetCurrentRequirementFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetCurrentRequirementResult.Failed(
                GetCurrentRequirementFailure.Unauthorized);
        }

        var access = await ValidateAccessAsync(
            command.PreQuoteId,
            userId,
            cancellationToken);
        if (access != GetCurrentRequirementFailure.None)
        {
            return GetCurrentRequirementResult.Failed(access);
        }

        try
        {
            var requirement = await requirementRepository
                .GetCurrentByPreQuoteIdAsync(
                    command.PreQuoteId,
                    cancellationToken);

            return requirement is null
                ? GetCurrentRequirementResult.Failed(
                    GetCurrentRequirementFailure.CurrentRequirementNotFound)
                : GetCurrentRequirementResult.Success(requirement);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return GetCurrentRequirementResult.Failed(
                GetCurrentRequirementFailure.QueryError);
        }
    }

    private async Task<GetCurrentRequirementFailure> ValidateAccessAsync(
        Guid preQuoteId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
            if (user is null)
            {
                return GetCurrentRequirementFailure.Unauthorized;
            }

            if (!user.IsActive)
            {
                return GetCurrentRequirementFailure.InactiveUser;
            }

            var preQuote = await preQuoteRepository.FindByIdAsync(
                preQuoteId,
                cancellationToken);
            if (preQuote is null)
            {
                return GetCurrentRequirementFailure.PreQuoteNotFound;
            }

            var project = await projectRepository.FindByIdAsync(
                preQuote.ProjectId,
                cancellationToken);
            if (project is null)
            {
                return GetCurrentRequirementFailure.ProjectNotFound;
            }

            if (project.CreatedByUserId != userId)
            {
                return GetCurrentRequirementFailure.PreQuoteNotFound;
            }

            if (!project.IsActive)
            {
                return GetCurrentRequirementFailure.InactiveProject;
            }

            var client = await clientRepository.FindByIdAsync(
                project.ClientId,
                cancellationToken);
            if (client is null)
            {
                return GetCurrentRequirementFailure.ClientNotFound;
            }

            return client.IsActive
                ? GetCurrentRequirementFailure.None
                : GetCurrentRequirementFailure.InactiveClient;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return GetCurrentRequirementFailure.QueryError;
        }
    }
}
