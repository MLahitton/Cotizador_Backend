using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;

namespace Application.HistoricalPricing;

public sealed class HistoricalDocumentEstimatePipeline(
    IAi2DocumentProcessingClient ai2Client,
    IPriceRequirementExtractionService extractionPricingService,
    IHistoricalQuoteCorpus corpus) : IHistoricalDocumentEstimatePipeline
{
    public async Task<HistoricalDocumentEstimatePipelineResult> EstimateAsync(
        IReadOnlyList<DocumentProcessingFile> files,
        Guid? projectId,
        Guid? requirementId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ArgumentException("Debe existir al menos un documento.", nameof(files));
        }

        var corpusSnapshot = corpus.Current;
        if (!corpusSnapshot.IsAvailable)
        {
            corpusSnapshot = await corpus.ReloadAsync(cancellationToken);
        }

        if (!corpusSnapshot.IsAvailable)
        {
            return Failed(HistoricalDocumentEstimatePipelineFailure.CorpusUnavailable);
        }

        var processingResult = await ai2Client.ProcessAsync(
            new DocumentProcessingClientRequest(
                files[0].DocumentId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                files,
                projectId,
                requirementId),
            cancellationToken);

        if (!processingResult.IsSuccess)
        {
            return Failed(processingResult.Failure
                == DocumentProcessingClientFailure.RemoteRejection
                    ? HistoricalDocumentEstimatePipelineFailure.Ai2RemoteRejection
                    : HistoricalDocumentEstimatePipelineFailure.Ai2Unavailable);
        }

        var response = processingResult.Response!;
        var extraction = response.StructuredExtraction;
        if (extraction is null)
        {
            return Failed(HistoricalDocumentEstimatePipelineFailure.InvalidExtraction);
        }

        var aggregate = await extractionPricingService.PriceAsync(
            extraction.Items,
            response.Warnings,
            cancellationToken);

        return new HistoricalDocumentEstimatePipelineResult(
            HistoricalDocumentEstimatePipelineFailure.None,
            projectId,
            requirementId,
            files.Count,
            extraction.Items,
            aggregate);

        HistoricalDocumentEstimatePipelineResult Failed(
            HistoricalDocumentEstimatePipelineFailure failure) =>
            new(failure, projectId, requirementId, files.Count, [], null);
    }
}
