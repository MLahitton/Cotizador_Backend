using System.Collections.Concurrent;
using System.Diagnostics;
using Application.PreQuotes.ClaimDocumentProcessingAttempt;
using Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;
using Infrastructure.DocumentProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

public sealed class DocumentProcessingWorkerTestsLoop
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ErrorPollInterval =
        TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan NegativeWindow =
        TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MinimumObservedErrorDelay =
        TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task StartAsync_WithNoWork_WaitsUntilStoppedWithoutProcessing()
    {
        var claimReached = Signal();
        var claim = Substitute.For<IDocumentProcessingClaimService>();
        var processor = Substitute.For<IClaimedDocumentProcessingService>();
        claim.ClaimNextAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                claimReached.TrySetResult();
                return (Guid?)null;
            });
        var claimScope = ScopeFor(claim);
        var factory = new QueueScopeFactory(claimScope);
        using var worker = CreateWorker(
            factory,
            TimeSpan.FromSeconds(30));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await claimReached.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        await StopAsync(worker);

        await claim.Received(1).ClaimNextAsync(
            Arg.Any<CancellationToken>());
        await processor.DidNotReceive().ProcessAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        Assert.True(claimScope.IsDisposed);
        Assert.Equal(1, factory.CreatedScopeCount);
    }

    [Fact]
    public async Task StartAsync_AfterSuccessfulWork_ClaimsAgainImmediately()
    {
        var attemptId = Guid.NewGuid();
        var secondClaimReached = Signal();
        var firstClaim = ClaimReturning(attemptId);
        var processor = Substitute.For<IClaimedDocumentProcessingService>();
        processor.ProcessAsync(
                attemptId,
                Arg.Any<CancellationToken>())
            .Returns(
                ProcessClaimedDocumentProcessingAttemptResult.Completed);
        var secondClaim = Substitute.For<IDocumentProcessingClaimService>();
        secondClaim.ClaimNextAsync(Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                secondClaimReached.TrySetResult();
                await WaitForCancellationAsync(
                    call.Arg<CancellationToken>());
                return null;
            });
        var firstClaimScope = ScopeFor(firstClaim);
        var processingScope = ScopeFor(processor);
        var secondClaimScope = ScopeFor(secondClaim);
        var factory = new QueueScopeFactory(
            firstClaimScope,
            processingScope,
            secondClaimScope);
        using var worker = CreateWorker(factory, TimeSpan.FromSeconds(30));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await secondClaimReached.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        await StopAsync(worker);

        await processor.Received(1).ProcessAsync(
            attemptId,
            Arg.Any<CancellationToken>());
        Assert.True(firstClaimScope.IsDisposed);
        Assert.True(processingScope.IsDisposed);
        Assert.True(secondClaimScope.IsDisposed);
        Assert.Equal(3, factory.CreatedScopeCount);
    }

    [Fact]
    public async Task StartAsync_AfterClaimError_WaitsAndClaimsAgain()
    {
        var firstErrorReached = Signal();
        var retryReached = Signal();
        var delayStopwatch = new Stopwatch();
        var firstClaim = Substitute.For<IDocumentProcessingClaimService>();
        firstClaim.ClaimNextAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                delayStopwatch.Restart();
                firstErrorReached.TrySetResult();
                return Task.FromException<Guid?>(
                    new InvalidOperationException("claim failed"));
            });
        var retryClaim = Substitute.For<IDocumentProcessingClaimService>();
        retryClaim.ClaimNextAsync(Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                retryReached.TrySetResult();
                await WaitForCancellationAsync(
                    call.Arg<CancellationToken>());
                return null;
            });
        var factory = new QueueScopeFactory(
            ScopeFor(firstClaim),
            ScopeFor(retryClaim));
        using var worker = CreateWorker(factory, ErrorPollInterval);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await firstErrorReached.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        var negativeWindow = Task.Delay(
            NegativeWindow,
            TestContext.Current.CancellationToken);
        var negativeWinner = await Task.WhenAny(
            retryReached.Task,
            negativeWindow);
        Assert.Same(negativeWindow, negativeWinner);
        Assert.False(retryReached.Task.IsCompleted);
        await retryReached.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        delayStopwatch.Stop();
        await StopAsync(worker);

        Assert.True(
            delayStopwatch.Elapsed >= MinimumObservedErrorDelay,
            $"El segundo claim ocurrio tras {delayStopwatch.Elapsed}.");
        await firstClaim.Received(1).ClaimNextAsync(
            Arg.Any<CancellationToken>());
        await retryClaim.Received(1).ClaimNextAsync(
            Arg.Any<CancellationToken>());
        Assert.Equal(2, factory.CreatedScopeCount);
    }

    [Fact]
    public async Task StartAsync_AfterProcessingError_WaitsAndClaimsAgain()
    {
        var attemptId = Guid.NewGuid();
        var processingErrorReached = Signal();
        var retryReached = Signal();
        var delayStopwatch = new Stopwatch();
        var processor = Substitute.For<IClaimedDocumentProcessingService>();
        processor.ProcessAsync(
                attemptId,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                delayStopwatch.Restart();
                processingErrorReached.TrySetResult();
                return Task.FromException<
                    ProcessClaimedDocumentProcessingAttemptResult>(
                    new InvalidOperationException("processing failed"));
            });
        var retryClaim = Substitute.For<IDocumentProcessingClaimService>();
        retryClaim.ClaimNextAsync(Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                retryReached.TrySetResult();
                await WaitForCancellationAsync(
                    call.Arg<CancellationToken>());
                return null;
            });
        var factory = new QueueScopeFactory(
            ScopeFor(ClaimReturning(attemptId)),
            ScopeFor(processor),
            ScopeFor(retryClaim));
        using var worker = CreateWorker(factory, ErrorPollInterval);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await processingErrorReached.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        var negativeWindow = Task.Delay(
            NegativeWindow,
            TestContext.Current.CancellationToken);
        var negativeWinner = await Task.WhenAny(
            retryReached.Task,
            negativeWindow);
        Assert.Same(negativeWindow, negativeWinner);
        Assert.False(retryReached.Task.IsCompleted);
        await retryReached.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        delayStopwatch.Stop();
        await StopAsync(worker);

        Assert.True(
            delayStopwatch.Elapsed >= MinimumObservedErrorDelay,
            $"El siguiente claim ocurrio tras {delayStopwatch.Elapsed}.");
        await processor.Received(1).ProcessAsync(
            attemptId,
            Arg.Any<CancellationToken>());
        await retryClaim.Received(1).ClaimNextAsync(
            Arg.Any<CancellationToken>());
        Assert.Equal(3, factory.CreatedScopeCount);
    }

    [Fact]
    public async Task StopAsync_DuringProcessing_StopsWithoutAnotherClaim()
    {
        var attemptId = Guid.NewGuid();
        var processingStarted = Signal();
        var processor = Substitute.For<IClaimedDocumentProcessingService>();
        processor.ProcessAsync(
                attemptId,
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                processingStarted.TrySetResult();
                await WaitForCancellationAsync(
                    call.Arg<CancellationToken>());
                return ProcessClaimedDocumentProcessingAttemptResult.Completed;
            });
        var factory = new QueueScopeFactory(
            ScopeFor(ClaimReturning(attemptId)),
            ScopeFor(processor));
        using var worker = CreateWorker(factory, TimeSpan.FromSeconds(30));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await processingStarted.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        await StopAsync(worker);

        Assert.Equal(2, factory.CreatedScopeCount);
        await processor.Received(1).ProcessAsync(
            attemptId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_ProcessesSequentiallyWithDistinctDisposedScopes()
    {
        var firstAttemptId = Guid.NewGuid();
        var secondAttemptId = Guid.NewGuid();
        var firstStarted = Signal();
        var releaseFirst = Signal();
        var secondStarted = Signal();
        var firstClaimScope = ScopeFor(ClaimReturning(firstAttemptId));
        var firstProcessor =
            Substitute.For<IClaimedDocumentProcessingService>();
        firstProcessor.ProcessAsync(
                firstAttemptId,
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                Assert.True(firstClaimScope.IsDisposed);
                firstStarted.TrySetResult();
                await releaseFirst.Task;
                return ProcessClaimedDocumentProcessingAttemptResult.Completed;
            });
        var secondClaimScope = ScopeFor(ClaimReturning(secondAttemptId));
        var secondProcessor =
            Substitute.For<IClaimedDocumentProcessingService>();
        secondProcessor.ProcessAsync(
                secondAttemptId,
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                secondStarted.TrySetResult();
                await WaitForCancellationAsync(
                    call.Arg<CancellationToken>());
                return ProcessClaimedDocumentProcessingAttemptResult.Completed;
            });
        var firstProcessingScope = ScopeFor(firstProcessor);
        var secondProcessingScope = ScopeFor(secondProcessor);
        var factory = new QueueScopeFactory(
            firstClaimScope,
            firstProcessingScope,
            secondClaimScope,
            secondProcessingScope);
        using var worker = CreateWorker(factory, TimeSpan.FromSeconds(30));

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await firstStarted.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        Assert.False(secondStarted.Task.IsCompleted);
        releaseFirst.TrySetResult();
        await secondStarted.Task.WaitAsync(
            SafetyTimeout,
            TestContext.Current.CancellationToken);
        await StopAsync(worker);

        Assert.True(firstClaimScope.IsDisposed);
        Assert.True(firstProcessingScope.IsDisposed);
        Assert.True(secondClaimScope.IsDisposed);
        Assert.True(secondProcessingScope.IsDisposed);
        Assert.NotSame(firstClaimScope, firstProcessingScope);
        Assert.NotSame(firstProcessor, secondProcessor);
        Assert.Equal(4, factory.CreatedScopeCount);
    }

    private static DocumentProcessingWorker CreateWorker(
        IServiceScopeFactory scopeFactory,
        TimeSpan pollInterval)
    {
        return new DocumentProcessingWorker(
            scopeFactory,
            new DocumentProcessingWorkerOptions(true, pollInterval),
            Substitute.For<ILogger<DocumentProcessingWorker>>());
    }

    private static IDocumentProcessingClaimService ClaimReturning(
        Guid attemptId)
    {
        var claim = Substitute.For<IDocumentProcessingClaimService>();
        claim.ClaimNextAsync(Arg.Any<CancellationToken>())
            .Returns(attemptId);
        return claim;
    }

    private static ProbeScope ScopeFor<TService>(TService service)
        where TService : class
    {
        return new ProbeScope(service);
    }

    private static TaskCompletionSource Signal()
    {
        return new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static async Task WaitForCancellationAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    private static async Task StopAsync(DocumentProcessingWorker worker)
    {
        await worker
            .StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(
                SafetyTimeout,
                TestContext.Current.CancellationToken);
    }

    private sealed class QueueScopeFactory(params ProbeScope[] scopes)
        : IServiceScopeFactory
    {
        private readonly ConcurrentQueue<ProbeScope> scopes = new(scopes);
        private int createdScopeCount;

        public int CreatedScopeCount => Volatile.Read(ref createdScopeCount);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref createdScopeCount);

            if (!scopes.TryDequeue(out var scope))
            {
                throw new InvalidOperationException(
                    "No existe un scope configurado para esta iteracion.");
            }

            return scope;
        }
    }

    private sealed class ProbeScope(object service) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } =
            new SingleServiceProvider(service);

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class SingleServiceProvider(object service)
        : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType.IsInstanceOfType(service)
                ? service
                : null;
        }
    }
}
