using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using FluentValidation;
namespace Application.PreQuotes.CreatePreQuoteDraft;
public sealed class CreatePreQuoteDraftService(
    IValidator<CreatePreQuoteDraftCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identity,
    IPreQuoteDraftRepository repository,
    TimeProvider timeProvider)
{
    public async Task<CreatePreQuoteDraftResult> ExecuteAsync(
        CreatePreQuoteDraftCommand command, CancellationToken cancellationToken)
    {
        if (!(await validator.ValidateAsync(command, cancellationToken)).IsValid)
            return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InvalidRequest);
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        var user = await identity.FindUserByIdAsync(userId, cancellationToken);
        if (user is null) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        if (!user.IsActive) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveUser);
        try
        {
            var source = await repository.FindSourceAsync(
                command.PreQuoteId, command.SourceDocumentId,
                command.SourceStructuredExtractionId, user.Id,
                cancellationToken);
            if (source is null) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.NotFound);
            if (await repository.ExistsAsync(
                    command.PreQuoteId, user.Id, cancellationToken))
                return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.DraftAlreadyExists);
            if (!source.ProjectIsActive) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveProject);
            if (!source.ClientIsActive) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveClient);
            var draft = PreQuoteDraft.Create(
                source.PreQuoteId, source.DocumentId, source.StructuredExtractionId,
                source.ProjectName, source.ClientName, source.Location, userId,
                timeProvider.GetUtcNow(), source.Items, source.Requirements,
                source.DocumentReferences, source.Issues, source.Conflicts);
            repository.Add(draft);
            await repository.SaveChangesAsync(cancellationToken);
            return CreatePreQuoteDraftResult.Success(draft);
        }
        catch (PreQuoteDraftConflictException) { return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.DraftAlreadyExists); }
        catch (PreQuoteDraftQueryException) { return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.QueryError); }
        catch (PreQuoteDraftPersistenceException) { return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.PersistenceError); }
    }
}
