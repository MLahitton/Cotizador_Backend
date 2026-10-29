using Application.Common.Abstractions.DocumentProcessing;

namespace Application.PreQuotes.ProcessRequirement;

public enum AssignedExecutionOutcome
{
    Published, Cancelled, Failed, Rejected, AlreadyStarted, EvidenceAlreadyRegistered, Indeterminate
}
public enum AssignedExecutionStage { Start, Documents, Consumption, Preparation, Publication, Termination }
public sealed record AssignedExecutionResult(AssignedExecutionOutcome Outcome, AssignedExecutionStage Stage,
    AssignedJobStartResult? Start = null,
    ExecutionDocumentPreparationFailure DocumentFailure = ExecutionDocumentPreparationFailure.None,
    ExecutionDocumentConsumptionState? Consumption = null,
    DocumentProcessingClientFailure ProviderFailure = DocumentProcessingClientFailure.None,
    JobPublicationResult? Publication = null, JobTerminationResult? Termination = null);

// Explicit composition only. No scheduling, lease recovery, retries or production registration.
public sealed class CoordinateAssignedExecutionService(StartAssignedJobService start,
    IExecutionDocumentPreparation documents, ConsumeExecutionDocumentsService consume,
    PrepareJobPublicationService prepare, PublishJobResultService publish, JobTerminationService terminate)
{
    public Task<AssignedExecutionResult> ExecuteAsync(AssignedJobExecution execution, ExperienceExecutionPermit permit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permit);
        return ExecuteCoreAsync(execution with { Permit = permit }, permit.Admission.ActorUserId, null, cancellationToken);
    }

    public Task<AssignedExecutionResult> ExecuteAsync(AssignedJobExecution execution, Guid actorUserId,
        CancellationToken cancellationToken) => ExecuteCoreAsync(execution, actorUserId, null, cancellationToken);

    public Task<AssignedExecutionResult> ExecuteAsync(AssignedJobExecution execution, Guid actorUserId,
        AssignedExecutionControl control, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.Attach();
        return ExecuteCoreAsync(execution, actorUserId, control, cancellationToken);
    }

    private async Task<AssignedExecutionResult> ExecuteCoreAsync(AssignedJobExecution execution, Guid actorUserId,
        AssignedExecutionControl? control, CancellationToken cancellationToken)
    {
        using var linked = control is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, control.StopToken);
        var ct = linked?.Token ?? cancellationToken;
        var stage = AssignedExecutionStage.Start;
        PreparedExecutionDocuments? owned = null;
        AssignedJobStartResult? started = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (execution is null || execution.ProcessingAttemptId.HasValue)
                return new(AssignedExecutionOutcome.Rejected, stage,
                    AssignedJobStartResult.Reject(AssignedJobStartFailure.InvalidRequest));
            Admit(control, stage, ct);
            started = await start.StartVerifiedAsync(execution, actorUserId, ct);
            if (started.State == AssignedJobStartState.Started)
                control?.NotifyStarted(execution with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId });
            ct.ThrowIfCancellationRequested();
            if (started.State == AssignedJobStartState.AlreadyStarted)
                return new(AssignedExecutionOutcome.AlreadyStarted, stage, started);
            if (started.State == AssignedJobStartState.Indeterminate)
                return new(AssignedExecutionOutcome.Indeterminate, stage, started);
            if (started.State != AssignedJobStartState.Started)
                return started.Failure == AssignedJobStartFailure.CancellationRequested
                    ? await CloseAsync(execution, actorUserId, null, new(AssignedExecutionOutcome.Rejected, stage, started), control, ct)
                    : new(AssignedExecutionOutcome.Rejected, stage, started);

            execution = execution with { ProcessingAttemptId = started.Receipt!.ProcessingAttemptId };
            stage = AssignedExecutionStage.Documents;
            Admit(control, stage, ct);
            var prepared = await documents.PrepareAsync(execution, actorUserId, ct);
            owned = prepared.Package;
            ct.ThrowIfCancellationRequested();
            var result = new AssignedExecutionResult(AssignedExecutionOutcome.Rejected, stage, started, prepared.Failure);
            if (prepared.State == ExecutionDocumentPreparationState.Indeterminate)
                return result with { Outcome = AssignedExecutionOutcome.Indeterminate };
            if (prepared.State == ExecutionDocumentPreparationState.AlreadyRegistered)
                return result with { Outcome = AssignedExecutionOutcome.EvidenceAlreadyRegistered };
            if (prepared.State != ExecutionDocumentPreparationState.Registered)
            {
                if (prepared.Failure == ExecutionDocumentPreparationFailure.CancellationRequested)
                    return await CloseAsync(execution, actorUserId, null, result, control, ct);
                if (prepared.State == ExecutionDocumentPreparationState.Rejected
                    && prepared.Failure == ExecutionDocumentPreparationFailure.SourceUnavailable)
                    return await CloseAsync(execution, actorUserId, "REQUIREMENT_STORAGE_ERROR", result, control, ct);
                return result;
            }
            if (owned is null || prepared.CommercialLine is null) return result;

            stage = AssignedExecutionStage.Consumption;
            Admit(control, stage, ct);
            var package = owned;
            owned = null; // The consumer reserves, drains readers and disposes its winning package.
            var consumed = await consume.ConsumeAsync(package, execution, actorUserId, ct);
            ct.ThrowIfCancellationRequested();
            result = result with { Stage = stage, Consumption = consumed.State,
                DocumentFailure = consumed.Rejection, ProviderFailure = consumed.ProviderFailure };
            if (consumed.State == ExecutionDocumentConsumptionState.Rejected)
                return consumed.Rejection == ExecutionDocumentPreparationFailure.CancellationRequested
                    ? await CloseAsync(execution, actorUserId, null, result, control, ct) : result;
            if (consumed.State == ExecutionDocumentConsumptionState.InvalidResponse)
                return await CloseAsync(execution, actorUserId, "AI_INVALID_RESPONSE", result, control, ct);
            if (consumed.State == ExecutionDocumentConsumptionState.ProviderFailure)
            {
                var code = consumed.ProviderFailure switch
                {
                    DocumentProcessingClientFailure.ServiceUnavailable => "AI2_SERVICE_UNAVAILABLE",
                    DocumentProcessingClientFailure.Timeout => "AI2_TIMEOUT",
                    DocumentProcessingClientFailure.RemoteRejection => "AI2_REQUEST_REJECTED",
                    DocumentProcessingClientFailure.ServiceError => "AI2_SERVICE_ERROR",
                    DocumentProcessingClientFailure.InvalidResponse => "AI_INVALID_RESPONSE",
                    _ => null
                };
                return code is null ? result : await CloseAsync(execution, actorUserId, code, result, control, ct);
            }
            if (consumed.State != ExecutionDocumentConsumptionState.ValidResponse || consumed.Result is null) return result;

            stage = AssignedExecutionStage.Preparation;
            Admit(control, stage, ct);
            PreparedJobPublication snapshot;
            try { snapshot = await prepare.PrepareAsync(consumed.Result, prepared.CommercialLine.Value, ct); }
            catch (ExecutionPreparationRejectedException error)
            {
                var rejected = result with { Stage = stage, DocumentFailure = error.Failure };
                return error.Failure == ExecutionDocumentPreparationFailure.CancellationRequested
                    ? await CloseAsync(execution, actorUserId, null, rejected, control, ct) : rejected;
            }
            ct.ThrowIfCancellationRequested();
            stage = AssignedExecutionStage.Publication;
            Admit(control, stage, ct);
            var publication = await publish.PublishAsync(execution, actorUserId, snapshot, ct);
            ct.ThrowIfCancellationRequested();
            result = result with { Stage = stage, Publication = publication };
            if (publication.State is JobPublicationState.Published or JobPublicationState.AlreadyPublished)
                return result with { Outcome = AssignedExecutionOutcome.Published };
            if (publication.State == JobPublicationState.Indeterminate)
                return result with { Outcome = AssignedExecutionOutcome.Indeterminate };
            return publication.Failure == JobPublicationFailure.CancellationRequested
                ? await CloseAsync(execution, actorUserId, null, result, control, ct) : result;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Unclassified failures have no approved terminal code. Never infer rollback or compensate.
            return new(stage is AssignedExecutionStage.Start or AssignedExecutionStage.Documents or AssignedExecutionStage.Publication
                ? AssignedExecutionOutcome.Indeterminate : AssignedExecutionOutcome.Rejected, stage, started);
        }
        finally
        {
            try { if (owned is not null) await owned.DisposeAsync(); }
            finally { control?.Complete(); }
        }
    }

    private static void Admit(AssignedExecutionControl? control, AssignedExecutionStage stage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (control is not null && !control.TryAdmit(stage)) throw new OperationCanceledException(control.StopToken);
    }

    private async Task<AssignedExecutionResult> CloseAsync(AssignedJobExecution execution, Guid actor,
        string? error, AssignedExecutionResult preceding, AssignedExecutionControl? control, CancellationToken ct)
    {
        Admit(control, AssignedExecutionStage.Termination, ct);
        JobTerminationResult closed;
        try
        {
            closed = await terminate.FinalizeExecutionAsync(new(execution), actor, error, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return preceding with { Outcome = AssignedExecutionOutcome.Indeterminate, Stage = AssignedExecutionStage.Termination };
        }
        ct.ThrowIfCancellationRequested();
        var outcome = closed.State switch
        {
            JobTerminationState.Indeterminate => AssignedExecutionOutcome.Indeterminate,
            JobTerminationState.CancellationConfirmed => AssignedExecutionOutcome.Cancelled,
            JobTerminationState.Failed => AssignedExecutionOutcome.Failed,
            JobTerminationState.AlreadyRecognized when closed.Receipt?.JobState == "Cancelled" && error is null => AssignedExecutionOutcome.Cancelled,
            JobTerminationState.AlreadyRecognized when closed.Receipt?.JobState == "Failed" && error is not null => AssignedExecutionOutcome.Failed,
            _ => AssignedExecutionOutcome.Rejected
        };
        return preceding with { Outcome = outcome, Stage = AssignedExecutionStage.Termination, Termination = closed };
    }
}
