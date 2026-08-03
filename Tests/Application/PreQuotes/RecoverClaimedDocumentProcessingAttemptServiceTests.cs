using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.RecoverClaimedDocumentProcessingAttempt;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class RecoverClaimedDocumentProcessingAttemptServiceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecoverAsync_WithProcessingAttempt_FinishesAsInternalError()
    {
        var repository = Substitute.For<IDocumentProcessingRepository>();
        var attempt = DocumentProcessingAttempt.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CreatedAt);
        attempt.Start(CreatedAt.AddMinutes(1));
        repository.FindProcessingWorkItemAsync(
                attempt.Id,
                Arg.Any<CancellationToken>())
            .Returns(new DocumentProcessingWorkItem(
                attempt,
                new DocumentProcessingSource(
                    attempt.PreQuoteDocumentId,
                    Guid.NewGuid(),
                    "document.pdf",
                    "application/pdf",
                    100,
                    "prequotes/document.pdf",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    true,
                    Guid.NewGuid(),
                    true)));
        var completedAt = CreatedAt.AddMinutes(2);
        var service = new RecoverClaimedDocumentProcessingAttemptService(
            repository,
            new FixedTimeProvider(completedAt));

        var result = await service.RecoverAsync(
            attempt.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            RecoverClaimedDocumentProcessingAttemptResult.Recovered,
            result);
        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(DocumentProcessingOutcome.Failed, attempt.Outcome);
        Assert.Equal(
            RecoverClaimedDocumentProcessingAttemptService.InternalErrorCode,
            attempt.ErrorCode);
        Assert.Equal(completedAt, attempt.CompletedAtUtc);
        await repository.Received(1).SaveChangesAsync(
            TestContext.Current.CancellationToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
