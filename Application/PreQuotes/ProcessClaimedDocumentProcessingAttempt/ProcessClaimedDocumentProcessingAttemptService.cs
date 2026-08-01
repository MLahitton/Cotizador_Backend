using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Storage;
using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessClaimedDocumentProcessingAttempt;

public enum ProcessClaimedDocumentProcessingAttemptResult
{
    Completed = 1,
    Failed = 2,
    NotFound = 3,
    InvalidState = 4,
    QueryError = 5,
    PersistenceError = 6
}

public interface IClaimedDocumentProcessingService
{
    Task<ProcessClaimedDocumentProcessingAttemptResult> ProcessAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken);
}

public sealed class ProcessClaimedDocumentProcessingAttemptService(
    IDocumentProcessingRepository repository,
    IFileStorage fileStorage,
    IDocumentProcessingClient client,
    IGlassTypeCatalogRepository glassCatalogRepository,
    TimeProvider timeProvider,
    IDocumentProcessingDiagnostics? diagnostics = null)
    : IClaimedDocumentProcessingService
{
    private const string AiServiceUnavailableCode = "AI_SERVICE_UNAVAILABLE";
    private const string AiServiceTimeoutCode = "AI_SERVICE_TIMEOUT";
    private const string AiInvalidResponseCode = "AI_INVALID_RESPONSE";
    private const string AiServiceErrorCode = "AI_SERVICE_ERROR";
    private const string DocumentStorageErrorCode = "DOCUMENT_STORAGE_ERROR";

    public async Task<ProcessClaimedDocumentProcessingAttemptResult> ProcessAsync(
        Guid processingAttemptId,
        CancellationToken cancellationToken)
    {
        DocumentProcessingWorkItem? workItem;

        try
        {
            workItem = await repository.FindProcessingWorkItemAsync(
                processingAttemptId,
                cancellationToken);
        }
        catch (DocumentProcessingQueryException)
        {
            return ProcessClaimedDocumentProcessingAttemptResult.QueryError;
        }

        if (workItem is null)
        {
            return ProcessClaimedDocumentProcessingAttemptResult.NotFound;
        }

        if (workItem.Attempt.ProcessingState
            != DocumentProcessingState.Processing)
        {
            return ProcessClaimedDocumentProcessingAttemptResult.InvalidState;
        }

        try
        {
            await using var content = await fileStorage.OpenReadAsync(
                workItem.Source.StorageKey,
                cancellationToken);
            var clientResult = await client.ProcessAsync(
                new DocumentProcessingClientRequest(
                    workItem.Source.DocumentId,
                    workItem.Attempt.Id,
                    workItem.Attempt.CorrelationId,
                    workItem.Source.OriginalFileName,
                    workItem.Source.SizeBytes,
                    content),
                cancellationToken);

            if (clientResult.IsSuccess
                && clientResult.Response is { } response)
            {
                return await CompleteAsync(
                    workItem.Attempt,
                    response,
                    cancellationToken);
            }

            return await FailAsync(
                workItem.Attempt,
                MapClientFailure(clientResult),
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidStorageKeyException)
        {
            return await FailAsync(
                workItem.Attempt,
                DocumentStorageErrorCode,
                cancellationToken);
        }
        catch (FileStorageReadException)
        {
            return await FailAsync(
                workItem.Attempt,
                DocumentStorageErrorCode,
                cancellationToken);
        }
    }

    private async Task<ProcessClaimedDocumentProcessingAttemptResult>
        CompleteAsync(
            DocumentProcessingAttempt attempt,
            DocumentProcessingResponseData response,
            CancellationToken cancellationToken)
    {
        if (response.StructuredExtraction is not { } structured)
        {
            return await FailAsync(
                attempt,
                AiInvalidResponseCode,
                cancellationToken);
        }

        IReadOnlyDictionary<string, GlassTypeCatalogReadModel> glassTypes =
            new Dictionary<string, GlassTypeCatalogReadModel>(StringComparer.Ordinal);
        if (response.SchemaVersion == "3.0")
        {
            IReadOnlyList<GlassTypeCatalogReadModel> catalog;
            try
            {
                catalog = await glassCatalogRepository
                    .GetActiveWithCurrentPriceRangesAsync(cancellationToken);
            }
            catch (GlassTypeCatalogQueryException)
            {
                diagnostics?.CatalogResolutionFailed(
                    response.DocumentId,
                    response.ProcessingAttemptId,
                    attempt.CorrelationId,
                    "query_error",
                    null);
                return await FailAsync(
                    attempt, AiInvalidResponseCode, cancellationToken);
            }
            glassTypes = catalog.ToDictionary(
                value => value.Code,
                value => value,
                StringComparer.Ordinal);
            if (structured.Items.Any(item => item.Glass is null
                || item.Glass.NormalizedCode is { } code
                    && !glassTypes.ContainsKey(code)))
            {
                var unknownCode = structured.Items
                    .Select(item => item.Glass?.NormalizedCode)
                    .FirstOrDefault(code => code is not null
                        && !glassTypes.ContainsKey(code));
                diagnostics?.CatalogResolutionFailed(
                    response.DocumentId,
                    response.ProcessingAttemptId,
                    attempt.CorrelationId,
                    unknownCode is null
                        ? "missing_glass_contract"
                        : "unknown_code",
                    unknownCode);
                return await FailAsync(
                    attempt, AiInvalidResponseCode, cancellationToken);
            }
        }
        else if (response.SchemaVersion != "2.0")
        {
            return await FailAsync(
                attempt, AiInvalidResponseCode, cancellationToken);
        }

        var completedAtUtc = timeProvider.GetUtcNow();
        var extractionResult = DocumentExtractionResult.Create(
            attempt.Id,
            response.SchemaVersion,
            response.Document.Classification,
            response.Document.RequiresOcr,
            response.Document.PageCount,
            response.ProcessingMetadata.Method,
            response.ProcessingMetadata.DurationMs,
            response.PayloadJson,
            completedAtUtc);

        attempt.Complete(response.Outcome, completedAtUtc);
        repository.AddResult(extractionResult);
        var structuredExtraction = StructuredDocumentExtraction.Create(
            extractionResult.Id,
            structured.Status,
            structured.ProjectName,
            structured.ClientName,
            structured.Location,
            structured.ItemCount,
            structured.DocumentReferenceCount,
            structured.ItemsRequiringReview,
            structured.KnownQuoteableUnitCount,
            structured.ProcessingMethod,
            structured.DurationMs,
            structured.Items.Select(x => new StructuredItemInput(
                x.Sequence, x.Reference, x.Description, x.ElementType,
                x.RawMeasurements, x.WidthMillimeters,
                x.HeightMillimeters, x.Quantity, x.RequiresReview,
                x.Glass is null ? null : new StructuredItemGlassInput(
                    x.Glass.NormalizedCode is { } code
                        && glassTypes.TryGetValue(code, out var glassType)
                        ? glassType.GlassTypeId
                        : null,
                    x.Glass.RawSpecification,
                    x.Glass.NormalizedCode,
                    x.Glass.AssignmentScope,
                    x.Glass.RequiresReview,
                    x.Glass.ReviewReasons,
                    x.Glass.SourcePages,
                    x.Glass.Evidence.Select((value, index) =>
                        new StructuredItemGlassEvidenceInput(
                            index + 1, value.PageNumber,
                            value.SourceType, value.Text)).ToArray()),
                response.SchemaVersion == "3.0"
                    ? CreateValuation(x, glassTypes)
                    : null)).ToArray(),
            structured.Requirements.Select((x, index) =>
                new StructuredRequirementInput(
                    index + 1, x.Category, x.Value)).ToArray(),
            structured.DocumentReferences.Select(x =>
                new StructuredDocumentReferenceInput(
                    x.Sequence, x.Reference, x.Description,
                    x.Detail, x.Quantity)).ToArray(),
            structured.Issues.Select(x => new StructuredIssueInput(
                x.Sequence, x.Code, x.Message, x.ItemSequence,
                [.. x.PageNumbers])).ToArray(),
            structured.Conflicts.Select(x => new StructuredConflictInput(
                x.Sequence, x.Code, x.Message,
                [.. x.ItemSequences], [.. x.PageNumbers])).ToArray(),
            completedAtUtc,
            structured.IdentifiedGlassItemCount,
            structured.GlassItemsRequiringReview);
        repository.AddStructuredExtraction(structuredExtraction);

        return await SaveTerminalAsync(
            cancellationToken,
            ProcessClaimedDocumentProcessingAttemptResult.Completed);
    }

    private static StructuredItemGlassValuationInput CreateValuation(
        StructuredItemData item,
        IReadOnlyDictionary<string, GlassTypeCatalogReadModel> catalog)
    {
        GlassValuationReason? reason = item.WidthMillimeters is null
            || item.HeightMillimeters is null
                ? GlassValuationReason.MissingMeasurements
            : item.Quantity is null
                ? GlassValuationReason.MissingQuantity
            : item.Glass?.NormalizedCode is null
                ? GlassValuationReason.GlassNotNormalized
            : !catalog.TryGetValue(item.Glass.NormalizedCode, out var glassType)
                ? GlassValuationReason.GlassTypeNotResolved
            : glassType.CurrentPriceRange is null
                ? GlassValuationReason.PriceRangeNotAvailable
                : null;
        if (reason is { } notValuedReason)
            return new(GlassValuationStatus.NotValued, notValuedReason,
                item.Glass?.NormalizedCode is { } code
                    && catalog.TryGetValue(code, out var known) ? known.GlassTypeId : null,
                null, null, null, null, null, null, null, null, null, null);

        var resolved = catalog[item.Glass!.NormalizedCode!];
        var range = resolved.CurrentPriceRange!;
        return StructuredExtractionItemGlassValuation.Calculate(
            item.WidthMillimeters!.Value,
            item.HeightMillimeters!.Value,
            item.Quantity!.Value,
            resolved.GlassTypeId,
            range.GlassPriceRangeVersionId, range.Version, range.Status,
            range.Currency,
            range.MinimumPricePerSquareMeter,
            range.MaximumPricePerSquareMeter);
    }

    private async Task<ProcessClaimedDocumentProcessingAttemptResult> FailAsync(
        DocumentProcessingAttempt attempt,
        string errorCode,
        CancellationToken cancellationToken)
    {
        attempt.Fail(errorCode, timeProvider.GetUtcNow());

        return await SaveTerminalAsync(
            cancellationToken,
            ProcessClaimedDocumentProcessingAttemptResult.Failed);
    }

    private async Task<ProcessClaimedDocumentProcessingAttemptResult>
        SaveTerminalAsync(
            CancellationToken cancellationToken,
            ProcessClaimedDocumentProcessingAttemptResult successResult)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return successResult;
        }
        catch (DocumentProcessingPersistenceException)
        {
            return ProcessClaimedDocumentProcessingAttemptResult
                .PersistenceError;
        }
    }

    private static string MapClientFailure(
        DocumentProcessingClientResult clientResult)
    {
        if (clientResult.Failure
                == DocumentProcessingClientFailure.RemoteRejection
            && clientResult.RemoteError is { } remoteError
            && IsRecognizedRemoteRejectionCode(remoteError.ErrorCode))
        {
            return remoteError.ErrorCode;
        }

        return clientResult.Failure switch
        {
            DocumentProcessingClientFailure.ServiceUnavailable =>
                AiServiceUnavailableCode,
            DocumentProcessingClientFailure.Timeout =>
                AiServiceTimeoutCode,
            DocumentProcessingClientFailure.ServiceError =>
                AiServiceErrorCode,
            _ => AiInvalidResponseCode
        };
    }

    private static bool IsRecognizedRemoteRejectionCode(
        string errorCode)
    {
        return errorCode is
            "INVALID_REQUEST"
            or "INVALID_CORRELATION_ID"
            or "EMPTY_FILE"
            or "INVALID_PDF"
            or "PDF_PASSWORD_REQUIRED"
            or "PDF_PAGE_LIMIT_EXCEEDED"
            or "FILE_TOO_LARGE"
            or "UNSUPPORTED_FILE_TYPE";
    }
}
