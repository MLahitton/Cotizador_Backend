namespace Application.Common.Abstractions.PreQuotes;

public sealed record StoredPreQuoteDocumentReadModel(
    Guid DocumentId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string StorageKey);

public sealed record StoredPreQuoteDocumentsReadModel(
    Guid PreQuoteId,
    Guid ProjectId,
    bool AllRequestedDocumentsFound,
    IReadOnlyList<StoredPreQuoteDocumentReadModel> Documents);

public interface IPreQuoteStoredDocumentRepository
{
    Task<StoredPreQuoteDocumentsReadModel?> GetForHistoricalEstimateAsync(
        Guid preQuoteId,
        Guid ownerUserId,
        IReadOnlyList<Guid>? documentIds,
        CancellationToken cancellationToken);
}

public sealed class StoredPreQuoteDocumentQueryException(Exception innerException)
    : Exception("No fue posible consultar los documentos almacenados.", innerException);
