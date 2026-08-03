using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;

namespace Application.PreQuotes.GetDocumentProcessingAttempt;

public sealed class GetDocumentProcessingAttemptService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IDocumentProcessingRepository repository)
{
    public async Task<GetDocumentProcessingAttemptResult> ExecuteAsync(
        Guid documentId,
        Guid processingAttemptId,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty || processingAttemptId == Guid.Empty)
        {
            return GetDocumentProcessingAttemptResult.Failed(
                GetDocumentProcessingAttemptFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetDocumentProcessingAttemptResult.Failed(
                GetDocumentProcessingAttemptFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetDocumentProcessingAttemptResult.Failed(
                GetDocumentProcessingAttemptFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetDocumentProcessingAttemptResult.Failed(
                GetDocumentProcessingAttemptFailure.InactiveUser);
        }

        DocumentProcessingSource? source;

        try
        {
            source = await repository.FindDocumentSourceAsync(
                documentId,
                cancellationToken);
        }
        catch (DocumentProcessingQueryException)
        {
            return GetDocumentProcessingAttemptResult.Failed(
                GetDocumentProcessingAttemptFailure.QueryError);
        }

        if (source is null || source.ProjectCreatedByUserId != userId)
        {
            return GetDocumentProcessingAttemptResult.Failed(
                GetDocumentProcessingAttemptFailure.DocumentNotFound);
        }

        DocumentProcessingAttemptStatusSnapshot? snapshot;

        try
        {
            snapshot = await repository.FindAttemptStatusAsync(
                documentId,
                processingAttemptId,
                userId,
                cancellationToken);
        }
        catch (DocumentProcessingQueryException)
        {
            return GetDocumentProcessingAttemptResult.Failed(
                GetDocumentProcessingAttemptFailure.QueryError);
        }

        if (snapshot is null)
        {
            return GetDocumentProcessingAttemptResult.Failed(
                GetDocumentProcessingAttemptFailure.AttemptNotFound);
        }

        return GetDocumentProcessingAttemptResult.Success(
            new DocumentProcessingAttemptStatusData(
                snapshot.ProcessingAttemptId,
                snapshot.DocumentId,
                snapshot.ProcessingState,
                snapshot.Outcome,
                snapshot.ErrorCode,
                snapshot.CreatedAtUtc,
                snapshot.StartedAtUtc,
                snapshot.CompletedAtUtc,
                snapshot.ResultPayloadJson));
    }
}
