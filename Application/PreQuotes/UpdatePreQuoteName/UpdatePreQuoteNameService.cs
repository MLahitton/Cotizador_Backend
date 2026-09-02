using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using FluentValidation;

namespace Application.PreQuotes.UpdatePreQuoteName;

public sealed class UpdatePreQuoteNameService(
    IValidator<UpdatePreQuoteNameCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository,
    IPreQuoteRepository preQuoteRepository,
    TimeProvider timeProvider)
{
    public async Task<UpdatePreQuoteNameResult> ExecuteAsync(
        UpdatePreQuoteNameCommand command,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);
        if (user is null)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.InactiveUser);
        }

        Domain.PreQuotes.PreQuote? preQuote;
        try
        {
            preQuote = await preQuoteRepository.FindForUpdateByIdAsync(
                command.PreQuoteId,
                cancellationToken);
        }
        catch (PreQuoteQueryException)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.QueryError);
        }

        if (preQuote is null)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.NotFound);
        }

        try
        {
            var project = await projectRepository.FindByIdAsync(
                preQuote.ProjectId,
                cancellationToken);
            if (project is null || project.CreatedByUserId != userId)
            {
                return UpdatePreQuoteNameResult.Failed(
                    UpdatePreQuoteNameFailure.NotFound);
            }
        }
        catch (ProjectQueryException)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.QueryError);
        }

        try
        {
            preQuote.UpdateName(command.Name, timeProvider.GetUtcNow());
            await preQuoteRepository.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.InvalidRequest);
        }
        catch (PreQuotePersistenceException)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.PersistenceError);
        }

        PreQuoteDetails? details;
        try
        {
            details = await preQuoteRepository.FindByIdAsync(
                preQuote.Id,
                cancellationToken);
        }
        catch (PreQuoteQueryException)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.QueryError);
        }

        if (details is null)
        {
            return UpdatePreQuoteNameResult.Failed(
                UpdatePreQuoteNameFailure.NotFound);
        }

        return UpdatePreQuoteNameResult.Success(new UpdatedPreQuoteNameResult(
            details.Id,
            details.ProjectId,
            details.Serial,
            details.Name,
            details.DocumentCount,
            details.CreatedAtUtc,
            details.UpdatedAtUtc));
    }
}