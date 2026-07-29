using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Projects;
using Domain.Projects;
using FluentValidation;

namespace Application.Projects.SetProjectActivation;

public sealed class SetProjectActivationService(
    IValidator<SetProjectActivationCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository)
{
    public async Task<SetProjectActivationResult> ExecuteAsync(
        SetProjectActivationCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validation.IsValid)
        {
            return SetProjectActivationResult.Failed(
                SetProjectActivationFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return SetProjectActivationResult.Failed(
                SetProjectActivationFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return SetProjectActivationResult.Failed(
                SetProjectActivationFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return SetProjectActivationResult.Failed(
                SetProjectActivationFailure.InactiveUser);
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
            return SetProjectActivationResult.Failed(
                SetProjectActivationFailure.QueryError);
        }

        if (project is null)
        {
            return SetProjectActivationResult.Failed(
                SetProjectActivationFailure.NotFound);
        }

        var desiredState = command.IsActive!.Value;
        var stateChanged = project.IsActive != desiredState;
        project.SetActive(
            desiredState,
            user.Id,
            DateTimeOffset.UtcNow);

        if (stateChanged)
        {
            try
            {
                await projectRepository.SaveChangesAsync(
                    cancellationToken);
            }
            catch (ProjectPersistenceException)
            {
                return SetProjectActivationResult.Failed(
                    SetProjectActivationFailure.PersistenceError);
            }
            catch (ProjectConflictException)
            {
                return SetProjectActivationResult.Failed(
                    SetProjectActivationFailure.PersistenceError);
            }
        }

        return SetProjectActivationResult.Success(
            new ProjectActivationResult(
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
