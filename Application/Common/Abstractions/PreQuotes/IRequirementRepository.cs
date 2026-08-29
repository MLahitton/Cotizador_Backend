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

    Task<IReadOnlyList<RequirementDocumentReadModel>>
        ListDocumentReadModelsByRequirementIdAsync(
            Guid requirementId,
            CancellationToken cancellationToken);

    Task<Requirement?> FindByIdForUpdateAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<RequirementFile?> FindFileForUpdateAsync(
        Guid requirementFileId,
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<CurrentRequirementReadModel?> GetCurrentByPreQuoteIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken);

    Task<RequirementProcessingAttempt?> FindProcessingAttemptByIdAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken);

    Task<RequirementProcessingAttempt?>
        FindActiveProcessingAttemptByRequirementIdAsync(
            Guid requirementId,
            CancellationToken cancellationToken);

    Task<RequirementProcessingFailureFinalization?>
        FinalizeProcessingFailureAsync(
            Guid requirementId,
            Guid processingAttemptId,
            string errorCode,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken);

    Task<RequirementProcessingCancellationFinalization?>
        FinalizeProcessingCancellationAsync(
            Guid requirementId,
            Guid processingAttemptId,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken);

    void Add(Requirement requirement);

    void AddFile(RequirementFile file);

    void RemoveFile(RequirementFile file);

    void AddProcessingAttempt(RequirementProcessingAttempt attempt);

    void AddExtractionResult(RequirementExtractionResult result);

    void AddExtractedItem(RequirementExtractedItem item);

    void AddExtractedItemEvidence(RequirementExtractedItemEvidence evidence);

    void AddExtractedItemSegment(RequirementExtractedItemSegment segment);

    void AddTechnicalProposal(RequirementTechnicalProposal proposal);

    void AddPricingSnapshot(RequirementPricingSnapshot snapshot);

    Task<RequirementExtractionResult?> GetLatestSuccessfulExtractionAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<RequirementTechnicalProposal?> GetCurrentTechnicalProposalAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<RequirementTechnicalProposal?> FindTechnicalProposalForUpdateAsync(
        Guid technicalProposalId,
        CancellationToken cancellationToken);

    Task<RequirementTechnicalProposal?> FindCurrentTechnicalProposalForUpdateAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<RequirementPricingSnapshot?> GetCurrentPricingSnapshotAsync(
        Guid requirementId,
        CancellationToken cancellationToken);

    Task<RequirementPricingSnapshot?> FindCurrentPricingSnapshotForUpdateAsync(
        Guid requirementId,
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

public sealed record RequirementProcessingCancellationFinalization(
    Guid RequirementId,
    Guid ProcessingAttemptId,
    Guid CorrelationId,
    DocumentProcessingState ProcessingState,
    DocumentProcessingOutcome Outcome,
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
    Guid? LatestAttemptId,
    DocumentProcessingState? LatestAttemptState,
    DocumentProcessingOutcome? LatestAttemptOutcome,
    string? LatestAttemptErrorCode,
    bool CanEditDocuments,
    bool CanCancel,
    bool CanReplace,
    bool IsCurrent,
    Guid? SupersedesRequirementId,
    Guid? SupersededByRequirementId,
    IReadOnlyList<RequirementDocumentReadModel> Documents);

public sealed record RequirementDocumentReadModel(
    Guid RequirementFileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);

public sealed record RequirementDetailsReadModel(
    Guid RequirementId,
    Guid PreQuoteId,
    RequirementStatus Status,
    RequirementCommercialLine? CommercialLine,
    bool CanEditDocuments,
    bool CanCancel,
    bool CanReplace,
    bool IsCurrent,
    Guid? SupersedesRequirementId,
    Guid? SupersededByRequirementId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RequirementDocumentReadModel> Documents);

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
