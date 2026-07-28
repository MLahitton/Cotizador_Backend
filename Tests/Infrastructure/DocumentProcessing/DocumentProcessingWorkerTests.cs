using Application.PreQuotes.ClaimDocumentProcessingAttempt;
using Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;
using Infrastructure.DocumentProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class DocumentProcessingWorkerTests
{
    [Fact]
    public async Task ExecuteIterationAsync_WithNoWork_UsesOnlyClaimScope()
    {
        var context = new Context(null);

        var result = await context.Worker.ExecuteIterationAsync(
            TestContext.Current.CancellationToken);

        Assert.False(result);
        await context.Claim.Received(1).ClaimNextAsync(
            TestContext.Current.CancellationToken);
        await context.Processor.DidNotReceive().ProcessAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        context.ClaimScope.Received(1).Dispose();
        context.ProcessingScope.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task ExecuteIterationAsync_WithWork_ClaimsAndProcessesSequentially()
    {
        var attemptId = Guid.NewGuid();
        var context = new Context(attemptId);
        var order = new List<string>();
        context.Claim.ClaimNextAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("claim");
                return attemptId;
            });
        context.Processor.ProcessAsync(
                attemptId,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("process");
                return ProcessClaimedDocumentProcessingAttemptResult.Completed;
            });

        var result = await context.Worker.ExecuteIterationAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal(["claim", "process"], order);
        context.ClaimScope.Received(1).Dispose();
        context.ProcessingScope.Received(1).Dispose();
    }

    [Fact]
    public async Task ExecuteIterationAsync_WhenProcessingCancels_Propagates()
    {
        var attemptId = Guid.NewGuid();
        var context = new Context(attemptId);
        using var source = new CancellationTokenSource();
        source.Cancel();
        context.Processor.ProcessAsync(
                attemptId,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<ProcessClaimedDocumentProcessingAttemptResult>(
                source.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Worker.ExecuteIterationAsync(source.Token));

        await context.Processor.Received(1).ProcessAsync(
            attemptId,
            source.Token);
    }

    [Fact]
    public async Task ExecuteIterationAsync_WhenClaimFails_PropagatesAndDisposesScope()
    {
        var context = new Context(null);
        context.Claim.ClaimNextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Guid?>(
                new InvalidOperationException("claim failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Worker.ExecuteIterationAsync(
                TestContext.Current.CancellationToken));

        context.ClaimScope.Received(1).Dispose();
        await context.Processor.DidNotReceive().ProcessAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteIterationAsync_WhenProcessingFails_PropagatesAndDisposesBothScopes()
    {
        var attemptId = Guid.NewGuid();
        var context = new Context(attemptId);
        context.Processor.ProcessAsync(
                attemptId,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ProcessClaimedDocumentProcessingAttemptResult>(
                new InvalidOperationException("processing failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Worker.ExecuteIterationAsync(
                TestContext.Current.CancellationToken));

        context.ClaimScope.Received(1).Dispose();
        context.ProcessingScope.Received(1).Dispose();
    }

    [Fact]
    public async Task ExecuteIterationAsync_WhenClaimCancels_PropagatesWithoutProcessing()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var context = new Context(null);
        context.Claim.ClaimNextAsync(source.Token)
            .Returns(Task.FromCanceled<Guid?>(source.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.Worker.ExecuteIterationAsync(source.Token));

        await context.Processor.DidNotReceive().ProcessAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        context.ClaimScope.Received(1).Dispose();
    }

    [Theory]
    [InlineData(ProcessClaimedDocumentProcessingAttemptResult.Completed)]
    [InlineData(ProcessClaimedDocumentProcessingAttemptResult.Failed)]
    [InlineData(ProcessClaimedDocumentProcessingAttemptResult.InvalidState)]
    public async Task ExecuteIterationAsync_WithHandledResult_ReportsWorkCompleted(
        ProcessClaimedDocumentProcessingAttemptResult processingResult)
    {
        var attemptId = Guid.NewGuid();
        var context = new Context(attemptId);
        context.Processor.ProcessAsync(
                attemptId,
                Arg.Any<CancellationToken>())
            .Returns(processingResult);

        var hadWork = await context.Worker.ExecuteIterationAsync(
            TestContext.Current.CancellationToken);

        Assert.True(hadWork);
        await context.Processor.Received(1).ProcessAsync(
            attemptId,
            TestContext.Current.CancellationToken);
    }

    private sealed class Context
    {
        public Context(Guid? attemptId)
        {
            Claim = Substitute.For<IDocumentProcessingClaimService>();
            Processor = Substitute.For<IClaimedDocumentProcessingService>();
            ClaimScope = Substitute.For<IServiceScope>();
            ProcessingScope = Substitute.For<IServiceScope>();
            var claimProvider = Substitute.For<IServiceProvider>();
            var processingProvider = Substitute.For<IServiceProvider>();
            ClaimScope.ServiceProvider.Returns(claimProvider);
            ProcessingScope.ServiceProvider.Returns(processingProvider);
            claimProvider.GetService(typeof(IDocumentProcessingClaimService))
                .Returns(Claim);
            processingProvider.GetService(
                    typeof(IClaimedDocumentProcessingService))
                .Returns(Processor);
            var factory = Substitute.For<IServiceScopeFactory>();
            factory.CreateScope().Returns(ClaimScope, ProcessingScope);
            Claim.ClaimNextAsync(Arg.Any<CancellationToken>())
                .Returns(attemptId);
            Processor.ProcessAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(
                    ProcessClaimedDocumentProcessingAttemptResult.Completed);
            Worker = new DocumentProcessingWorker(
                factory,
                new DocumentProcessingWorkerOptions(
                    true,
                    TimeSpan.FromMilliseconds(1)),
                Substitute.For<ILogger<DocumentProcessingWorker>>());
        }

        public IDocumentProcessingClaimService Claim { get; }
        public IClaimedDocumentProcessingService Processor { get; }
        public IServiceScope ClaimScope { get; }
        public IServiceScope ProcessingScope { get; }
        public DocumentProcessingWorker Worker { get; }
    }
}
