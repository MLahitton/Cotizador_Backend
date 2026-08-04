using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public sealed record PreQuoteDraftSourceContext(
    Guid PreQuoteId,
    Guid DocumentId,
    Guid StructuredExtractionId,
    bool ProjectIsActive,
    bool ClientIsActive,
    string? ProjectName,
    string? ClientName,
    string? Location,
    IReadOnlyList<PreQuoteDraftItemSource> Items,
    IReadOnlyList<PreQuoteDraftRequirementSource> Requirements,
    IReadOnlyList<PreQuoteDraftReferenceSource> DocumentReferences,
    IReadOnlyList<PreQuoteDraftIssueSource> Issues,
    IReadOnlyList<PreQuoteDraftConflictSource> Conflicts);

public sealed record PreQuoteDraftActivityContext(
    bool ProjectIsActive,
    bool ClientIsActive);

public interface IPreQuoteDraftRepository
{
    Task<PreQuoteDraftSourceContext?> FindSourceAsync(
        Guid preQuoteId,
        Guid documentId,
        Guid structuredExtractionId,
        Guid ownerUserId,
        CancellationToken cancellationToken);
    Task<bool> ExistsAsync(
        Guid preQuoteId,
        Guid ownerUserId,
        CancellationToken cancellationToken);
    Task<PreQuoteDraft?> FindForUpdateAsync(
        Guid preQuoteId,
        Guid ownerUserId,
        CancellationToken cancellationToken);
    Task<PreQuoteDraft?> FindReadAsync(
        Guid preQuoteId,
        Guid ownerUserId,
        CancellationToken cancellationToken);
    Task<PreQuoteDraftActivityContext?> FindActivityAsync(
        Guid preQuoteId,
        Guid ownerUserId,
        CancellationToken cancellationToken);
    void Add(PreQuoteDraft draft);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public class PreQuoteDraftQueryException(Exception innerException)
    : Exception("No fue posible consultar el borrador.", innerException);
public class PreQuoteDraftPersistenceException(Exception innerException)
    : Exception("No fue posible guardar el borrador.", innerException);
public sealed class PreQuoteDraftConcurrencyException(Exception innerException)
    : PreQuoteDraftPersistenceException(innerException);
public sealed class PreQuoteDraftConflictException(Exception innerException)
    : PreQuoteDraftPersistenceException(innerException);
