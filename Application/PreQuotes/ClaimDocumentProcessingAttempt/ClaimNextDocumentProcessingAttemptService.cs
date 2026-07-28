using Application.Common.Abstractions.DocumentProcessing;

namespace Application.PreQuotes.ClaimDocumentProcessingAttempt;

public interface IDocumentProcessingClaimService
{
    Task<Guid?> ClaimNextAsync(CancellationToken cancellationToken);
}

public sealed class ClaimNextDocumentProcessingAttemptService(
    IDocumentProcessingRepository repository,
    TimeProvider timeProvider)
    : IDocumentProcessingClaimService
{
    public Task<Guid?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        return repository.ClaimNextPendingDocumentProcessingAttemptAsync(
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
