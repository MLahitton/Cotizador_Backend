using Application.PreQuotes.ClaimDocumentProcessingAttempt;
using Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;
using Application.PreQuotes.RecoverClaimedDocumentProcessingAttempt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.DocumentProcessing;

public sealed class DocumentProcessingWorker(
    IServiceScopeFactory scopeFactory,
    DocumentProcessingWorkerOptions options,
    ILogger<DocumentProcessingWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        logger.LogInformation("Document processing worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hadWork = await ExecuteIterationAsync(stoppingToken);

                if (!hadWork)
                {
                    await Task.Delay(options.PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Document processing worker iteration failed.");
                await Task.Delay(options.PollInterval, stoppingToken);
            }
        }

        logger.LogInformation("Document processing worker stopped.");
    }

    public async Task<bool> ExecuteIterationAsync(
        CancellationToken cancellationToken)
    {
        Guid? processingAttemptId;

        using (var claimScope = scopeFactory.CreateScope())
        {
            var claimService = claimScope.ServiceProvider
                .GetRequiredService<IDocumentProcessingClaimService>();
            processingAttemptId = await claimService.ClaimNextAsync(
                cancellationToken);
        }

        if (processingAttemptId is null)
        {
            logger.LogDebug("No pending document processing attempts.");
            return false;
        }

        try
        {
            using var processingScope = scopeFactory.CreateScope();
            var processingService = processingScope.ServiceProvider
                .GetRequiredService<IClaimedDocumentProcessingService>();
            var result = await processingService.ProcessAsync(
                processingAttemptId.Value,
                cancellationToken);
            logger.LogInformation(
                "Document processing attempt handled. ProcessingAttemptId={ProcessingAttemptId} Result={Result}",
                processingAttemptId,
                result);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception originalException)
        {
            const string stage = "processing_pipeline";
            logger.LogError(
                originalException,
                "Unexpected document processing failure. ProcessingAttemptId={ProcessingAttemptId} Stage={Stage} ExceptionType={ExceptionType}",
                processingAttemptId.Value,
                stage,
                originalException.GetType().Name);

            try
            {
                using var recoveryScope = scopeFactory.CreateScope();
                var recoveryService = recoveryScope.ServiceProvider
                    .GetRequiredService<
                        IClaimedDocumentProcessingRecoveryService>();
                var recoveryResult = await recoveryService.RecoverAsync(
                    processingAttemptId.Value,
                    CancellationToken.None);
                logger.LogInformation(
                    "Document processing recovery handled. ProcessingAttemptId={ProcessingAttemptId} Stage={Stage} Result={Result}",
                    processingAttemptId.Value,
                    "terminal_recovery",
                    recoveryResult);
            }
            catch (Exception recoveryException)
            {
                logger.LogError(
                    recoveryException,
                    "Document processing recovery failed. ProcessingAttemptId={ProcessingAttemptId} Stage={Stage} ExceptionType={ExceptionType}",
                    processingAttemptId.Value,
                    "terminal_recovery",
                    recoveryException.GetType().Name);
            }

            throw;
        }

        return true;
    }
}
