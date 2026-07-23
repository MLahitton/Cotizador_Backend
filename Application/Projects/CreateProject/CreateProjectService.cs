using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Domain.Clients;
using Domain.Projects;
using FluentValidation;

namespace Application.Projects.CreateProject;

public sealed class CreateProjectService(
    IValidator<CreateProjectCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IClientRepository clientRepository,
    IProjectRepository projectRepository)
{
    public async Task<CreateProjectResult> ExecuteAsync(
        CreateProjectCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.InactiveUser);
        }

        Client? client;

        try
        {
            client = await clientRepository.FindByIdAsync(
                command.ClientId!.Value,
                cancellationToken);
        }
        catch (ClientQueryException)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.PersistenceError);
        }

        if (client is null)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.ClientNotFound);
        }

        if (!client.IsActive)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.InactiveClient);
        }

        var normalizedCode = command.Code!
            .Trim()
            .ToUpperInvariant();

        try
        {
            if (await projectRepository.ExistsByCodeAsync(
                    normalizedCode,
                    cancellationToken))
            {
                return CreateProjectResult.Failed(
                    CreateProjectFailure.DuplicateCode);
            }
        }
        catch (ProjectQueryException)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.PersistenceError);
        }

        var now = DateTimeOffset.UtcNow;
        var project = Project.Create(
            client.Id,
            normalizedCode,
            command.Name!,
            NormalizeOptional(command.Description),
            NormalizeOptional(command.Location),
            user.Id,
            now);

        projectRepository.Add(project);

        try
        {
            await projectRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ProjectConflictException)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.DuplicateCode);
        }
        catch (ProjectPersistenceException)
        {
            return CreateProjectResult.Failed(
                CreateProjectFailure.PersistenceError);
        }

        return CreateProjectResult.Success(
            new CreatedProjectResult(
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
