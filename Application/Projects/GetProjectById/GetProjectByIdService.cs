using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Projects;
using Domain.Projects;
using FluentValidation;

namespace Application.Projects.GetProjectById;

public sealed class GetProjectByIdService(
    IValidator<GetProjectByIdQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository)
{
    public async Task<GetProjectByIdResult> ExecuteAsync(
        GetProjectByIdQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetProjectByIdResult.Failed(
                GetProjectByIdFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetProjectByIdResult.Failed(
                GetProjectByIdFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetProjectByIdResult.Failed(
                GetProjectByIdFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetProjectByIdResult.Failed(
                GetProjectByIdFailure.InactiveUser);
        }

        Project? project;

        try
        {
            project = await projectRepository.FindByIdAsync(
                query.ProjectId,
                cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return GetProjectByIdResult.Failed(
                GetProjectByIdFailure.QueryError);
        }

        if (project is null)
        {
            return GetProjectByIdResult.Failed(
                GetProjectByIdFailure.NotFound);
        }

        return GetProjectByIdResult.Success(
            new ProjectByIdResult(
                project.Id,
                project.ClientId,
                project.Code,
                project.Name,
                project.Description,
                project.Location,
                project.IsActive,
                project.CreatedAtUtc,
                project.UpdatedAtUtc));
    }
}
