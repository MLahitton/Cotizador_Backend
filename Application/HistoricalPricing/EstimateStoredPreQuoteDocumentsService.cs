using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Storage;

namespace Application.HistoricalPricing;

public enum StoredPreQuoteHistoricalEstimateFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    NotFound,
    NoDocuments,
    FileUnavailable,
    QueryError
}

public sealed record StoredPreQuoteHistoricalEstimateResult(
    StoredPreQuoteHistoricalEstimateFailure Failure,
    HistoricalDocumentEstimatePipelineResult? Estimate)
{
    public bool IsSuccess => Failure == StoredPreQuoteHistoricalEstimateFailure.None
        && Estimate is { IsSuccess: true };
}

public sealed class EstimateStoredPreQuoteDocumentsService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteStoredDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IHistoricalDocumentEstimatePipeline pipeline)
{
    public async Task<StoredPreQuoteHistoricalEstimateResult> ExecuteAsync(
        Guid preQuoteId,
        IReadOnlyList<Guid>? documentIds,
        CancellationToken cancellationToken)
    {
        if (preQuoteId == Guid.Empty
            || documentIds is { Count: 0 }
            || documentIds?.Any(id => id == Guid.Empty) == true)
        {
            return Failed(StoredPreQuoteHistoricalEstimateFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return Failed(StoredPreQuoteHistoricalEstimateFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Failed(StoredPreQuoteHistoricalEstimateFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return Failed(StoredPreQuoteHistoricalEstimateFailure.InactiveUser);
        }

        StoredPreQuoteDocumentsReadModel? stored;
        try
        {
            stored = await documentRepository.GetForHistoricalEstimateAsync(
                preQuoteId,
                userId,
                documentIds?.Distinct().ToArray(),
                cancellationToken);
        }
        catch (StoredPreQuoteDocumentQueryException)
        {
            return Failed(StoredPreQuoteHistoricalEstimateFailure.QueryError);
        }

        if (stored is null || !stored.AllRequestedDocumentsFound)
        {
            return Failed(StoredPreQuoteHistoricalEstimateFailure.NotFound);
        }

        if (stored.Documents.Count == 0)
        {
            return Failed(StoredPreQuoteHistoricalEstimateFailure.NoDocuments);
        }

        var streams = new List<Stream>(stored.Documents.Count);
        try
        {
            foreach (var document in stored.Documents)
            {
                streams.Add(await fileStorage.OpenReadAsync(
                    document.StorageKey,
                    cancellationToken));
            }

            var files = stored.Documents.Select((document, index) =>
                new DocumentProcessingFile(
                    document.DocumentId,
                    document.OriginalFileName,
                    document.ContentType,
                    document.SizeBytes,
                    streams[index])).ToArray();

            var estimate = await pipeline.EstimateAsync(
                files,
                stored.ProjectId,
                stored.PreQuoteId,
                cancellationToken);
            return new StoredPreQuoteHistoricalEstimateResult(
                StoredPreQuoteHistoricalEstimateFailure.None,
                estimate);
        }
        catch (Exception exception) when (
            exception is FileStorageReadException
                or InvalidStorageKeyException
                or FileNotFoundException
                or DirectoryNotFoundException)
        {
            return Failed(StoredPreQuoteHistoricalEstimateFailure.FileUnavailable);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }

        static StoredPreQuoteHistoricalEstimateResult Failed(
            StoredPreQuoteHistoricalEstimateFailure failure) => new(failure, null);
    }
}
