using Application.PreQuotes.ClaimDocumentProcessingAttempt;
using Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;
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

        using (var processingScope = scopeFactory.CreateScope())
        {
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

        return true;
    }
}
