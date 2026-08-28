using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;

namespace Application.PreQuotes.GetRequirementDetails;

public sealed record GetRequirementDetailsCommand(Guid RequirementId);

public enum GetRequirementDetailsFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    RequirementNotFound,
    PreQuoteNotFound,
    ProjectNotFound,
    InactiveProject,
    ClientNotFound,
    InactiveClient,
    QueryError
}

public sealed record GetRequirementDetailsResult(
    bool IsSuccess,
    GetRequirementDetailsFailure Failure,
    RequirementDetailsReadModel? Requirement)
{
    public static GetRequirementDetailsResult Success(
        RequirementDetailsReadModel requirement) =>
        new(true, GetRequirementDetailsFailure.None, requirement);

    public static GetRequirementDetailsResult Failed(
        GetRequirementDetailsFailure failure) =>
        new(false, failure, null);
}

public sealed class GetRequirementDetailsService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IRequirementRepository requirementRepository)
{
    public async Task<GetRequirementDetailsResult> ExecuteAsync(
        GetRequirementDetailsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty)
        {
            return GetRequirementDetailsResult.Failed(
                GetRequirementDetailsFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetRequirementDetailsResult.Failed(
                GetRequirementDetailsFailure.Unauthorized);
        }

        Domain.Identity.User? user;
        Requirement? requirement;
        try
        {
            user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
            requirement = await requirementRepository.FindByIdAsync(
                command.RequirementId,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return GetRequirementDetailsResult.Failed(
                GetRequirementDetailsFailure.QueryError);
        }

        if (user is null)
        {
            return GetRequirementDetailsResult.Failed(
                GetRequirementDetailsFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetRequirementDetailsResult.Failed(
                GetRequirementDetailsFailure.InactiveUser);
        }

        if (requirement is null)
        {
            return GetRequirementDetailsResult.Failed(
                GetRequirementDetailsFailure.RequirementNotFound);
        }

        var access = await ValidateAccessAsync(
            requirement.PreQuoteId,
            userId,
            cancellationToken);
        if (access != GetRequirementDetailsFailure.None)
        {
            return GetRequirementDetailsResult.Failed(access);
        }

        var documents = requirement.Files
            .OrderBy(file => file.CreatedAtUtc)
            .ThenBy(file => file.Id)
            .Select(file => new RequirementDocumentReadModel(
                file.Id,
                file.OriginalFileName,
                file.ContentType,
                file.SizeBytes,
                file.CreatedAtUtc))
            .ToArray();

        return GetRequirementDetailsResult.Success(
            new RequirementDetailsReadModel(
                requirement.Id,
                requirement.PreQuoteId,
                requirement.Status,
                requirement.CommercialLine,
                requirement.CanEditDocuments,
                requirement.CanCancel,
                requirement.CanReplace,
                requirement.IsCurrent,
                requirement.SupersedesRequirementId,
                requirement.SupersededByRequirementId,
                requirement.CreatedAtUtc,
                requirement.UpdatedAtUtc,
                documents));
    }

    private async Task<GetRequirementDetailsFailure> ValidateAccessAsync(
        Guid preQuoteId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var preQuote = await preQuoteRepository.FindByIdAsync(
                preQuoteId,
                cancellationToken);
            if (preQuote is null)
            {
                return GetRequirementDetailsFailure.PreQuoteNotFound;
            }

            var project = await projectRepository.FindByIdAsync(
                preQuote.ProjectId,
                cancellationToken);
            if (project is null)
            {
                return GetRequirementDetailsFailure.ProjectNotFound;
            }

            if (project.CreatedByUserId != userId)
            {
                return GetRequirementDetailsFailure.RequirementNotFound;
            }

            if (!project.IsActive)
            {
                return GetRequirementDetailsFailure.InactiveProject;
            }

            var client = await clientRepository.FindByIdAsync(
                project.ClientId,
                cancellationToken);
            if (client is null)
            {
                return GetRequirementDetailsFailure.ClientNotFound;
            }

            return client.IsActive
                ? GetRequirementDetailsFailure.None
                : GetRequirementDetailsFailure.InactiveClient;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return GetRequirementDetailsFailure.QueryError;
        }
    }
}
