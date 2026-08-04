using System.Data.Common;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class PreQuoteDraftRepository(ApplicationDbContext dbContext)
    : IPreQuoteDraftRepository
{
    private static readonly string[] SupportedSchemaVersions = ["2.0", "3.0"];

    public async Task<PreQuoteDraftSourceContext?> FindSourceAsync(
        Guid preQuoteId, Guid documentId, Guid extractionId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = await dbContext.StructuredDocumentExtractions
                .AsNoTracking()
                .Where(x =>
                    x.Id == extractionId
                    && SupportedSchemaVersions.Contains(
                        x.DocumentExtractionResult.SchemaVersion)
                    && x.DocumentExtractionResult.ProcessingAttempt
                        .PreQuoteDocumentId == documentId
                    && x.DocumentExtractionResult.ProcessingAttempt
                        .PreQuoteDocument.PreQuoteId == preQuoteId
                    && x.DocumentExtractionResult.ProcessingAttempt
                        .PreQuoteDocument.PreQuote.Project.CreatedByUserId
                        == ownerUserId
                    && x.DocumentExtractionResult.ProcessingAttempt
                        .ProcessingState == DocumentProcessingState.Finished
                    && (x.DocumentExtractionResult.ProcessingAttempt.Outcome
                            == DocumentProcessingOutcome.Completed
                        || x.DocumentExtractionResult.ProcessingAttempt.Outcome
                            == DocumentProcessingOutcome.RequiresReview))
                .Select(x => new
                {
                    x.Id,
                    DocumentId = x.DocumentExtractionResult.ProcessingAttempt
                        .PreQuoteDocumentId,
                    PreQuoteId = x.DocumentExtractionResult.ProcessingAttempt
                        .PreQuoteDocument.PreQuoteId,
                    ProjectActive = x.DocumentExtractionResult
                        .ProcessingAttempt.PreQuoteDocument.PreQuote.Project
                        .IsActive,
                    ClientActive = x.DocumentExtractionResult
                        .ProcessingAttempt.PreQuoteDocument.PreQuote.Project
                        .Client.IsActive,
                    x.ProjectName,
                    x.ClientName,
                    x.Location
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (root is null) return null;

            var items = await dbContext.Set<StructuredExtractionItem>()
                .AsNoTracking().Where(x => x.StructuredDocumentExtractionId == extractionId)
                .OrderBy(x => x.Sequence)
                .Select(x => new PreQuoteDraftItemSource(
                    x.Id, x.Sequence, x.Reference, x.Description, x.ElementType,
                    x.RawMeasurements, x.WidthMillimeters, x.HeightMillimeters,
                    x.Quantity,
                    x.GlassDetection == null
                        ? null
                        : new PreQuoteDraftItemGlassSnapshotSource(
                            x.GlassDetection.Id,
                            x.GlassDetection.GlassTypeId,
                            x.GlassDetection.RawSpecification,
                            x.GlassDetection.NormalizedCodeSnapshot,
                            x.GlassDetection.AssignmentScope,
                            x.GlassDetection.RequiresReview,
                            x.GlassDetection.ReviewReasons
                                .OrderBy(reason => reason.Sequence)
                                .Select(reason => reason.Code)
                                .ToArray(),
                            x.GlassDetection.SourcePages
                                .OrderBy(page => page.Sequence)
                                .Select(page => page.PageNumber)
                                .ToArray(),
                            x.GlassDetection.Evidence
                                .OrderBy(evidence => evidence.Sequence)
                                .Select(evidence => new
                                    PreQuoteDraftItemGlassEvidenceSource(
                                        evidence.Sequence,
                                        evidence.PageNumber,
                                        evidence.SourceType,
                                        evidence.Text))
                                .ToArray()),
                    x.GlassValuation == null
                        ? null
                        : new PreQuoteDraftItemValuationSnapshotSource(
                            x.GlassValuation.Id,
                            x.GlassValuation.Status == GlassValuationStatus.Valued
                                ? PreQuoteDraftValuationStatus.Valued
                                : PreQuoteDraftValuationStatus.Pending,
                            x.GlassValuation.Reason,
                            x.GlassValuation.GlassTypeId,
                            x.GlassValuation.GlassPriceRangeVersionId,
                            x.WidthMillimeters,
                            x.HeightMillimeters,
                            x.Quantity,
                            x.GlassValuation.UnitAreaSquareMeters,
                            x.GlassValuation.TotalAreaSquareMeters,
                            x.GlassValuation.MinimumPricePerSquareMeter,
                            x.GlassValuation.MinimumAmount,
                            x.GlassValuation.MinimumAmount == null
                                || x.GlassValuation.TotalAreaSquareMeters == null
                                ? null
                                : x.GlassValuation.MinimumAmount,
                            x.GlassValuation.Currency,
                            x.GlassValuation.CalculatedAtUtc,
                            null,
                            x.GlassValuation.Reason == null
                                ? null
                                : x.GlassValuation.Reason == GlassValuationReason.MissingMeasurements
                                    ? PreQuoteDraftValuationInvalidationReason.WidthChanged
                                    : x.GlassValuation.Reason == GlassValuationReason.MissingQuantity
                                        ? PreQuoteDraftValuationInvalidationReason.QuantityChanged
                                        : x.GlassValuation.Reason == GlassValuationReason.GlassTypeNotResolved
                                            ? PreQuoteDraftValuationInvalidationReason.QuantityChanged
                                            : x.GlassValuation.Reason == GlassValuationReason.PriceRangeNotAvailable
                                                ? PreQuoteDraftValuationInvalidationReason.QuantityChanged
                                                : null)))
                .ToArrayAsync(cancellationToken);
            var requirements = await dbContext.Set<StructuredExtractionRequirement>()
                .AsNoTracking().Where(x => x.StructuredDocumentExtractionId == extractionId)
                .OrderBy(x => x.Sequence)
                .Select(x => new PreQuoteDraftRequirementSource(
                    x.Id, x.Sequence, x.Category, x.Value))
                .ToArrayAsync(cancellationToken);
            var references = await dbContext.Set<StructuredExtractionDocumentReference>()
                .AsNoTracking().Where(x => x.StructuredDocumentExtractionId == extractionId)
                .OrderBy(x => x.Sequence)
                .Select(x => new PreQuoteDraftReferenceSource(
                    x.Id, x.Sequence, x.Reference, x.Description, x.Detail,
                    x.Quantity)).ToArrayAsync(cancellationToken);
            var issues = await dbContext.Set<StructuredExtractionIssue>()
                .AsNoTracking().Where(x => x.StructuredDocumentExtractionId == extractionId)
                .OrderBy(x => x.Sequence)
                .Select(x => new PreQuoteDraftIssueSource(
                    x.Id, x.Sequence, x.Code, x.Message, x.ItemSequence,
                    x.PageNumbers)).ToArrayAsync(cancellationToken);
            var conflicts = await dbContext.Set<StructuredExtractionConflict>()
                .AsNoTracking().Where(x => x.StructuredDocumentExtractionId == extractionId)
                .OrderBy(x => x.Sequence)
                .Select(x => new PreQuoteDraftConflictSource(
                    x.Id, x.Sequence, x.Code, x.Message, x.ItemSequences,
                    x.PageNumbers)).ToArrayAsync(cancellationToken);

            return new PreQuoteDraftSourceContext(
                root.PreQuoteId, root.DocumentId, root.Id,
                root.ProjectActive, root.ClientActive, root.ProjectName,
                root.ClientName, root.Location, items, requirements,
                references, issues, conflicts);
        }
        catch (DbException exception)
        {
            throw new PreQuoteDraftQueryException(exception);
        }
    }

    public async Task<bool> ExistsAsync(
        Guid preQuoteId, Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.PreQuoteDrafts.AsNoTracking()
                .AnyAsync(x =>
                    x.PreQuoteId == preQuoteId
                    && x.PreQuote.Project.CreatedByUserId == ownerUserId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new PreQuoteDraftQueryException(exception);
        }
    }

    public async Task<PreQuoteDraft?> FindForUpdateAsync(
        Guid preQuoteId, Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FullQuery(false).SingleOrDefaultAsync(
                x => x.PreQuoteId == preQuoteId
                    && x.PreQuote.Project.CreatedByUserId == ownerUserId,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new PreQuoteDraftQueryException(exception);
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is DbException databaseException)
        {
            throw new PreQuoteDraftQueryException(databaseException);
        }
    }

    public async Task<PreQuoteDraft?> FindReadAsync(
        Guid preQuoteId, Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FullQuery(true).SingleOrDefaultAsync(
                x => x.PreQuoteId == preQuoteId
                    && x.PreQuote.Project.CreatedByUserId == ownerUserId,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new PreQuoteDraftQueryException(exception);
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is DbException databaseException)
        {
            throw new PreQuoteDraftQueryException(databaseException);
        }
    }

    public async Task<PreQuoteDraftActivityContext?> FindActivityAsync(
        Guid preQuoteId, Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.PreQuotes.AsNoTracking()
                .Where(x => x.Id == preQuoteId)
                .Where(x => x.Project.CreatedByUserId == ownerUserId)
                .Select(x => new PreQuoteDraftActivityContext(
                    x.Project.IsActive,
                    x.Project.Client.IsActive))
                .SingleOrDefaultAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new PreQuoteDraftQueryException(exception);
        }
    }

    public void Add(PreQuoteDraft draft) => dbContext.PreQuoteDrafts.Add(draft);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PreQuoteDraftConcurrencyException(exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PreQuoteDraftConflictException(exception);
        }
        catch (DbUpdateException exception)
        {
            throw new PreQuoteDraftPersistenceException(exception);
        }
    }

    private IQueryable<PreQuoteDraft> FullQuery(bool noTracking)
    {
        IQueryable<PreQuoteDraft> query = dbContext.PreQuoteDrafts
            .Include(x => x.Items)
            .ThenInclude(x => x.GlassSnapshot)
            .ThenInclude(x => x!.ReviewReasons)
            .Include(x => x.Items)
            .ThenInclude(x => x.GlassSnapshot)
            .ThenInclude(x => x!.SourcePages)
            .Include(x => x.Items)
            .ThenInclude(x => x.GlassSnapshot)
            .ThenInclude(x => x!.Evidence)
            .Include(x => x.Items)
            .ThenInclude(x => x.ValuationSnapshot)
            .Include(x => x.Requirements)
            .Include(x => x.DocumentReferences)
            .Include(x => x.Issues)
            .Include(x => x.Conflicts);
        return noTracking ? query.AsNoTracking() : query;
    }
}
