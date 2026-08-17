using Application.Common.Abstractions.DocumentProcessing;

namespace Application.Common.Abstractions.HistoricalPricing;

public enum HistoricalDocumentEstimatePipelineFailure
{
    None = 0,
    CorpusUnavailable,
    Ai2RemoteRejection,
    Ai2Unavailable,
    InvalidExtraction
}

public sealed record HistoricalDocumentEstimatePipelineResult(
    HistoricalDocumentEstimatePipelineFailure Failure,
    Guid? ProjectId,
    Guid? RequirementId,
    int SourceCount,
    IReadOnlyList<StructuredItemData> SourceItems,
    PricedRequirementExtraction? Aggregate)
{
    public bool IsSuccess => Failure == HistoricalDocumentEstimatePipelineFailure.None;
}

public interface IHistoricalDocumentEstimatePipeline
{
    Task<HistoricalDocumentEstimatePipelineResult> EstimateAsync(
        IReadOnlyList<DocumentProcessingFile> files,
        Guid? projectId,
        Guid? requirementId,
        CancellationToken cancellationToken);
}
