using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Projects;
using Domain.Projects;
using FluentValidation;

namespace Application.Projects.UpdateProject;

public sealed class UpdateProjectService(
    IValidator<UpdateProjectCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository)
{
    public async Task<UpdateProjectResult> ExecuteAsync(
        UpdateProjectCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.InactiveUser);
        }

        Project? project;

        try
        {
            project = await projectRepository.FindForUpdateByIdAsync(
                command.ProjectId,
                cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.QueryError);
        }

        if (project is null)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.NotFound);
        }

        var normalizedCode = command.Code!
            .Trim()
            .ToUpperInvariant();

        bool codeExists;

        try
        {
            codeExists =
                await projectRepository
                    .ExistsByCodeForOtherProjectAsync(
                        project.Id,
                        normalizedCode,
                        cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.QueryError);
        }

        if (codeExists)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.DuplicateCode);
        }

        var now = DateTimeOffset.UtcNow;

        project.UpdateDetails(
            normalizedCode,
            command.Name!,
            NormalizeOptional(command.Description),
            NormalizeOptional(command.Location),
            user.Id,
            now);

        try
        {
            await projectRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch (ProjectConflictException)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.DuplicateCode);
        }
        catch (ProjectPersistenceException)
        {
            return UpdateProjectResult.Failed(
                UpdateProjectFailure.PersistenceError);
        }

        return UpdateProjectResult.Success(
            new UpdatedProjectResult(
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

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
