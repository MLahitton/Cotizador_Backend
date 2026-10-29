using System.Diagnostics;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Diagnostics;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.BuildRequirementTechnicalProposal;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.ProcessRequirement;

public sealed class ProcessRequirementCoreService(
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IRequirementRepository requirementRepository,
    IRequirementProcessingLock processingLock,
    IFileStorage fileStorage,
    IAi2DocumentProcessingClient ai2Client,
    IOperationCancellationRegistry cancellationRegistry,
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

    public Task<ProcessRequirementResult> ExecuteAsync(
        Guid requirementId, Guid actorUserId, CancellationToken cancellationToken) =>
        ExecuteAsync(requirementId, actorUserId, Stopwatch.StartNew(), cancellationToken);

    // The authenticated wrapper includes validation time in the existing total performance metric.
    internal async Task<ProcessRequirementResult> ExecuteAsync(
        Guid requirementId, Guid userId, Stopwatch totalStopwatch, CancellationToken cancellationToken,
        IRequirementProcessingLease? heldLease = null)
    {
        var stages = new List<NewPipePerfStage>();

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
            LogFailure(exception, requirementId, userId, "identity_query");
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

        IRequirementProcessingLease? acquiredLease = null;
        try
        {
            // Authorize before revealing either job ownership or operational state.
            await using (await requirementRepository.BeginLegacyWriteAsync(requirementId, userId, true, cancellationToken)) { }
            if (heldLease is null)
                acquiredLease = await processingLock.TryAcquireAsync(requirementId, cancellationToken);
            else if (heldLease.RequirementId != requirementId)
                throw new InvalidOperationException("Processing lease belongs to another requirement.");
        }
        catch (LegacyProcessingRejectedException exception)
        {
            return ProcessRequirementResult.Failed(exception.Failure);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirementId, userId, "processing_lock");
            return ProcessRequirementResult.Failed(ProcessRequirementFailure.QueryError);
        }
        await using var lease = acquiredLease;
        if (heldLease is null && lease is null)
            return ProcessRequirementResult.Failed(ProcessRequirementFailure.AlreadyProcessing);

        Requirement? requirement;
        try
        {
            var loadRequirement = Stopwatch.StartNew();
            requirement = await requirementRepository.FindByIdAsync(
                requirementId,
                cancellationToken);
            RecordPerfStage(
                stages,
                requirementId,
                null,
                "LOAD_REQUIREMENT",
                loadRequirement,
                ("found", requirement is not null));
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(exception, requirementId, userId, "requirement_query");
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
        var operationKey = RequirementOperationKeys.ProcessingAttempt(
            attempt.Id);
        var operationCancellationToken = cancellationRegistry.Register(
            operationKey,
            cancellationToken);
        using var perfContext = NewPipePerformanceContext.Begin(
            requirement.Id,
            attempt.Id,
            attempt.CorrelationId);

        try
        {
            var persistAttempt = Stopwatch.StartNew();
            await using var write = await requirementRepository.BeginLegacyWriteAsync(requirement.Id, userId, true, operationCancellationToken);
            requirement = write.Requirement;
            preQuote = write.PreQuote;
            if (!requirement.IsActive) throw new LegacyProcessingRejectedException(ProcessRequirementFailure.RequirementNotFound);
            if (requirement.Status == RequirementStatus.Processing
                || write.Attempts.Any(x => x.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing))
                throw new LegacyProcessingRejectedException(ProcessRequirementFailure.AlreadyProcessing);
            if (requirement.CommercialLine is null) throw new LegacyProcessingRejectedException(ProcessRequirementFailure.InvalidRequest);
            commercialLine = requirement.CommercialLine.Value;
            files = await requirementRepository.ListFilesByRequirementIdAsync(requirement.Id, operationCancellationToken);
            if (files.Count == 0) throw new LegacyProcessingRejectedException(ProcessRequirementFailure.NoFiles);
            requirementRepository.AddProcessingAttempt(attempt);
            var startedAtUtc = timeProvider.GetUtcNow();
            attempt.Start(startedAtUtc);
            requirement.StartProcessing(startedAtUtc);
            preQuote.RegisterActivity(startedAtUtc);
            await requirementRepository.SaveChangesAsync(
                operationCancellationToken);
            await write.CommitAsync(operationCancellationToken);
            RecordPerfStage(
                stages,
                requirement.Id,
                attempt.Id,
                "FINALIZE_ATTEMPT_START",
                persistAttempt);
        }
        catch (OperationCanceledException)
        {
            cancellationRegistry.Complete(operationKey);
            throw;
        }
        catch (LegacyProcessingRejectedException exception)
        {
            cancellationRegistry.Complete(operationKey);
            return ProcessRequirementResult.Failed(exception.Failure);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            cancellationRegistry.Complete(operationKey);
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
                    operationCancellationToken));
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
            logger.LogInformation(
                "[REQ-AI2-START] RequirementId={RequirementId} ProcessingAttemptId={ProcessingAttemptId} CorrelationId={CorrelationId} ProjectId={ProjectId} FileCount={FileCount} TotalBytes={TotalBytes}",
                requirement.Id, attempt.Id, attempt.CorrelationId, project.Id,
                files.Count, files.Sum(file => file.SizeBytes));
            var aiResult = await ai2Client.ProcessAsync(
                request,
                operationCancellationToken);
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
                    CancellationToken.None);
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
                    CancellationToken.None);
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
                operationCancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "[REQ-CANCEL-START] RequirementId={RequirementId} ProcessingAttemptId={ProcessingAttemptId} CorrelationId={CorrelationId} RequestAborted={RequestAborted}",
                requirement.Id, attempt.Id, attempt.CorrelationId, cancellationToken.IsCancellationRequested);
            var result = await CancelAttemptAsync(
                requirement,
                attempt,
                CancellationToken.None);
            logger.LogInformation(
                "[REQ-CANCEL-END] RequirementId={RequirementId} ProcessingAttemptId={ProcessingAttemptId} CorrelationId={CorrelationId} Result={Result}",
                requirement.Id, attempt.Id, attempt.CorrelationId, result.Failure);
            // Finalize durable state before propagating an aborted HTTP request.
            cancellationToken.ThrowIfCancellationRequested();
            return result;
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
                CancellationToken.None);
        }
        finally
        {
            cancellationRegistry.Complete(operationKey);
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
            await using var write = await requirementRepository.BeginLegacyWriteAsync(requirement.Id,
                attempt.RequestedByUserId, false, cancellationToken);
            requirement = write.Requirement;
            preQuote = write.PreQuote;
            var current = write.Attempts.SingleOrDefault(x => x.Id == attempt.Id && x.CorrelationId == attempt.CorrelationId);
            if (current is null || current.ProcessingState != DocumentProcessingState.Processing
                || requirement.Status != RequirementStatus.Processing || !requirement.IsCurrent
                || write.Attempts.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Last().Id != current.Id)
                throw new LegacyProcessingRejectedException(ProcessRequirementFailure.PersistenceError);
            attempt = current;
            requirementRepository.AddExtractionResult(result);
            foreach (var item in extractedItems)
            {
                requirementRepository.AddExtractedItem(item.Item);
                foreach (var evidence in item.Evidence)
                {
                    requirementRepository.AddExtractedItemEvidence(evidence);
                }
                foreach (var segment in item.Segments)
                {
                    requirementRepository.AddExtractedItemSegment(segment);
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
            await write.CommitAsync(cancellationToken);
            RecordPerfStage(
                stages,
                requirement.Id,
                attempt.Id,
                "FINALIZE_ATTEMPT",
                finalizeAttempt,
                ("outcome", outcome));
        }
        catch (LegacyProcessingRejectedException exception)
        {
            return ProcessRequirementResult.Failed(exception.Failure);
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
                CancellationToken.None);
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

    private IReadOnlyList<RequirementExtractionMapper.ExtractedItemWithEvidence> CreateExtractedItems(
        Guid requirementId, Guid extractionResultId, IReadOnlyList<StructuredItemData> items, DateTimeOffset createdAtUtc) =>
        new RequirementExtractionMapper(logger).CreateExtractedItems(requirementId, extractionResultId, items, createdAtUtc);

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
                    cancellationToken,
                    attempt.RequestedByUserId);

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
        catch (LegacyProcessingRejectedException exception)
        {
            return ProcessRequirementResult.Failed(exception.Failure);
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

    private async Task<ProcessRequirementResult> CancelAttemptAsync(
        Requirement requirement,
        RequirementProcessingAttempt attempt,
        CancellationToken cancellationToken)
    {
        var completedAtUtc = timeProvider.GetUtcNow();

        try
        {
            var finalization =
                await requirementRepository.FinalizeProcessingCancellationAsync(
                    requirement.Id,
                    attempt.Id,
                    completedAtUtc,
                    cancellationToken,
                    attempt.RequestedByUserId);

            if (finalization is null)
            {
                return ProcessRequirementResult.Failed(
                    ProcessRequirementFailure.PersistenceError);
            }

            return ProcessRequirementResult.Failed(
                ProcessRequirementFailure.Cancelled,
                CreateAttemptResult(finalization));
        }
        catch (LegacyProcessingRejectedException exception)
        {
            return ProcessRequirementResult.Failed(exception.Failure);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            LogFailure(
                exception,
                requirement.Id,
                attempt.RequestedByUserId,
                "attempt_cancel");
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

    private static ProcessedRequirementAttemptResult CreateAttemptResult(
        RequirementProcessingCancellationFinalization finalization)
    {
        return new ProcessedRequirementAttemptResult(
            finalization.RequirementId,
            finalization.ProcessingAttemptId,
            finalization.CorrelationId,
            finalization.ProcessingState,
            finalization.Outcome,
            null,
            finalization.StartedAtUtc,
            finalization.CompletedAtUtc,
            null);
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

}
