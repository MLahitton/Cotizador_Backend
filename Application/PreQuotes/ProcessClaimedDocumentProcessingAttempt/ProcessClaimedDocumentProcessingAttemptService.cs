using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Storage;
using Domain.Catalogs;
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
    IProductSystemCatalogRepository productSystemCatalogRepository,
    IFrameTypeCatalogRepository frameTypeCatalogRepository,
    IFinishTypeCatalogRepository finishTypeCatalogRepository,
    ICatalogAliasRepository catalogAliasRepository,
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
            var relatedSources = await repository
                .ListDocumentSourcesByPreQuoteIdAsync(
                    workItem.Source.PreQuoteId,
                    cancellationToken);
            if (relatedSources.Count == 0)
            {
                relatedSources = [workItem.Source];
            }

            var openedStreams = new List<Stream>(relatedSources.Count);
            try
            {
                foreach (var source in relatedSources)
                {
                    openedStreams.Add(await fileStorage.OpenReadAsync(
                        source.StorageKey,
                        cancellationToken));
                }

                var files = relatedSources.Select((source, index) =>
                    new DocumentProcessingFile(
                        source.DocumentId,
                        source.OriginalFileName,
                        source.ContentType,
                        source.SizeBytes,
                        openedStreams[index])).ToArray();
            var clientResult = await client.ProcessAsync(
                new DocumentProcessingClientRequest(
                    workItem.Source.DocumentId,
                    workItem.Attempt.Id,
                    workItem.Attempt.CorrelationId,
                    files,
                    workItem.Source.ProjectId,
                    workItem.Source.PreQuoteId),
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
            finally
            {
                foreach (var stream in openedStreams)
                {
                    await stream.DisposeAsync();
                }
            }
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
        var hasNormalizedGlass = structured.Items.Any(
            item => item.Glass?.NormalizedCode is not null);
        if (response.RequiresResolvedGlassCatalog
            || response.SupportsPreliminaryValuation
            || hasNormalizedGlass)
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
            var invalidGlassContractItem = structured.Items.FirstOrDefault(
                item => response.RequiresResolvedGlassCatalog
                        && item.Glass is null
                    || item.Glass?.NormalizedCode is { } code
                        && !glassTypes.ContainsKey(code));
            if (invalidGlassContractItem is not null)
            {
                var unknownCode = invalidGlassContractItem.Glass?.NormalizedCode;
                var acceptedNormalizedCodes = unknownCode is null
                    ? null
                    : glassTypes.Keys
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray();
                diagnostics?.CatalogResolutionFailed(
                    response.DocumentId,
                    response.ProcessingAttemptId,
                    attempt.CorrelationId,
                    unknownCode is null
                        ? "missing_glass_contract"
                        : "unknown_code",
                    unknownCode,
                    invalidGlassContractItem.Sequence,
                    acceptedNormalizedCodes);
                return await FailAsync(
                    attempt, AiInvalidResponseCode, cancellationToken);
            }
        }

        IReadOnlyDictionary<string, ProductSystemCatalogReadModel> systems;
        IReadOnlyDictionary<string, FrameTypeCatalogReadModel> frames;
        IReadOnlyDictionary<string, FinishTypeCatalogReadModel> finishes;
        IReadOnlyDictionary<(CatalogAliasCategory, string), CatalogAliasReadModel>
            aliases;
        try
        {
            systems = (await productSystemCatalogRepository
                    .ListActiveAsync(cancellationToken))
                .Where(value => value.ActiveForRecognition)
                .ToDictionary(value => value.Code, StringComparer.Ordinal);
            frames = (await frameTypeCatalogRepository
                    .ListActiveAsync(cancellationToken))
                .ToDictionary(value => value.Code, StringComparer.Ordinal);
            finishes = (await finishTypeCatalogRepository
                    .ListActiveAsync(cancellationToken))
                .ToDictionary(value => value.Code, StringComparer.Ordinal);
            aliases = (await catalogAliasRepository
                    .ListActiveAsync(cancellationToken))
                .ToDictionary(
                    value => (value.Category, value.NormalizedAlias),
                    value => value);
        }
        catch (CanonicalCatalogQueryException)
        {
            diagnostics?.CatalogResolutionFailed(
                response.DocumentId,
                response.ProcessingAttemptId,
                attempt.CorrelationId,
                "canonical_query_error",
                null);
            return await FailAsync(
                attempt, AiInvalidResponseCode, cancellationToken);
        }

        var enrichedItems = structured.Items
            .Select(item => ResolveTechnicalClassification(
                item, systems, frames, finishes, aliases))
            .ToArray();

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
            enrichedItems.Count(x => x.RequiresReview),
            structured.KnownQuoteableUnitCount,
            structured.ProcessingMethod,
            structured.DurationMs,
            enrichedItems.Select(resolved => new StructuredItemInput(
                resolved.Item.Sequence, resolved.Item.Reference,
                resolved.Item.Description, resolved.Item.ElementType,
                resolved.Item.RawMeasurements, resolved.Item.WidthMillimeters,
                resolved.Item.HeightMillimeters, resolved.Item.Quantity,
                resolved.RequiresReview,
                resolved.Item.Glass is null ? null : new StructuredItemGlassInput(
                    resolved.Item.Glass.NormalizedCode is { } code
                        && glassTypes.TryGetValue(code, out var glassType)
                        ? glassType.GlassTypeId
                        : null,
                    resolved.Item.Glass.RawSpecification,
                    resolved.Item.Glass.NormalizedCode,
                    resolved.Item.Glass.AssignmentScope,
                    resolved.Item.Glass.RequiresReview,
                    resolved.Item.Glass.ReviewReasons,
                    resolved.Item.Glass.SourcePages,
                    resolved.Item.Glass.Evidence.Select((value, index) =>
                        new StructuredItemGlassEvidenceInput(
                            index + 1, value.PageNumber,
                            value.SourceType, value.Text,
                            value.SheetName, value.CellRange)).ToArray()),
                (response.SupportsPreliminaryValuation || hasNormalizedGlass)
                    && !resolved.IsNotPriceable
                    ? CreateValuation(resolved.Item, glassTypes)
                    : null,
                resolved.TechnicalClassification is null
                    ? null
                    : new StructuredItemTechnicalClassificationInput(
                        resolved.TechnicalClassification.SystemCode,
                        resolved.TechnicalClassification.SystemOriginalText,
                        resolved.TechnicalClassification.SystemSource,
                        resolved.TechnicalClassification.SystemConfidence,
                        resolved.TechnicalClassification.FrameCode,
                        resolved.TechnicalClassification.FrameOriginalText,
                        resolved.TechnicalClassification.FrameSource,
                        resolved.TechnicalClassification.FrameConfidence,
                        resolved.TechnicalClassification.FinishCode,
                        resolved.TechnicalClassification.FinishOriginalText,
                        resolved.TechnicalClassification.FinishSource,
                        resolved.TechnicalClassification.FinishConfidence,
                        resolved.TechnicalClassification.RequiresReview,
                        resolved.TechnicalClassification.ReviewReasons)))
                .ToArray(),
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
                null, null, null, null, null, null, null, null, null, null,
                null, null);

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
            range.ExpectedAmountPerM2,
            range.MaximumPricePerSquareMeter);
    }

    private static ResolvedStructuredItem ResolveTechnicalClassification(
        StructuredItemData item,
        IReadOnlyDictionary<string, ProductSystemCatalogReadModel> systems,
        IReadOnlyDictionary<string, FrameTypeCatalogReadModel> frames,
        IReadOnlyDictionary<string, FinishTypeCatalogReadModel> finishes,
        IReadOnlyDictionary<(CatalogAliasCategory, string), CatalogAliasReadModel>
            aliases)
    {
        var input = item.TechnicalClassification;
        var reasons = new List<string>();
        var system = ResolveCatalogPart(
            CatalogAliasCategory.System,
            input?.SystemCode,
            input?.SystemOriginalText,
            input?.SystemConfidence,
            systems.ContainsKey,
            aliases,
            "UNKNOWN_SYSTEM_CODE");
        var frame = ResolveCatalogPart(
            CatalogAliasCategory.Frame,
            input?.FrameCode,
            input?.FrameOriginalText,
            input?.FrameConfidence,
            frames.ContainsKey,
            aliases,
            "UNKNOWN_FRAME_CODE");
        var finish = ResolveCatalogPart(
            CatalogAliasCategory.Finish,
            input?.FinishCode,
            input?.FinishOriginalText,
            input?.FinishConfidence,
            finishes.ContainsKey,
            aliases,
            "UNKNOWN_FINISH_CODE");

        reasons.AddRange(system.ReviewReasons);
        reasons.AddRange(frame.ReviewReasons);
        reasons.AddRange(finish.ReviewReasons);
        reasons.AddRange(input?.ReviewReasons ?? []);

        if (system.Code is null)
        {
            var inferredCode = item.ElementType switch
            {
                StructuredElementType.Railing => "BARANDA",
                StructuredElementType.ShowerDivision => "DIVISION_BANO",
                _ => null
            };
            if (inferredCode is not null && systems.ContainsKey(inferredCode))
            {
                system = system with
                {
                    Code = inferredCode,
                    Source = TechnicalClassificationSource.Inferred,
                    Confidence = 1m
                };
                reasons.Add("ELEMENT_TYPE_INFERRED_SYSTEM");
            }
        }

        var notPriceable = system.Code is ("BARANDA" or "DIVISION_BANO")
            && systems.TryGetValue(system.Code, out var productSystem)
            && !productSystem.Priceable;
        if (notPriceable)
        {
            reasons.Add("SYSTEM_NOT_CURRENTLY_PRICEABLE");
        }
        if (system.Code is { } systemCode
            && systems.TryGetValue(systemCode, out var resolvedSystem)
            && resolvedSystem.RequiresReview)
        {
            reasons.Add("SYSTEM_REQUIRES_REVIEW");
        }
        if (finish.Code is { } finishCode
            && finishes.TryGetValue(finishCode, out var resolvedFinish)
            && resolvedFinish.RequiresReview)
        {
            reasons.Add("FINISH_REQUIRES_REVIEW");
        }

        var distinctReasons = reasons
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hasTechnicalData = system.HasData || frame.HasData
            || finish.HasData || distinctReasons.Length > 0;
        var technical = hasTechnicalData
            ? new StructuredItemTechnicalClassificationData(
                system.Code,
                system.OriginalText,
                system.Source,
                system.Confidence,
                frame.Code,
                frame.OriginalText,
                frame.Source,
                frame.Confidence,
                finish.Code,
                finish.OriginalText,
                finish.Source,
                finish.Confidence,
                distinctReasons.Length > 0,
                distinctReasons)
            : null;

        return new ResolvedStructuredItem(
            item,
            technical,
            notPriceable,
            item.RequiresReview || distinctReasons.Length > 0);
    }

    private static ResolvedCatalogPart ResolveCatalogPart(
        CatalogAliasCategory category,
        string? code,
        string? originalText,
        decimal? confidence,
        Func<string, bool> exists,
        IReadOnlyDictionary<(CatalogAliasCategory, string), CatalogAliasReadModel>
            aliases,
        string unknownReason)
    {
        var normalizedCode = NormalizeCode(code);
        var normalizedOriginal = NormalizeText(originalText);
        if (normalizedCode is not null)
        {
            return exists(normalizedCode)
                ? new ResolvedCatalogPart(
                    normalizedCode,
                    normalizedOriginal,
                    TechnicalClassificationSource.Explicit,
                    confidence ?? 1m,
                    [])
                : new ResolvedCatalogPart(
                    null,
                    normalizedOriginal,
                    TechnicalClassificationSource.Unresolved,
                    0m,
                    [unknownReason]);
        }

        if (normalizedOriginal is null)
        {
            return new ResolvedCatalogPart(
                null, null, null, null, []);
        }

        var normalizedAlias = CatalogAliasNormalizer.Normalize(normalizedOriginal);
        if (aliases.TryGetValue((category, normalizedAlias), out var alias)
            && exists(alias.CanonicalCode))
        {
            return new ResolvedCatalogPart(
                alias.CanonicalCode,
                normalizedOriginal,
                TechnicalClassificationSource.Alias,
                alias.Confidence,
                []);
        }

        return new ResolvedCatalogPart(
            null,
            normalizedOriginal,
            TechnicalClassificationSource.Unresolved,
            0m,
            [unknownReason]);
    }

    private static string? NormalizeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var code = value.Trim().ToUpperInvariant();
        return code.Length <= 30
            && code.All(character =>
                character is >= 'A' and <= 'Z'
                || character is >= '0' and <= '9'
                || character is '_' or '-')
            ? code
            : null;
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private sealed record ResolvedStructuredItem(
        StructuredItemData Item,
        StructuredItemTechnicalClassificationData? TechnicalClassification,
        bool IsNotPriceable,
        bool RequiresReview);

    private sealed record ResolvedCatalogPart(
        string? Code,
        string? OriginalText,
        TechnicalClassificationSource? Source,
        decimal? Confidence,
        IReadOnlyList<string> ReviewReasons)
    {
        public bool HasData => Code is not null || OriginalText is not null
            || Source is not null || Confidence is not null
            || ReviewReasons.Count > 0;
    }
}
