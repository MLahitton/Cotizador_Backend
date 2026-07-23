using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.CreatePreQuote;

public sealed class CreatePreQuoteService(
    IValidator<CreatePreQuoteCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IPreQuoteRepository preQuoteRepository)
{
    public async Task<CreatePreQuoteResult> ExecuteAsync(
        CreatePreQuoteCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.InactiveUser);
        }

        Domain.Projects.Project? project;

        try
        {
            project = await projectRepository.FindByIdAsync(
                command.ProjectId,
                cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.QueryError);
        }

        if (project is null)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.ProjectNotFound);
        }

        if (!project.IsActive)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.InactiveProject);
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
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.QueryError);
        }

        if (client is null)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.ClientNotFound);
        }

        if (!client.IsActive)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.InactiveClient);
        }

        var now = DateTimeOffset.UtcNow;

        var preQuote = PreQuote.Create(
            project.Id,
            user.Id,
            now);

        preQuoteRepository.Add(preQuote);

        try
        {
            await preQuoteRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch (PreQuotePersistenceException)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.PersistenceError);
        }

        return CreatePreQuoteResult.Success(
            new CreatedPreQuoteResult(
                preQuote.Id,
                preQuote.ProjectId,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc));
    }
}
