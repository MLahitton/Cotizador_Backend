using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;

namespace Application.PreQuotes.RecoverClaimedDocumentProcessingAttempt;

public enum RecoverClaimedDocumentProcessingAttemptResult
{
    Recovered,
    AttemptNotProcessing
}

public interface IClaimedDocumentProcessingRecoveryService
{
    Task<RecoverClaimedDocumentProcessingAttemptResult> RecoverAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken);
}

public sealed class RecoverClaimedDocumentProcessingAttemptService(
    IDocumentProcessingRepository repository,
    TimeProvider timeProvider)
    : IClaimedDocumentProcessingRecoveryService
{
    public const string InternalErrorCode = "PROCESSING_INTERNAL_ERROR";

    public async Task<RecoverClaimedDocumentProcessingAttemptResult>
        RecoverAsync(
            Guid processingAttemptId,
            CancellationToken cancellationToken)
    {
        var workItem = await repository.FindProcessingWorkItemAsync(
            processingAttemptId,
            cancellationToken);

        if (workItem?.Attempt is not { } attempt
            || attempt.ProcessingState != DocumentProcessingState.Processing)
        {
            return RecoverClaimedDocumentProcessingAttemptResult
                .AttemptNotProcessing;
        }

        attempt.Fail(InternalErrorCode, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);

        return RecoverClaimedDocumentProcessingAttemptResult.Recovered;
    }
}
