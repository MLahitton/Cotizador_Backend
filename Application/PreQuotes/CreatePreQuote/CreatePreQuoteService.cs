using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.CreatePreQuote;

public sealed class CreatePreQuoteService(
    IValidator<CreatePreQuoteCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IPreQuoteRepository preQuoteRepository,
    ILogger<CreatePreQuoteService> logger)
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

        Domain.Identity.User? user;
        try
        {
            user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command.ProjectId, userId, "identity_query");
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.QueryError);
        }

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
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command.ProjectId, userId, "project_query");
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.QueryError);
        }

        if (project is null)
        {
            return CreatePreQuoteResult.Failed(
                CreatePreQuoteFailure.ProjectNotFound);
        }

        if (project.CreatedByUserId != userId)
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
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command.ProjectId, userId, "client_query");
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

        PreQuote preQuote;
        try
        {
            preQuote = PreQuote.Create(project.Id, user.Id, now);
            preQuoteRepository.Add(preQuote);
            await preQuoteRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command.ProjectId, userId, "persistence");
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

    private void LogFailure(
        Exception exception,
        Guid projectId,
        Guid userId,
        string stage)
    {
        logger.LogError(
            exception,
            "PreQuote creation failed. ProjectId={ProjectId} UserId={UserId} Stage={Stage} TraceId={TraceId} ExceptionType={ExceptionType}",
            projectId,
            userId,
            stage,
            System.Diagnostics.Activity.Current?.Id,
            exception.GetType().Name);
    }
}
