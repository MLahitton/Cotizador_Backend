using System.Diagnostics;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Diagnostics;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.BuildRequirementTechnicalProposal;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.ProcessRequirement;

public sealed class ProcessRequirementService(
    IValidator<ProcessRequirementCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IRequirementRepository requirementRepository,
    IFileStorage fileStorage,
    IAi2DocumentProcessingClient ai2Client,
    BuildRequirementTechnicalProposalService technicalProposalService,
    TimeProvider timeProvider,
    ILogger<ProcessRequirementService> logger)
{
    private const string StorageErrorCode = "REQUIREMENT_STORAGE_ERROR";
    private const string AiUnavailableErrorCode = "AI2_SERVICE_UNAVAILABLE";
    private const string AiTimeoutErrorCode = "AI2_TIMEOUT";
    private const string AiRejectedErrorCode = "AI2_REQUEST_REJECTED";
    private const string AiInvalidResponseErrorCode = "AI_INVALID_RESPONSE";
    private const string AiServiceErrorCode = "AI2_SERVICE_ERROR";
    private const string PersistenceErrorCode = "REQUIREMENT_PERSISTENCE_ERROR";
    private const string InvalidEvidenceLocationReason =
        "INVALID_EVIDENCE_LOCATION";

    public async Task<ProcessRequirementResult> ExecuteAsync(
        ProcessRequirementCommand command,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var stages = new List<NewPipePerfStage>();

        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.Unauthorized);
        }

        Domain.Identity.User? user;
        try
        {
            user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command.RequirementId, userId, "identity_query");
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.QueryError);
        }

        if (user is null)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.InactiveUser);
        }

        Requirement? requirement;
        try
        {
            var loadRequirement = Stopwatch.StartNew();
            requirement = await requirementRepository.FindByIdAsync(
                command.RequirementId,
                cancellationToken);
            RecordPerfStage(
                stages,
                command.RequirementId,
                null,
                "LOAD_REQUIREMENT",
                loadRequirement,
                ("found", requirement is not null));
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, command.RequirementId, userId, "requirement_query");
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.QueryError);
        }

        if (requirement is null || !requirement.IsActive)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.RequirementNotFound);
        }

        if (requirement.Status == RequirementStatus.Processing)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.AlreadyProcessing);
        }

        if (requirement.CommercialLine is null)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.InvalidRequest);
        }

        var commercialLine = requirement.CommercialLine.Value;

        PreQuote? preQuote;
        try
        {
            preQuote = await preQuoteRepository.FindForUpdateByIdAsync(
                requirement.PreQuoteId,
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, userId, "prequote_query");
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.QueryError);
        }

        if (preQuote is null)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.PreQuoteNotFound);
        }

        Domain.Projects.Project? project;
        try
        {
            project = await projectRepository.FindByIdAsync(
                preQuote.ProjectId,
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, userId, "project_query");
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.QueryError);
        }

        if (project is null)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.ProjectNotFound);
        }

        if (project.CreatedByUserId != userId)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.RequirementNotFound);
        }

        if (!project.IsActive)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.InactiveProject);
        }

        Domain.Clients.Client? client;
        try
        {
            client = await clientRepository.FindByIdAsync(
                project.ClientId,
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, userId, "client_query");
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.QueryError);
        }

        if (client is null)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.ClientNotFound);
        }

        if (!client.IsActive)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.InactiveClient);
        }

        IReadOnlyList<RequirementFile> files;
        try
        {
            var loadFiles = Stopwatch.StartNew();
            files = await requirementRepository.ListFilesByRequirementIdAsync(
                requirement.Id,
                cancellationToken);
            RecordPerfStage(
                stages,
                requirement.Id,
                null,
                "LOAD_FILES",
                loadFiles,
                ("fileCount", files.Count));
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, userId, "files_query");
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.QueryError);
        }

        if (files.Count == 0)
        {
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.NoFiles);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var attempt = RequirementProcessingAttempt.Create(
            requirement.Id,
            userId,
            Guid.NewGuid(),
            createdAtUtc);
        using var perfContext = NewPipePerformanceContext.Begin(
            requirement.Id,
            attempt.Id);

        try
        {
            var persistAttempt = Stopwatch.StartNew();
            requirementRepository.AddProcessingAttempt(attempt);
            await requirementRepository.SaveChangesAsync(cancellationToken);

            var startedAtUtc = timeProvider.GetUtcNow();
            attempt.Start(startedAtUtc);
            requirement.StartProcessing(startedAtUtc);
            preQuote.RegisterActivity(startedAtUtc);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            RecordPerfStage(
                stages,
                requirement.Id,
                attempt.Id,
                "FINALIZE_ATTEMPT_START",
                persistAttempt);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, userId, "attempt_start");
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.PersistenceError);
        }

        var streams = new List<Stream>(files.Count);
        try
        {
            foreach (var file in files.Select((value, index) => (value, index)))
            {
                var openFile = Stopwatch.StartNew();
                streams.Add(await fileStorage.OpenReadAsync(
                    file.value.StorageKey,
                    cancellationToken));
                RecordPerfStage(
                    stages,
                    requirement.Id,
                    attempt.Id,
                    "LOAD_FILES",
                    openFile,
                    ("fileIndex", file.index),
                    ("fileName", file.value.OriginalFileName),
                    ("sizeBytes", file.value.SizeBytes));
            }

            var request = CreateAi2Request(
                requirement,
                project.Id,
                attempt,
                files,
                streams);
            var ai2Extraction = Stopwatch.StartNew();
            var aiResult = await ai2Client.ProcessAsync(
                request,
                cancellationToken);
            RecordPerfStage(
                stages,
                requirement.Id,
                attempt.Id,
                "CALL_AI2_EXTRACTION",
                ai2Extraction,
                ("fileCount", files.Count),
                ("success", aiResult.IsSuccess));

            if (!aiResult.IsSuccess || aiResult.Response is null)
            {
                return await FailAttemptAsync(
                    requirement,
                    preQuote,
                    attempt,
                    MapAiFailure(aiResult.Failure),
                    cancellationToken);
            }

            if (aiResult.Response.StructuredExtraction is null)
            {
                return await FailAttemptAsync(
                    requirement,
                    preQuote,
                    attempt,
                    new AiFailure(
                        ProcessRequirementFailure.AiInvalidResponse,
                        AiInvalidResponseErrorCode),
                    cancellationToken);
            }

            return await CompleteAttemptAsync(
                requirement,
                preQuote,
                attempt,
                aiResult.Response,
                commercialLine,
                stages,
                totalStopwatch,
                files.Count,
                cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirement.Id, userId, "processing");
            var failure = exception is FileStorageReadException
                or InvalidStorageKeyException
                    ? new AiFailure(
                        ProcessRequirementFailure.StorageError,
                        StorageErrorCode)
                    : new AiFailure(
                        ProcessRequirementFailure.AiServiceUnavailable,
                        AiUnavailableErrorCode);

            return await FailAttemptAsync(
                requirement,
                preQuote,
                attempt,
                failure,
                cancellationToken);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private async Task<ProcessRequirementResult> CompleteAttemptAsync(
        Requirement requirement,
        PreQuote preQuote,
        RequirementProcessingAttempt attempt,
        DocumentProcessingResponseData response,
        RequirementCommercialLine commercialLine,
        List<NewPipePerfStage> stages,
        Stopwatch totalStopwatch,
        int fileCount,
        CancellationToken cancellationToken)
    {
        var consolidate = Stopwatch.StartNew();
        var structuredExtraction = response.StructuredExtraction!;
        var completedAtUtc = timeProvider.GetUtcNow();
        var summary = new ProcessedRequirementSummary(
            structuredExtraction.ItemCount,
            structuredExtraction.ItemsRequiringReview,
            structuredExtraction.Issues.Count,
            structuredExtraction.Conflicts.Count,
            structuredExtraction.ProcessingMethod,
            structuredExtraction.DurationMs);
        var result = RequirementExtractionResult.Create(
            attempt.Id,
            response.SchemaVersion,
            response.Provider.ToString(),
            response.PayloadJson,
            summary.ItemCount,
            summary.ItemsRequiringReview,
            summary.IssueCount,
            summary.ConflictCount,
            summary.ProcessingMethod,
            summary.DurationMs,
            completedAtUtc);
        var extractedItems = CreateExtractedItems(
            requirement.Id,
            result.Id,
            structuredExtraction.Items,
            completedAtUtc);
        RecordPerfStage(
            stages,
            requirement.Id,
            attempt.Id,
            "CONSOLIDATE_EXTRACTION",
            consolidate,
            ("extractedItemCount", extractedItems.Count));

        try
        {
            var buildProposal = Stopwatch.StartNew();
            var proposal = await technicalProposalService.BuildAsync(
                requirement.Id,
                commercialLine,
                result,
                extractedItems.Select(item => item.Item).ToArray(),
                cancellationToken);
            RecordPerfStage(
                stages,
                requirement.Id,
                attempt.Id,
                "BUILD_TECHNICAL_PROPOSAL",
                buildProposal,
                ("technicalProposalItemCount", proposal.Items.Count));
            var outcome = response.Outcome == DocumentProcessingOutcome.RequiresReview
                || proposal.Status == RequirementTechnicalProposalStatus.RequiresReview
                ? DocumentProcessingOutcome.RequiresReview
                : DocumentProcessingOutcome.Completed;

            var persistExtraction = Stopwatch.StartNew();
            requirementRepository.AddExtractionResult(result);
            foreach (var item in extractedItems)
            {
                requirementRepository.AddExtractedItem(item.Item);
                foreach (var evidence in item.Evidence)
                {
                    requirementRepository.AddExtractedItemEvidence(evidence);
                }
            }
            RecordPerfStage(
                stages,
                requirement.Id,
                attempt.Id,
                "PERSIST_EXTRACTION",
                persistExtraction,
                ("extractedItemCount", extractedItems.Count));
            var persistProposal = Stopwatch.StartNew();
            requirementRepository.AddTechnicalProposal(proposal);
            RecordPerfStage(
                stages,
                requirement.Id,
                attempt.Id,
                "PERSIST_TECHNICAL_PROPOSAL",
                persistProposal,
                ("technicalProposalItemCount", proposal.Items.Count));
            var finalizeAttempt = Stopwatch.StartNew();
            attempt.Complete(outcome, completedAtUtc);
            requirement.MarkProcessed(completedAtUtc);
            preQuote.RegisterActivity(completedAtUtc);
            await requirementRepository.SaveChangesAsync(cancellationToken);
            RecordPerfStage(
                stages,
                requirement.Id,
                attempt.Id,
                "FINALIZE_ATTEMPT",
                finalizeAttempt,
                ("outcome", outcome));
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(
                exception,
                requirement.Id,
                attempt.RequestedByUserId,
                "attempt_complete");
            return await FailAttemptAsync(
                requirement,
                preQuote,
                attempt,
                new AiFailure(
                    ProcessRequirementFailure.PersistenceError,
                    PersistenceErrorCode),
                cancellationToken);
        }

        totalStopwatch.Stop();
        LogPerfSummary(
            requirement.Id,
            attempt.Id,
            stages,
            summary,
            extractedItems.Count,
            fileCount,
            totalStopwatch.ElapsedMilliseconds);

        return ProcessRequirementResult.Success(
            CreateAttemptResult(requirement.Id, attempt, null, summary));
    }

    private IReadOnlyList<ExtractedItemWithEvidence> CreateExtractedItems(
        Guid requirementId,
        Guid extractionResultId,
        IReadOnlyList<StructuredItemData> items,
        DateTimeOffset createdAtUtc)
    {
        return items.Select(item =>
        {
            var rawEvidenceSources = item.Glass is null
                ? item.Evidence
                : item.Evidence.Concat(item.Glass.Evidence);
            var evidenceSources = rawEvidenceSources
                .GroupBy(value => new
                {
                    value.PageNumber,
                    value.SourceType,
                    value.Text,
                    value.SheetName,
                    value.CellRange,
                    value.SourceId
                })
                .Select(group => group.First())
                .ToArray();
            var validEvidenceSources = evidenceSources
                .Where(value => IsValidEvidenceLocation(value))
                .ToArray();
            var invalidEvidenceSources = evidenceSources
                .Where(value => !IsValidEvidenceLocation(value))
                .ToArray();
            var reviewReasons = item.ReviewReasons.Select(reason =>
                reason.ToString()).ToList();
            if (invalidEvidenceSources.Length > 0)
            {
                reviewReasons.Add(InvalidEvidenceLocationReason);
            }

            var extracted = RequirementExtractedItem.Create(
                extractionResultId,
                item.Ai2ElementId,
                item.Sequence,
                item.Reference,
                item.Description,
                item.ElementType,
                item.Quantity,
                item.WidthMillimeters,
                item.HeightMillimeters,
                item.AreaSquareMeters,
                item.Confidence,
                MapExtractionStatus(item.ExtractionStatus),
                item.RequiresReview || invalidEvidenceSources.Length > 0,
                reviewReasons,
                item.FunctionalType,
                item.Operation,
                item.PanelCount,
                item.MovablePanelCount,
                item.FixedPanelCount,
                item.Arrangement,
                item.Modulation,
                item.OpeningDirection,
                item.SpecialFeatures,
                item.GeometryType,
                item.RequestedSystemRaw,
                item.RequestedProfileRaw,
                item.Glass?.RawSpecification,
                item.GlassTypeRaw,
                item.GlassTypeNormalized,
                item.GlassThicknessMm,
                item.GlassColorRaw,
                item.GlassColorNormalized,
                item.GlassTreatmentRaw,
                item.GlassTreatmentNormalized,
                item.GlassComposition,
                item.GlassCoating,
                item.GlassTransparency,
                item.Glass?.RequiresReview,
                item.FinishRawDescription,
                item.FinishNormalizedType,
                item.FinishColorRaw,
                item.FinishColorNormalized,
                item.FinishTextureRaw,
                item.FinishTextureNormalized,
                item.FinishExplicitCode,
                item.FinishRequiresReview,
                createdAtUtc);
            foreach (var invalidEvidence in invalidEvidenceSources)
            {
                LogInvalidEvidence(
                    requirementId,
                    extracted.Id,
                    item.Reference,
                    item.Sequence,
                    invalidEvidence);
            }

            var persistableEvidence = validEvidenceSources.Select(value =>
                RequirementExtractedItemEvidence.Create(
                    extracted.Id,
                    value.PageNumber,
                    value.SourceType,
                    value.Text,
                    value.SheetName,
                    value.CellRange,
                    value.SourceId,
                    value.Confidence,
                    MapExtractionStatus(value.Status),
                    createdAtUtc)).ToArray();

            return new ExtractedItemWithEvidence(extracted, persistableEvidence);
        }).ToArray();
    }

    private static bool IsValidEvidenceLocation(SourceEvidenceData evidence)
    {
        var hasSheet = !string.IsNullOrWhiteSpace(evidence.SheetName);
        var hasCellRange = !string.IsNullOrWhiteSpace(evidence.CellRange);

        return evidence.SourceType switch
        {
            EvidenceSourceType.Native or EvidenceSourceType.Ocr =>
                evidence.PageNumber is > 0
                && !hasSheet
                && !hasCellRange,
            EvidenceSourceType.Xlsx =>
                evidence.PageNumber is null
                && hasSheet
                && hasCellRange,
            _ => false
        };
    }

    private void LogInvalidEvidence(
        Guid requirementId,
        Guid extractedItemId,
        string? reference,
        int itemSequence,
        SourceEvidenceData evidence)
    {
        logger.LogWarning(
            "[NEWPIPE-EVIDENCE-INVALID] RequirementId={RequirementId} ExtractedItemId={ExtractedItemId} Reference={Reference} ItemSequence={ItemSequence} SourceType={SourceType} SourceId={SourceId} PageNumber={PageNumber} SheetName={SheetName} CellRange={CellRange}",
            requirementId,
            extractedItemId,
            reference,
            itemSequence,
            evidence.SourceType,
            evidence.SourceId,
            evidence.PageNumber,
            evidence.SheetName,
            evidence.CellRange);
    }

    private async Task<ProcessRequirementResult> FailAttemptAsync(
        Requirement requirement,
        PreQuote preQuote,
        RequirementProcessingAttempt attempt,
        AiFailure failure,
        CancellationToken cancellationToken)
    {
        var completedAtUtc = timeProvider.GetUtcNow();

        try
        {
            var finalization =
                await requirementRepository.FinalizeProcessingFailureAsync(
                    requirement.Id,
                    attempt.Id,
                    failure.ErrorCode,
                    completedAtUtc,
                    cancellationToken);

            if (finalization is null)
            {
                return ProcessRequirementResult.Failed(
                    ProcessRequirementFailure.PersistenceError);
            }

            LogFailureStatePersisted(
                requirement.Id,
                attempt.Id,
                failure.ErrorCode);

            return ProcessRequirementResult.Failed(
                failure.Failure,
                CreateAttemptResult(finalization, failure.ErrorCode, null));
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(
                exception,
                requirement.Id,
                attempt.RequestedByUserId,
                "attempt_fail");
            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.PersistenceError);
        }
    }

    private static DocumentProcessingClientRequest CreateAi2Request(
        Requirement requirement,
        Guid projectId,
        RequirementProcessingAttempt attempt,
        IReadOnlyList<RequirementFile> files,
        IReadOnlyList<Stream> streams)
    {
        var requestFiles = files.Select((file, index) =>
            new DocumentProcessingFile(
                file.Id,
                file.OriginalFileName,
                file.ContentType,
                file.SizeBytes,
                streams[index])).ToArray();

        return new DocumentProcessingClientRequest(
            requestFiles[0].DocumentId,
            attempt.Id,
            attempt.CorrelationId,
            requestFiles,
            projectId,
            requirement.Id);
    }

    private static ProcessedRequirementAttemptResult CreateAttemptResult(
        Guid requirementId,
        RequirementProcessingAttempt attempt,
        string? errorCode,
        ProcessedRequirementSummary? summary)
    {
        return new ProcessedRequirementAttemptResult(
            requirementId,
            attempt.Id,
            attempt.CorrelationId,
            attempt.ProcessingState,
            attempt.Outcome ?? DocumentProcessingOutcome.Failed,
            errorCode,
            attempt.StartedAtUtc!.Value,
            attempt.CompletedAtUtc!.Value,
            summary);
    }

    private static ProcessedRequirementAttemptResult CreateAttemptResult(
        RequirementProcessingFailureFinalization finalization,
        string errorCode,
        ProcessedRequirementSummary? summary)
    {
        return new ProcessedRequirementAttemptResult(
            finalization.RequirementId,
            finalization.ProcessingAttemptId,
            finalization.CorrelationId,
            finalization.ProcessingState,
            finalization.Outcome,
            errorCode,
            finalization.StartedAtUtc,
            finalization.CompletedAtUtc,
            summary);
    }

    private static AiFailure MapAiFailure(
        DocumentProcessingClientFailure failure)
    {
        return failure switch
        {
            DocumentProcessingClientFailure.ServiceUnavailable =>
                new AiFailure(
                    ProcessRequirementFailure.AiServiceUnavailable,
                    AiUnavailableErrorCode),
            DocumentProcessingClientFailure.Timeout =>
                new AiFailure(
                    ProcessRequirementFailure.AiTimeout,
                    AiTimeoutErrorCode),
            DocumentProcessingClientFailure.RemoteRejection =>
                new AiFailure(
                    ProcessRequirementFailure.AiRemoteRejected,
                    AiRejectedErrorCode),
            DocumentProcessingClientFailure.InvalidResponse =>
                new AiFailure(
                    ProcessRequirementFailure.AiInvalidResponse,
                    AiInvalidResponseErrorCode),
            DocumentProcessingClientFailure.ServiceError =>
                new AiFailure(
                    ProcessRequirementFailure.AiServiceError,
                    AiServiceErrorCode),
            _ => new AiFailure(
                ProcessRequirementFailure.AiServiceUnavailable,
                AiUnavailableErrorCode)
        };
    }

    private static RequirementExtractionValueStatus MapExtractionStatus(
        CanonicalExtractionValueStatus status)
    {
        return status switch
        {
            CanonicalExtractionValueStatus.Explicit =>
                RequirementExtractionValueStatus.Explicit,
            CanonicalExtractionValueStatus.Inferred =>
                RequirementExtractionValueStatus.Inferred,
            CanonicalExtractionValueStatus.Ambiguous =>
                RequirementExtractionValueStatus.Ambiguous,
            CanonicalExtractionValueStatus.NotApplicable =>
                RequirementExtractionValueStatus.NotApplicable,
            _ => RequirementExtractionValueStatus.Unknown
        };
    }

    private void LogFailure(
        Exception exception,
        Guid requirementId,
        Guid userId,
        string stage)
    {
        logger.LogError(
            exception,
            "Requirement processing failed. RequirementId={RequirementId} UserId={UserId} Stage={Stage} TraceId={TraceId} ExceptionType={ExceptionType}",
            requirementId,
            userId,
            stage,
            System.Diagnostics.Activity.Current?.Id,
            exception.GetType().Name);
    }

    private void LogFailureStatePersisted(
        Guid requirementId,
        Guid processingAttemptId,
        string errorCode)
    {
        logger.LogInformation(
            "Requirement processing failure state persisted. RequirementId={RequirementId} ProcessingAttemptId={ProcessingAttemptId} ErrorCode={ErrorCode} FailureStatePersisted={FailureStatePersisted} TraceId={TraceId}",
            requirementId,
            processingAttemptId,
            errorCode,
            true,
            System.Diagnostics.Activity.Current?.Id);
    }

    private sealed record AiFailure(
        ProcessRequirementFailure Failure,
        string ErrorCode);

    private sealed record NewPipePerfStage(string Stage, long ElapsedMs);

    private void RecordPerfStage(
        ICollection<NewPipePerfStage> stages,
        Guid requirementId,
        Guid? attemptId,
        string stage,
        Stopwatch stopwatch,
        params (string Name, object? Value)[] values)
    {
        stopwatch.Stop();
        stages.Add(new NewPipePerfStage(stage, stopwatch.ElapsedMilliseconds));
        var detail = string.Join(
            " ",
            values.Select(value => $"{value.Name}={value.Value}"));
        logger.LogInformation(
            "[NEWPIPE-PERF] RequirementId={RequirementId} AttemptId={AttemptId} Stage={Stage} ElapsedMs={ElapsedMs} {Detail}",
            requirementId,
            attemptId,
            stage,
            stopwatch.ElapsedMilliseconds,
            detail);
    }

    private void LogPerfSummary(
        Guid requirementId,
        Guid attemptId,
        IReadOnlyCollection<NewPipePerfStage> stages,
        ProcessedRequirementSummary summary,
        int technicalProposalItemCount,
        int fileCount,
        long totalElapsedMs)
    {
        var context = NewPipePerformanceContext.Current;
        var stageSummary = string.Join(
            " | ",
            stages
                .GroupBy(stage => stage.Stage, StringComparer.Ordinal)
                .Select(group =>
                    $"{group.Key}={group.Sum(stage => stage.ElapsedMs)}ms"));
        logger.LogInformation(
            "[NEWPIPE-PERF-SUMMARY] RequirementId={RequirementId} AttemptId={AttemptId} TotalElapsedMs={TotalElapsedMs} FileCount={FileCount} ExtractedItemCount={ExtractedItemCount} TechnicalProposalItemCount={TechnicalProposalItemCount} HistoricalCandidateCountTotal={HistoricalCandidateCountTotal} SimilarityCallCount={SimilarityCallCount} SimilarityCandidateCountTotal={SimilarityCandidateCountTotal} CorpusReloadCount={CorpusReloadCount} CorpusReloadElapsedMs={CorpusReloadElapsedMs} HistoricalShortlistElapsedMs={HistoricalShortlistElapsedMs} Stages={Stages}",
            requirementId,
            attemptId,
            totalElapsedMs,
            fileCount,
            summary.ItemCount,
            technicalProposalItemCount,
            context?.SimilarityCandidateCountTotal ?? 0,
            context?.SimilarityCallCount ?? 0,
            context?.SimilarityCandidateCountTotal ?? 0,
            context?.CorpusReloadCount ?? 0,
            context?.CorpusReloadElapsedMs ?? 0,
            context?.HistoricalShortlistElapsedMs ?? 0,
            stageSummary);
    }

    private sealed record ExtractedItemWithEvidence(
        RequirementExtractedItem Item,
        IReadOnlyList<RequirementExtractedItemEvidence> Evidence);
}
