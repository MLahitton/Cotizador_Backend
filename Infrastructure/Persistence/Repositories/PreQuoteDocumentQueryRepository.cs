using System.Data.Common;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence.Repositories;

public sealed class PreQuoteDocumentQueryRepository(
    ApplicationDbContext dbContext,
    ILogger<PreQuoteDocumentQueryRepository>? logger = null)
    : IPreQuoteDocumentQueryRepository
{
    public async Task<PreQuoteDocumentsPageReadModel?> GetDocumentsAsync(
        Guid preQuoteId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await dbContext.PreQuotes.AsNoTracking().AnyAsync(
                    preQuote => preQuote.Id == preQuoteId,
                    cancellationToken))
            {
                return null;
            }

            var query = dbContext.PreQuoteDocuments
                .AsNoTracking()
                .Where(document => document.PreQuoteId == preQuoteId);
            var totalCount = await query.CountAsync(cancellationToken);
            var offset = (long)(page - 1) * pageSize;

            if (offset > int.MaxValue)
            {
                return new PreQuoteDocumentsPageReadModel([], totalCount);
            }

            var rows = await query
                .OrderByDescending(document => document.CreatedAtUtc)
                .ThenByDescending(document => document.Id)
                .Skip((int)offset)
                .Take(pageSize)
                .Select(document => new DocumentListProjection(
                    document.Id,
                    document.PreQuoteId,
                    document.OriginalFileName,
                    document.ContentType,
                    document.SizeBytes,
                    document.CreatedAtUtc,
                    dbContext.DocumentProcessingAttempts
                        .Where(attempt =>
                            attempt.PreQuoteDocumentId == document.Id)
                        .OrderByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt => new AttemptProjection(
                            attempt.Id,
                            attempt.ProcessingState,
                            attempt.Outcome,
                            attempt.ErrorCode,
                            attempt.CreatedAtUtc,
                            attempt.StartedAtUtc,
                            attempt.CompletedAtUtc,
                            attempt.ExtractionResult == null
                                ? null
                                : new ResultMetadataProjection(
                                    attempt.ExtractionResult.SchemaVersion,
                                    attempt.ExtractionResult.Classification,
                                    attempt.ExtractionResult.RequiresOcr,
                                    attempt.ExtractionResult.PageCount,
                                    attempt.ExtractionResult.ProcessingMethod,
                                    attempt.ExtractionResult.DurationMs)))
                        .FirstOrDefault(),
                    dbContext.DocumentProcessingAttempts
                        .Where(attempt =>
                            attempt.PreQuoteDocumentId == document.Id
                            && attempt.ProcessingState
                                == DocumentProcessingState.Finished
                            && (attempt.Outcome
                                    == DocumentProcessingOutcome.Completed
                                || attempt.Outcome
                                    == DocumentProcessingOutcome.RequiresReview)
                            && attempt.ExtractionResult != null
                            && (attempt.ExtractionResult.SchemaVersion == "2.0"
                                || attempt.ExtractionResult.SchemaVersion == "3.0")
                            && attempt.ExtractionResult.StructuredExtraction
                                != null)
                        .OrderByDescending(attempt => attempt.CompletedAtUtc)
                        .ThenByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt => new ExtractionSummaryProjection(
                            attempt.ExtractionResult!.StructuredExtraction!.Id,
                            attempt.Id,
                            attempt.ExtractionResult.StructuredExtraction.Status,
                            attempt.ExtractionResult.StructuredExtraction.ProjectName,
                            attempt.ExtractionResult.StructuredExtraction.ClientName,
                            attempt.ExtractionResult.StructuredExtraction.Location,
                            attempt.ExtractionResult.StructuredExtraction.ItemCount,
                            attempt.ExtractionResult.StructuredExtraction.DocumentReferenceCount,
                            attempt.ExtractionResult.StructuredExtraction.ItemsRequiringReview,
                            attempt.ExtractionResult.StructuredExtraction.KnownQuoteableUnitCount,
                            attempt.ExtractionResult.StructuredExtraction.Issues.Count,
                            attempt.ExtractionResult.StructuredExtraction.Conflicts.Count,
                            attempt.ExtractionResult.StructuredExtraction.ProcessingMethod,
                            attempt.ExtractionResult.StructuredExtraction.DurationMs,
                            attempt.ExtractionResult.StructuredExtraction.CreatedAtUtc))
                        .FirstOrDefault()))
                .ToArrayAsync(cancellationToken);

            return new PreQuoteDocumentsPageReadModel(
                rows.Select(MapListItem).ToArray(),
                totalCount);
        }
        catch (Exception exception) when (
            exception is DbException or InvalidDataException)
        {
            throw new PreQuoteDocumentQueryException(exception);
        }
    }

    public async Task<StructuredDocumentExtractionQueryReadModel?>
        GetStructuredExtractionAsync(
            Guid documentId,
            Guid userId,
            CancellationToken cancellationToken)
    {
        AvailableExtractionProjection? extraction = null;
        var assemblyStage = "document_lookup";
        try
        {
            var document = await dbContext.PreQuoteDocuments
                .AsNoTracking()
                .Where(entity => entity.Id == documentId
                    && entity.PreQuote.Project.CreatedByUserId == userId)
                .Select(entity => new PreQuoteDocumentReadModel(
                    entity.Id,
                    entity.PreQuoteId,
                    entity.OriginalFileName,
                    entity.ContentType,
                    entity.SizeBytes,
                    entity.CreatedAtUtc))
                .SingleOrDefaultAsync(cancellationToken);

            if (document is null)
            {
                return null;
            }

            assemblyStage = "latest_attempt_query";
            var latest = await LatestAttemptQuery(documentId)
                .FirstOrDefaultAsync(cancellationToken);
            assemblyStage = "available_extraction_query";
            extraction = await AvailableExtractionQuery(documentId)
                .FirstOrDefaultAsync(cancellationToken);
            StructuredExtractionDetailsReadModel? details = null;

            if (extraction is not null)
            {
                assemblyStage = "load_structured_extraction_details";
                details = await LoadDetailsAsync(
                    document.DocumentId,
                    extraction,
                    latest?.ProcessingAttemptId,
                    cancellationToken);
            }

            return new StructuredDocumentExtractionQueryReadModel(
                document,
                ResolveAvailability(latest, extraction?.ProcessingAttemptId),
                MapAttempt(latest),
                details);
        }
        catch (PreQuoteDocumentQueryException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is DbException
                or InvalidDataException
                or InvalidOperationException
                or System.Text.Json.JsonException)
        {
            logger?.LogError(
                exception,
                "Structured extraction query failed for document {DocumentId}, structured extraction {StructuredExtractionId}, stage {Stage}.",
                documentId,
                extraction?.ExtractionId,
                assemblyStage);
            throw new PreQuoteDocumentQueryException(exception);
        }
    }

    private IQueryable<AttemptProjection> LatestAttemptQuery(Guid documentId) =>
        dbContext.DocumentProcessingAttempts
            .AsNoTracking()
            .Where(attempt => attempt.PreQuoteDocumentId == documentId)
            .OrderByDescending(attempt => attempt.CreatedAtUtc)
            .ThenByDescending(attempt => attempt.Id)
            .Select(attempt => new AttemptProjection(
                attempt.Id,
                attempt.ProcessingState,
                attempt.Outcome,
                attempt.ErrorCode,
                attempt.CreatedAtUtc,
                attempt.StartedAtUtc,
                attempt.CompletedAtUtc,
                attempt.ExtractionResult == null
                    ? null
                    : new ResultMetadataProjection(
                        attempt.ExtractionResult.SchemaVersion,
                        attempt.ExtractionResult.Classification,
                        attempt.ExtractionResult.RequiresOcr,
                        attempt.ExtractionResult.PageCount,
                        attempt.ExtractionResult.ProcessingMethod,
                        attempt.ExtractionResult.DurationMs)));

    private IQueryable<AvailableExtractionProjection> AvailableExtractionQuery(
        Guid documentId) =>
        dbContext.DocumentProcessingAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.PreQuoteDocumentId == documentId
                && attempt.ProcessingState == DocumentProcessingState.Finished
                && (attempt.Outcome == DocumentProcessingOutcome.Completed
                    || attempt.Outcome
                        == DocumentProcessingOutcome.RequiresReview)
                && attempt.ExtractionResult != null
                && (attempt.ExtractionResult.SchemaVersion == "2.0"
                    || attempt.ExtractionResult.SchemaVersion == "3.0")
                && attempt.ExtractionResult.StructuredExtraction != null)
            .OrderByDescending(attempt => attempt.CompletedAtUtc)
            .ThenByDescending(attempt => attempt.CreatedAtUtc)
            .ThenByDescending(attempt => attempt.Id)
            .Select(attempt => new AvailableExtractionProjection(
                attempt.Id,
                attempt.ExtractionResult!.Id,
                attempt.ExtractionResult.SchemaVersion,
                attempt.ExtractionResult.PageCount,
                attempt.ExtractionResult.PayloadJson,
                attempt.ExtractionResult.StructuredExtraction!.Id,
                attempt.ExtractionResult.StructuredExtraction.Status,
                attempt.ExtractionResult.StructuredExtraction.ProjectName,
                attempt.ExtractionResult.StructuredExtraction.ClientName,
                attempt.ExtractionResult.StructuredExtraction.Location,
                attempt.ExtractionResult.StructuredExtraction.ItemCount,
                attempt.ExtractionResult.StructuredExtraction.DocumentReferenceCount,
                attempt.ExtractionResult.StructuredExtraction.ItemsRequiringReview,
                attempt.ExtractionResult.StructuredExtraction.KnownQuoteableUnitCount,
                attempt.ExtractionResult.StructuredExtraction.IdentifiedGlassItemCount,
                attempt.ExtractionResult.StructuredExtraction.GlassItemsRequiringReview,
                attempt.ExtractionResult.StructuredExtraction.ProcessingMethod,
                attempt.ExtractionResult.StructuredExtraction.DurationMs,
                attempt.ExtractionResult.StructuredExtraction.CreatedAtUtc));

    private async Task<StructuredExtractionDetailsReadModel> LoadDetailsAsync(
        Guid expectedDocumentId,
        AvailableExtractionProjection extraction,
        Guid? latestAttemptId,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.Set<StructuredExtractionItem>()
            .AsNoTracking()
            .Where(item =>
                item.StructuredDocumentExtractionId == extraction.ExtractionId)
            .OrderBy(item => item.Sequence)
            .Select(item => new PersistedItem(
                item.Sequence, item.Reference, item.Description,
                item.ElementType, item.RawMeasurements,
                item.WidthMillimeters, item.HeightMillimeters,
                item.Quantity, item.RequiresReview))
            .ToArrayAsync(cancellationToken);
        var requirements = await dbContext.Set<StructuredExtractionRequirement>()
            .AsNoTracking()
            .Where(item =>
                item.StructuredDocumentExtractionId == extraction.ExtractionId)
            .OrderBy(item => item.Sequence)
            .Select(item => new PersistedRequirement(
                item.Sequence, item.Category, item.Value))
            .ToArrayAsync(cancellationToken);
        var references = await dbContext
            .Set<StructuredExtractionDocumentReference>()
            .AsNoTracking()
            .Where(item =>
                item.StructuredDocumentExtractionId == extraction.ExtractionId)
            .OrderBy(item => item.Sequence)
            .Select(item => new PersistedReference(
                item.Sequence, item.Reference, item.Description,
                item.Detail, item.Quantity))
            .ToArrayAsync(cancellationToken);
        var issues = await dbContext.Set<StructuredExtractionIssue>()
            .AsNoTracking()
            .Where(item =>
                item.StructuredDocumentExtractionId == extraction.ExtractionId)
            .OrderBy(item => item.Sequence)
            .Select(item => new PersistedIssue(
                item.Sequence, item.Code, item.Message,
                item.ItemSequence, item.PageNumbers))
            .ToArrayAsync(cancellationToken);
        var conflicts = await dbContext.Set<StructuredExtractionConflict>()
            .AsNoTracking()
            .Where(item =>
                item.StructuredDocumentExtractionId == extraction.ExtractionId)
            .OrderBy(item => item.Sequence)
            .Select(item => new PersistedConflict(
                item.Sequence, item.Code, item.Message,
                item.ItemSequences, item.PageNumbers))
            .ToArrayAsync(cancellationToken);
        var glasses = await dbContext.StructuredExtractionItemGlassDetections
            .AsNoTracking()
            .Where(value => value.StructuredExtractionItem
                .StructuredDocumentExtractionId == extraction.ExtractionId)
            .OrderBy(value => value.StructuredExtractionItem.Sequence)
            .Select(value => new PersistedGlass(
                value.StructuredExtractionItem.Sequence,
                value.GlassTypeId,
                value.RawSpecification,
                value.NormalizedCodeSnapshot,
                value.AssignmentScope,
                value.RequiresReview,
                value.ReviewReasons.OrderBy(reason => reason.Sequence)
                    .Select(reason => reason.Code).ToArray(),
                value.SourcePages.OrderBy(page => page.Sequence)
                    .Select(page => page.PageNumber).ToArray(),
                value.Evidence.OrderBy(evidence => evidence.Sequence)
                    .Select(evidence => new PersistedGlassEvidence(
                        evidence.PageNumber,
                        evidence.SourceType,
                        evidence.Text)).ToArray()))
            .ToArrayAsync(cancellationToken);

        var valuations = await dbContext.StructuredExtractionItemGlassValuations
            .AsNoTracking()
            .Where(value => value.StructuredExtractionItem
                .StructuredDocumentExtractionId == extraction.ExtractionId)
            .OrderBy(value => value.StructuredExtractionItem.Sequence)
            .Select(value => new PersistedValuation(
                value.StructuredExtractionItem.Sequence,
                value.Status, value.Reason, value.GlassTypeId,
                value.GlassPriceRangeVersionId, value.PriceRangeVersion,
                value.PriceRangeStatus, value.Currency,
                value.UnitAreaSquareMeters, value.TotalAreaSquareMeters,
                value.MinimumPricePerSquareMeter,
                value.ExpectedPricePerSquareMeter,
                value.MaximumPricePerSquareMeter,
                value.MinimumAmount, value.ExpectedAmount, value.MaximumAmount,
                value.CalculatedAtUtc))
            .ToArrayAsync(cancellationToken);
        var technicalClassifications = await dbContext
            .StructuredExtractionItemTechnicalClassifications
            .AsNoTracking()
            .Where(value => value.StructuredExtractionItem
                .StructuredDocumentExtractionId == extraction.ExtractionId)
            .OrderBy(value => value.StructuredExtractionItem.Sequence)
            .Select(value => new PersistedTechnicalClassification(
                value.StructuredExtractionItem.Sequence,
                value.SystemCode,
                value.SystemOriginalText,
                value.SystemSource,
                value.SystemConfidence,
                value.FrameCode,
                value.FrameOriginalText,
                value.FrameSource,
                value.FrameConfidence,
                value.FinishCode,
                value.FinishOriginalText,
                value.FinishSource,
                value.FinishConfidence,
                value.RequiresReview,
                value.ReviewReasons))
            .ToArrayAsync(cancellationToken);

        var details = StructuredExtractionPayloadReader.Read(
            expectedDocumentId,
            extraction,
            latestAttemptId,
            items,
            requirements,
            references,
            issues,
            conflicts,
            glasses,
            technicalClassifications);
        var valuationBySequence = valuations
            .GroupBy(x => x.ItemSequence)
            .ToDictionary(x => x.Key, x => x.Single());
        var technicalBySequence = technicalClassifications.ToDictionary(
            x => x.ItemSequence);
        var mappedItems = details.Items.Select(item => item with
        {
            Valuation = valuationBySequence.TryGetValue(
                item.Sequence, out var value)
                ? new StructuredItemGlassValuationReadModel(
                    value.Status, value.Reason, value.GlassTypeId,
                    value.GlassPriceRangeVersionId, value.PriceRangeVersion,
                    value.PriceRangeStatus, value.Currency,
                    value.UnitAreaSquareMeters, value.TotalAreaSquareMeters,
                    value.MinimumPricePerSquareMeter,
                    value.ExpectedPricePerSquareMeter,
                    value.MaximumPricePerSquareMeter,
                    value.MinimumAmount, value.ExpectedAmount,
                    value.MaximumAmount,
                    value.CalculatedAtUtc)
                : null,
            TechnicalClassification = technicalBySequence.TryGetValue(
                item.Sequence, out var technical)
                ? new StructuredItemTechnicalClassificationReadModel(
                    technical.SystemCode,
                    technical.SystemOriginalText,
                    technical.SystemSource,
                    technical.SystemConfidence,
                    technical.FrameCode,
                    technical.FrameOriginalText,
                    technical.FrameSource,
                    technical.FrameConfidence,
                    technical.FinishCode,
                    technical.FinishOriginalText,
                    technical.FinishSource,
                    technical.FinishConfidence,
                    technical.RequiresReview,
                    technical.ReviewReasons)
                : null
        }).ToArray();
        var valued = mappedItems.Where(x =>
            x.Valuation?.Status == GlassValuationStatus.Valued).ToArray();
        var currencies = valued.Select(x => x.Valuation!.Currency)
            .Distinct(StringComparer.Ordinal).ToArray();
        var aggregateable = currencies.Length <= 1;
        return details with
        {
            Items = mappedItems,
            Summary = details.Summary with
            {
                ValuedItemCount = valued.Length,
                NotValuedItemCount = mappedItems.Count(x =>
                    x.Valuation?.Status == GlassValuationStatus.NotValued),
                TotalGlassAreaSquareMeters = valued.Sum(x =>
                    x.Valuation!.TotalAreaSquareMeters ?? 0),
                MinimumGlassAmount = aggregateable
                    ? valued.Sum(x => x.Valuation!.MinimumAmount ?? 0) : null,
                MaximumGlassAmount = aggregateable
                    ? valued.Sum(x => x.Valuation!.MaximumAmount ?? 0) : null,
                Currency = aggregateable ? currencies.SingleOrDefault() : null,
                IsAggregable = aggregateable,
                AggregationIssue = aggregateable ? null : "CURRENCY_MISMATCH"
            }
        };
    }

    private static PreQuoteDocumentListReadModel MapListItem(
        DocumentListProjection row)
    {
        var latest = MapAttempt(row.LatestAttempt);
        var summary = row.Extraction is null
            ? null
            : new StructuredExtractionSummaryReadModel(
                row.Extraction.ExtractionId,
                row.Extraction.ProcessingAttemptId,
                latest?.ProcessingAttemptId
                    == row.Extraction.ProcessingAttemptId,
                row.Extraction.Status,
                row.Extraction.ProjectName,
                row.Extraction.ClientName,
                row.Extraction.Location,
                row.Extraction.ItemCount,
                row.Extraction.DocumentReferenceCount,
                row.Extraction.ItemsRequiringReview,
                row.Extraction.KnownQuoteableUnitCount,
                row.Extraction.IssueCount,
                row.Extraction.ConflictCount,
                row.Extraction.ProcessingMethod,
                row.Extraction.DurationMs,
                row.Extraction.CreatedAtUtc);

        return new PreQuoteDocumentListReadModel(
            row.DocumentId,
            row.PreQuoteId,
            row.OriginalFileName,
            row.ContentType,
            row.SizeBytes,
            row.CreatedAtUtc,
            ResolveAvailability(
                row.LatestAttempt,
                row.Extraction?.ProcessingAttemptId),
            latest,
            summary);
    }

    private static DocumentProcessingAttemptSummaryReadModel? MapAttempt(
        AttemptProjection? attempt) =>
        attempt is null
            ? null
            : new DocumentProcessingAttemptSummaryReadModel(
                attempt.ProcessingAttemptId,
                attempt.ProcessingState,
                attempt.Outcome,
                attempt.ErrorCode,
                attempt.CreatedAtUtc,
                attempt.StartedAtUtc,
                attempt.CompletedAtUtc,
                attempt.Result is null
                    ? null
                    : new DocumentExtractionResultMetadataReadModel(
                        attempt.Result.SchemaVersion,
                        attempt.Result.Classification,
                        attempt.Result.RequiresOcr,
                        attempt.Result.PageCount,
                        attempt.Result.ProcessingMethod,
                        attempt.Result.DurationMs));

    internal static DocumentProcessingAvailability ResolveAvailability(
        AttemptProjection? latest,
        Guid? extractionAttemptId)
    {
        if (latest is null)
        {
            return DocumentProcessingAvailability.NotProcessed;
        }

        if (extractionAttemptId is Guid availableAttemptId)
        {
            return availableAttemptId == latest.ProcessingAttemptId
                ? DocumentProcessingAvailability.AvailableCurrent
                : DocumentProcessingAvailability.AvailablePrevious;
        }

        return latest.ProcessingState switch
        {
            DocumentProcessingState.Pending =>
                DocumentProcessingAvailability.Pending,
            DocumentProcessingState.Processing =>
                DocumentProcessingAvailability.Processing,
            DocumentProcessingState.Finished
                when latest.Outcome == DocumentProcessingOutcome.Failed =>
                DocumentProcessingAvailability.Failed,
            DocumentProcessingState.Finished
                when latest.Result?.SchemaVersion == "1.0" =>
                DocumentProcessingAvailability.LegacyOnly,
            _ => throw new InvalidDataException(
                "El estado persistido del procesamiento no es coherente.")
        };
    }

    private sealed record DocumentListProjection(
        Guid DocumentId, Guid PreQuoteId, string OriginalFileName,
        string ContentType, long SizeBytes, DateTimeOffset CreatedAtUtc,
        AttemptProjection? LatestAttempt,
        ExtractionSummaryProjection? Extraction);
    internal sealed record AttemptProjection(
        Guid ProcessingAttemptId, DocumentProcessingState ProcessingState,
        DocumentProcessingOutcome? Outcome, string? ErrorCode,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc, ResultMetadataProjection? Result);
    internal sealed record ResultMetadataProjection(
        string SchemaVersion, PdfClassification Classification,
        bool RequiresOcr, int PageCount, string ProcessingMethod,
        int DurationMs);
    private sealed record ExtractionSummaryProjection(
        Guid ExtractionId, Guid ProcessingAttemptId,
        StructuredExtractionStatus Status, string? ProjectName,
        string? ClientName, string? Location, int ItemCount,
        int DocumentReferenceCount, int ItemsRequiringReview,
        int KnownQuoteableUnitCount, int IssueCount, int ConflictCount,
        string ProcessingMethod, int DurationMs, DateTimeOffset CreatedAtUtc);
}

internal sealed record AvailableExtractionProjection(
    Guid ProcessingAttemptId,
    Guid ResultId,
    string SchemaVersion,
    int PageCount,
    string PayloadJson,
    Guid ExtractionId,
    StructuredExtractionStatus Status,
    string? ProjectName,
    string? ClientName,
    string? Location,
    int ItemCount,
    int DocumentReferenceCount,
    int ItemsRequiringReview,
    int KnownQuoteableUnitCount,
    int? IdentifiedGlassItemCount,
    int? GlassItemsRequiringReview,
    string ProcessingMethod,
    int DurationMs,
    DateTimeOffset CreatedAtUtc);
internal sealed record PersistedItem(
    int Sequence, string? Reference, string Description,
    StructuredElementType ElementType, string? RawMeasurements,
    int? WidthMillimeters, int? HeightMillimeters, int? Quantity,
    bool RequiresReview);
internal sealed record PersistedRequirement(
    int Sequence, RequirementCategory Category, string Value);
internal sealed record PersistedReference(
    int Sequence, string? Reference, string Description,
    string? Detail, int? Quantity);
internal sealed record PersistedIssue(
    int Sequence, StructuredIssueCode Code, string Message,
    int? ItemSequence, int[] PageNumbers);
internal sealed record PersistedConflict(
    int Sequence, StructuredConflictCode Code, string Message,
    int[] ItemSequences, int[] PageNumbers);
internal sealed record PersistedGlass(
    int ItemSequence,
    Guid? GlassTypeId,
    string? RawSpecification,
    string? NormalizedCode,
    GlassAssignmentScope AssignmentScope,
    bool RequiresReview,
    GlassReviewReason[] ReviewReasons,
    int[] SourcePages,
    PersistedGlassEvidence[] Evidence);
internal sealed record PersistedGlassEvidence(
    int PageNumber, EvidenceSourceType SourceType, string Text);
internal sealed record PersistedTechnicalClassification(
    int ItemSequence,
    string? SystemCode,
    string? SystemOriginalText,
    TechnicalClassificationSource? SystemSource,
    decimal? SystemConfidence,
    string? FrameCode,
    string? FrameOriginalText,
    TechnicalClassificationSource? FrameSource,
    decimal? FrameConfidence,
    string? FinishCode,
    string? FinishOriginalText,
    TechnicalClassificationSource? FinishSource,
    decimal? FinishConfidence,
    bool RequiresReview,
    string[] ReviewReasons);
