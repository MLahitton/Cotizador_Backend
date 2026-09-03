using System.Data.Common;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Persistence.Repositories;

public sealed class RequirementRepository(ApplicationDbContext dbContext)
    : IRequirementRepository
{
    public async Task<Requirement?> FindByIdAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Requirements
                .Include(requirement => requirement.Files)
                .Include(requirement => requirement.ProcessingAttempts)
                    .ThenInclude(attempt => attempt.ExtractionResult)
                        .ThenInclude(result => result!.Items)
                .SingleOrDefaultAsync(
                    requirement => requirement.Id == requirementId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<IReadOnlyList<RequirementFile>>
        ListFilesByRequirementIdAsync(
            Guid requirementId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementFiles
                .AsNoTracking()
                .Where(file => file.RequirementId == requirementId)
                .OrderBy(file => file.CreatedAtUtc)
                .ThenBy(file => file.Id)
                .ToListAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<IReadOnlyList<RequirementDocumentReadModel>>
        ListDocumentReadModelsByRequirementIdAsync(
            Guid requirementId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementFiles
                .AsNoTracking()
                .Where(file => file.RequirementId == requirementId)
                .OrderBy(file => file.CreatedAtUtc)
                .ThenBy(file => file.Id)
                .Select(file => new RequirementDocumentReadModel(
                    file.Id,
                    file.OriginalFileName,
                    file.ContentType,
                    file.SizeBytes,
                    file.CreatedAtUtc))
                .ToListAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<Requirement?> FindByIdForUpdateAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Requirements
                .Include(requirement => requirement.Files)
                .Include(requirement => requirement.ProcessingAttempts)
                .SingleOrDefaultAsync(
                    requirement => requirement.Id == requirementId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementFile?> FindFileForUpdateAsync(
        Guid requirementFileId,
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementFiles.SingleOrDefaultAsync(
                file => file.Id == requirementFileId
                    && file.RequirementId == requirementId,
                cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<CurrentRequirementReadModel?> GetCurrentByPreQuoteIdAsync(
        Guid preQuoteId,
        CancellationToken cancellationToken)
    {
        try
        {
            var candidates = await dbContext.Requirements
                .AsNoTracking()
                .Where(requirement =>
                    requirement.PreQuoteId == preQuoteId
                    && requirement.IsActive)
                .Select(requirement => new CurrentRequirementProjection(
                    requirement.Id,
                    requirement.PreQuoteId,
                    requirement.Status,
                    requirement.CommercialLine,
                    requirement.SupersedesRequirementId,
                    requirement.SupersededByRequirementId,
                    requirement.CreatedAtUtc,
                    dbContext.RequirementTechnicalProposals
                        .Any(proposal =>
                            proposal.RequirementId == requirement.Id),
                    dbContext.RequirementTechnicalProposals
                        .Where(proposal =>
                            proposal.RequirementId == requirement.Id)
                        .OrderByDescending(proposal =>
                            proposal.ProcessingAttempt.CompletedAtUtc)
                        .ThenByDescending(proposal => proposal.CreatedAtUtc)
                        .ThenByDescending(proposal => proposal.Id)
                        .Select(proposal => (Guid?)proposal.Id)
                        .FirstOrDefault(),
                    dbContext.RequirementProcessingAttempts
                        .Where(attempt =>
                            attempt.RequirementId == requirement.Id)
                        .OrderByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt => (Guid?)attempt.Id)
                        .FirstOrDefault(),
                    dbContext.RequirementProcessingAttempts
                        .Where(attempt =>
                            attempt.RequirementId == requirement.Id)
                        .OrderByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt =>
                            (DocumentProcessingState?)attempt.ProcessingState)
                        .FirstOrDefault(),
                    dbContext.RequirementProcessingAttempts
                        .Where(attempt =>
                            attempt.RequirementId == requirement.Id)
                        .OrderByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt => attempt.Outcome)
                        .FirstOrDefault(),
                    dbContext.RequirementProcessingAttempts
                        .Where(attempt =>
                            attempt.RequirementId == requirement.Id)
                        .OrderByDescending(attempt => attempt.CreatedAtUtc)
                        .ThenByDescending(attempt => attempt.Id)
                        .Select(attempt => attempt.ErrorCode)
                        .FirstOrDefault()))
                .ToListAsync(cancellationToken);

            var selected = candidates
                .OrderBy(candidate => candidate.Rank)
                .ThenByDescending(candidate => candidate.CreatedAtUtc)
                .FirstOrDefault();

            if (selected is null)
            {
                return null;
            }

            var documents = await ListDocumentReadModelsByRequirementIdAsync(
                selected.RequirementId,
                cancellationToken);

            return new CurrentRequirementReadModel(
                selected.RequirementId,
                selected.PreQuoteId,
                selected.Status,
                selected.CommercialLine,
                selected.CreatedAtUtc,
                selected.HasTechnicalProposal,
                selected.TechnicalProposalId,
                selected.LatestAttemptId,
                selected.LatestAttemptState,
                selected.LatestAttemptOutcome,
                selected.LatestAttemptErrorCode,
                selected.CanEditDocuments,
                selected.CanCancel,
                selected.CanReplace,
                selected.IsCurrent,
                selected.SupersedesRequirementId,
                selected.SupersededByRequirementId,
                documents);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementProcessingAttempt?>
        FindProcessingAttemptByIdAsync(
            Guid processingAttemptId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementProcessingAttempts
                .Include(attempt => attempt.ExtractionResult)
                    .ThenInclude(result => result!.Items)
                .SingleOrDefaultAsync(
                    attempt => attempt.Id == processingAttemptId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementProcessingAttempt?>
        FindActiveProcessingAttemptByRequirementIdAsync(
            Guid requirementId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementProcessingAttempts
                .Where(attempt =>
                    attempt.RequirementId == requirementId
                    && (attempt.ProcessingState == DocumentProcessingState.Pending
                        || attempt.ProcessingState
                            == DocumentProcessingState.Processing))
                .OrderByDescending(attempt => attempt.CreatedAtUtc)
                .ThenByDescending(attempt => attempt.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementProcessingFailureFinalization?>
        FinalizeProcessingFailureAsync(
            Guid requirementId,
            Guid processingAttemptId,
            string errorCode,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            var requirement = await dbContext.Requirements
                .SingleOrDefaultAsync(
                    value => value.Id == requirementId,
                    cancellationToken);
            var attempt = await dbContext.RequirementProcessingAttempts
                .SingleOrDefaultAsync(
                    value => value.Id == processingAttemptId
                        && value.RequirementId == requirementId,
                    cancellationToken);

            if (requirement is null || attempt is null)
            {
                return null;
            }

            var preQuote = await dbContext.PreQuotes
                .SingleOrDefaultAsync(
                    value => value.Id == requirement.PreQuoteId,
                    cancellationToken);

            if (attempt.ProcessingState == DocumentProcessingState.Processing)
            {
                attempt.Fail(errorCode, completedAtUtc);
            }

            if (requirement.Status == RequirementStatus.Processing)
            {
                requirement.MarkFailed(completedAtUtc);
            }

            preQuote?.RegisterActivity(completedAtUtc);
            await SaveChangesAsync(cancellationToken);

            return CreateFailureFinalization(requirement.Id, attempt);
        }
        catch (RequirementPersistenceException)
        {
            throw;
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementProcessingCancellationFinalization?>
        FinalizeProcessingCancellationAsync(
            Guid requirementId,
            Guid processingAttemptId,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            var requirement = await dbContext.Requirements
                .SingleOrDefaultAsync(
                    value => value.Id == requirementId,
                    cancellationToken);
            var attempt = await dbContext.RequirementProcessingAttempts
                .SingleOrDefaultAsync(
                    value => value.Id == processingAttemptId
                        && value.RequirementId == requirementId,
                    cancellationToken);

            if (requirement is null || attempt is null)
            {
                return null;
            }

            if (attempt.ProcessingState != DocumentProcessingState.Processing)
            {
                return attempt.Outcome == DocumentProcessingOutcome.Cancelled
                    ? CreateCancellationFinalization(requirement.Id, attempt)
                    : null;
            }

            var preQuote = await dbContext.PreQuotes
                .SingleOrDefaultAsync(
                    value => value.Id == requirement.PreQuoteId,
                    cancellationToken);

            attempt.Cancel(completedAtUtc);

            if (requirement.Status == RequirementStatus.Processing)
            {
                requirement.MarkProcessingCancelled(completedAtUtc);
            }

            preQuote?.RegisterActivity(completedAtUtc);
            await SaveChangesAsync(cancellationToken);

            return CreateCancellationFinalization(requirement.Id, attempt);
        }
        catch (RequirementPersistenceException)
        {
            throw;
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public void Add(Requirement requirement)
    {
        dbContext.Requirements.Add(requirement);
    }

    public void AddFile(RequirementFile file)
    {
        dbContext.RequirementFiles.Add(file);
    }

    public void RemoveFile(RequirementFile file)
    {
        dbContext.RequirementFiles.Remove(file);
    }

    public void AddProcessingAttempt(RequirementProcessingAttempt attempt)
    {
        dbContext.RequirementProcessingAttempts.Add(attempt);
    }

    public void AddExtractionResult(RequirementExtractionResult result)
    {
        dbContext.RequirementExtractionResults.Add(result);
    }

    public void AddExtractedItem(RequirementExtractedItem item)
    {
        dbContext.RequirementExtractedItems.Add(item);
    }

    public void AddExtractedItemEvidence(RequirementExtractedItemEvidence evidence)
    {
        dbContext.RequirementExtractedItemEvidence.Add(evidence);
    }

    public void AddExtractedItemSegment(RequirementExtractedItemSegment segment)
    {
        dbContext.RequirementExtractedItemSegments.Add(segment);
    }

    public void AddTechnicalProposal(RequirementTechnicalProposal proposal)
    {
        dbContext.RequirementTechnicalProposals.Add(proposal);
    }

    public void AddPricingSnapshot(RequirementPricingSnapshot snapshot)
    {
        dbContext.RequirementPricingSnapshots.Add(snapshot);
    }

    public async Task<RequirementExtractionResult?>
        GetLatestSuccessfulExtractionAsync(
            Guid requirementId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementExtractionResults
                .AsNoTracking()
                .Include(result => result.Items)
                    .ThenInclude(item => item.Evidence)
                .Include(result => result.Items)
                    .ThenInclude(item => item.Segments)
                .Where(result =>
                    result.ProcessingAttempt.RequirementId == requirementId
                    && result.ProcessingAttempt.ProcessingState
                        == DocumentProcessingState.Finished
                    && (result.ProcessingAttempt.Outcome
                        == DocumentProcessingOutcome.Completed
                        || result.ProcessingAttempt.Outcome
                        == DocumentProcessingOutcome.RequiresReview))
                .OrderByDescending(result => result.ProcessingAttempt.CompletedAtUtc)
                .ThenByDescending(result => result.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<IReadOnlyList<RequirementExtractedItem>>
        GetExtractedItemsAsync(
            Guid extractionResultId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementExtractedItems
                .AsNoTracking()
                .Include(item => item.Evidence)
                .Include(item => item.Segments)
                .Where(item => item.RequirementExtractionResultId
                    == extractionResultId)
                .OrderBy(item => item.Sequence)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementTechnicalProposal?>
        GetCurrentTechnicalProposalAsync(
            Guid requirementId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementTechnicalProposals
                .AsNoTracking()
                .Include(proposal => proposal.Requirement)
                .Include(proposal => proposal.Items)
                    .ThenInclude(item => item.ExtractedItem)
                        .ThenInclude(item => item.Evidence)
                .Include(proposal => proposal.Items)
                    .ThenInclude(item => item.ExtractedItem)
                        .ThenInclude(item => item.Segments)
                .Include(proposal => proposal.Items)
                    .ThenInclude(item => item.SystemAlternatives)
                .Include(proposal => proposal.Items)
                    .ThenInclude(item => item.GlassAlternatives)
                .Include(proposal => proposal.Items)
                    .ThenInclude(item => item.FinishAlternatives)
                .Include(proposal => proposal.Items)
                    .ThenInclude(item => item.HistoricalExamples)
                .Where(proposal => proposal.RequirementId == requirementId)
                .OrderByDescending(proposal =>
                    proposal.ProcessingAttempt.CompletedAtUtc)
                .ThenByDescending(proposal => proposal.CreatedAtUtc)
                .ThenByDescending(proposal => proposal.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementTechnicalProposal?>
        FindTechnicalProposalForUpdateAsync(
            Guid technicalProposalId,
            CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementTechnicalProposals
                .Include(proposal => proposal.Requirement)
                .Include(proposal => proposal.Items)
                    .ThenInclude(item => item.ExtractedItem)
                .SingleOrDefaultAsync(
                    proposal => proposal.Id == technicalProposalId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementTechnicalProposal?>
        FindCurrentTechnicalProposalForUpdateAsync(
            Guid requirementId,
            CancellationToken cancellationToken)
    {
        try
        {
            var proposalId = await LockCurrentTechnicalProposalIdAsync(
                requirementId,
                cancellationToken);
            if (proposalId is null)
            {
                return null;
            }

            return await dbContext.RequirementTechnicalProposals
                .Include(proposal => proposal.Requirement)
                .Include(proposal => proposal.Items)
                    .ThenInclude(item => item.ExtractedItem)
                .SingleOrDefaultAsync(
                    proposal => proposal.Id == proposalId.Value,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementPricingSnapshot?> GetCurrentPricingSnapshotAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.RequirementPricingSnapshots
                .AsNoTracking()
                .Include(snapshot => snapshot.Items)
                .Where(snapshot => snapshot.RequirementId == requirementId)
                .OrderByDescending(snapshot => snapshot.UpdatedAtUtc)
                .ThenByDescending(snapshot => snapshot.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<RequirementPricingSnapshot?>
        FindCurrentPricingSnapshotForUpdateAsync(
            Guid requirementId,
            CancellationToken cancellationToken)
    {
        try
        {
            var snapshotId = await LockCurrentPricingSnapshotIdAsync(
                requirementId,
                cancellationToken);
            if (snapshotId is null)
            {
                return null;
            }

            return await dbContext.RequirementPricingSnapshots
                .Include(snapshot => snapshot.Items)
                .SingleOrDefaultAsync(
                    snapshot => snapshot.Id == snapshotId.Value,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new RequirementQueryException(exception);
        }
    }

    public async Task<IRequirementPersistenceTransaction>
        BeginPricingUpdateTransactionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await dbContext.Database.BeginTransactionAsync(
                cancellationToken);
            return new EfRequirementPersistenceTransaction(transaction);
        }
        catch (DbException exception)
        {
            throw new RequirementPersistenceException(exception);
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new RequirementPersistenceException(exception);
        }
    }

    private async Task<Guid?> LockCurrentTechnicalProposalIdAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT proposal.id
            FROM core.requirement_technical_proposals AS proposal
            LEFT JOIN core.requirement_processing_attempts AS attempt
                ON attempt.id = proposal.requirement_processing_attempt_id
            WHERE proposal.requirement_id = @requirementId
            ORDER BY attempt.completed_at_utc DESC NULLS LAST,
                proposal.created_at_utc DESC,
                proposal.id DESC
            LIMIT 1
            FOR UPDATE OF proposal;
            """;

        return await ExecuteLockedIdAsync(
            commandText,
            requirementId,
            cancellationToken);
    }

    private async Task<Guid?> LockCurrentPricingSnapshotIdAsync(
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        const string commandText = """
            SELECT snapshot.id
            FROM core.requirement_pricing_snapshots AS snapshot
            WHERE snapshot.requirement_id = @requirementId
            ORDER BY snapshot.updated_at_utc DESC,
                snapshot.id DESC
            LIMIT 1
            FOR UPDATE OF snapshot;
            """;

        return await ExecuteLockedIdAsync(
            commandText,
            requirementId,
            cancellationToken);
    }

    private async Task<Guid?> ExecuteLockedIdAsync(
        string commandText,
        Guid requirementId,
        CancellationToken cancellationToken)
    {
        var currentTransaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "La lectura ForUpdate requiere una transaccion activa.");
        var connection = dbContext.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.Transaction = currentTransaction.GetDbTransaction();
        command.CommandText = commandText;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "requirementId";
        parameter.Value = requirementId;
        command.Parameters.Add(parameter);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar switch
        {
            null => null,
            DBNull => null,
            Guid id => id,
            _ => Guid.Parse(Convert.ToString(scalar)!)
        };
    }

    private static RequirementProcessingFailureFinalization
        CreateFailureFinalization(
            Guid requirementId,
            RequirementProcessingAttempt attempt)
    {
        return new RequirementProcessingFailureFinalization(
            requirementId,
            attempt.Id,
            attempt.CorrelationId,
            attempt.ProcessingState,
            attempt.Outcome ?? DocumentProcessingOutcome.Failed,
            attempt.ErrorCode ?? string.Empty,
            attempt.StartedAtUtc!.Value,
            attempt.CompletedAtUtc!.Value);
    }

    private static RequirementProcessingCancellationFinalization
        CreateCancellationFinalization(
            Guid requirementId,
            RequirementProcessingAttempt attempt)
    {
        return new RequirementProcessingCancellationFinalization(
            requirementId,
            attempt.Id,
            attempt.CorrelationId,
            attempt.ProcessingState,
            attempt.Outcome ?? DocumentProcessingOutcome.Cancelled,
            attempt.StartedAtUtc!.Value,
            attempt.CompletedAtUtc!.Value);
    }

    private sealed class EfRequirementPersistenceTransaction(
        IDbContextTransaction transaction) : IRequirementPersistenceTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    private sealed record CurrentRequirementProjection(
        Guid RequirementId,
        Guid PreQuoteId,
        RequirementStatus Status,
        RequirementCommercialLine? CommercialLine,
        Guid? SupersedesRequirementId,
        Guid? SupersededByRequirementId,
        DateTimeOffset CreatedAtUtc,
        bool HasTechnicalProposal,
        Guid? TechnicalProposalId,
        Guid? LatestAttemptId,
        DocumentProcessingState? LatestAttemptState,
        DocumentProcessingOutcome? LatestAttemptOutcome,
        string? LatestAttemptErrorCode)
    {
        public bool IsCurrent =>
            Status is not RequirementStatus.Cancelled
            && Status is not RequirementStatus.Superseded
            && SupersededByRequirementId is null;

        public bool CanEditDocuments =>
            IsCurrent
            && Status == RequirementStatus.Pending
            && LatestAttemptState is null;

        public bool CanCancel => CanEditDocuments;

        public bool CanReplace =>
            IsCurrent
            && Status is RequirementStatus.Processed or RequirementStatus.Failed;

        public int Rank =>
            !IsCurrent ? 5 :
            HasTechnicalProposal ? 1 :
            Status == RequirementStatus.Processing
                || LatestAttemptState == DocumentProcessingState.Processing ? 2 :
            Status == RequirementStatus.Pending
                || LatestAttemptState == DocumentProcessingState.Pending ? 3 :
            4;
    }
}
