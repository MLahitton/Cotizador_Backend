using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public interface IRequirementRepository
{
    Task<Requirement?> FindByIdAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RequirementFile>> ListFilesByRequirementIdAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<CurrentRequirementReadModel?> GetCurrentByPreQuoteIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken);

    Task<RequirementProcessingAttempt?> FindProcessingAttemptByIdAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken);

    Task<RequirementProcessingFailureFinalization?>
        FinalizeProcessingFailureAsync(
            Guid requirementId,
            Guid processingAttemptId,
            string errorCode,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken);

    void Add(Requirement requirement);

    void AddFile(RequirementFile file);

    void AddProcessingAttempt(RequirementProcessingAttempt attempt);

    void AddExtractionResult(RequirementExtractionResult result);

    void AddExtractedItem(RequirementExtractedItem item);

    void AddExtractedItemEvidence(RequirementExtractedItemEvidence evidence);

    void AddExtractedItemSegment(RequirementExtractedItemSegment segment);

    void AddTechnicalProposal(RequirementTechnicalProposal proposal);

    Task<RequirementExtractionResult?> GetLatestSuccessfulExtractionAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<RequirementTechnicalProposal?> GetCurrentTechnicalProposalAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<RequirementTechnicalProposal?> FindTechnicalProposalForUpdateAsync(
        Guid technicalProposalId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RequirementExtractedItem>> GetExtractedItemsAsync(
        Guid extractionResultId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record RequirementProcessingFailureFinalization(
    Guid RequirementId,
    Guid ProcessingAttemptId,
    Guid CorrelationId,
    DocumentProcessingState ProcessingState,
    DocumentProcessingOutcome Outcome,
    string ErrorCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public sealed record CurrentRequirementReadModel(
    Guid RequirementId,
    Guid PreQuoteId,
    RequirementStatus Status,
    RequirementCommercialLine? CommercialLine,
    DateTimeOffset CreatedAtUtc,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    DocumentProcessingState? LatestAttemptState,
    DocumentProcessingOutcome? LatestAttemptOutcome,
    string? LatestAttemptErrorCode);

public sealed class RequirementQueryException : Exception
{
    public RequirementQueryException(Exception innerException)
        : base(
            "No fue posible consultar el requerimiento.",
            innerException)
    {
    }
}

public sealed class RequirementPersistenceException : Exception
{
    public RequirementPersistenceException(Exception innerException)
        : base(
            "No fue posible guardar el requerimiento.",
            innerException)
    {
    }
}
