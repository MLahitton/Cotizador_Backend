using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.ClaimDocumentProcessingAttempt;
using CotizadorBackend.Tests.TestDoubles;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ClaimNextDocumentProcessingAttemptServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClaimNextAsync_ForwardsClockTokenAndResult(bool hasWork)
    {
        var repository = Substitute.For<IDocumentProcessingRepository>();
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var expected = hasWork ? Guid.NewGuid() : (Guid?)null;
        repository.ClaimNextPendingDocumentProcessingAttemptAsync(
                now,
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = new ClaimNextDocumentProcessingAttemptService(
            repository,
            new FixedTimeProvider(now));

        var result = await service.ClaimNextAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        await repository.Received(1)
            .ClaimNextPendingDocumentProcessingAttemptAsync(
                now,
                TestContext.Current.CancellationToken);
    }
}
