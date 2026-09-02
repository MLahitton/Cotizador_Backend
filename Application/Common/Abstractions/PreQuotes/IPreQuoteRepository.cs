using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public interface IPreQuoteRepository
{
    Task<PreQuote?> FindForUpdateByIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken);

    Task<PreQuoteDetails?> FindByIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken);

    Task<PreQuoteSearchPage> SearchByProjectAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<string> ReserveNextSerialAsync(
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);

    void Add(PreQuote preQuote);

    void AddDocument(PreQuoteDocument document);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record PreQuoteDetails(
    Guid Id,
    Guid ProjectId,
    string Serial,
    string? Name,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public PreQuoteDetails(
        Guid id,
        Guid projectId,
        int documentCount,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc)
        : this(
            id,
            projectId,
            PreQuote.FormatSerial(createdAtUtc.UtcDateTime.Year, 1),
            null,
            documentCount,
            createdAtUtc,
            updatedAtUtc)
    {
    }
}

public sealed record PreQuoteSearchItem(
    Guid Id,
    Guid ProjectId,
    string Serial,
    string? Name,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool HasRequirement,
    Guid? LatestRequirementId,
    RequirementStatus? LatestRequirementStatus,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    int? TechnicalProposalItemCount,
    DocumentProcessingState? LatestAttemptState,
    DocumentProcessingOutcome? LatestAttemptOutcome,
    string? LatestAttemptErrorCode)
{
    public PreQuoteSearchItem(
        Guid id,
        Guid projectId,
        int documentCount,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        bool hasRequirement,
        Guid? latestRequirementId,
        RequirementStatus? latestRequirementStatus,
        bool hasTechnicalProposal,
        Guid? technicalProposalId,
        int? technicalProposalItemCount,
        DocumentProcessingState? latestAttemptState,
        DocumentProcessingOutcome? latestAttemptOutcome,
        string? latestAttemptErrorCode)
        : this(
            id,
            projectId,
            PreQuote.FormatSerial(createdAtUtc.UtcDateTime.Year, 1),
            null,
            documentCount,
            createdAtUtc,
            updatedAtUtc,
            hasRequirement,
            latestRequirementId,
            latestRequirementStatus,
            hasTechnicalProposal,
            technicalProposalId,
            technicalProposalItemCount,
            latestAttemptState,
            latestAttemptOutcome,
            latestAttemptErrorCode)
    {
    }
}

public sealed record PreQuoteSearchPage(
    IReadOnlyList<PreQuoteSearchItem> Items,
    int TotalCount);

public sealed class PreQuoteQueryException : Exception
{
    public PreQuoteQueryException(Exception innerException)
        : base(
            "No fue posible consultar las precotizaciones.",
            innerException)
    {
    }
}

public sealed class PreQuotePersistenceException : Exception
{
    public PreQuotePersistenceException(Exception innerException)
        : base(
            "No fue posible guardar la precotizaciÃ³n.",
            innerException)
    {
    }
}
