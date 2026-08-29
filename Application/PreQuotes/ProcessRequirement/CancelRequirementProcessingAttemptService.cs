using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public sealed record CancelRequirementProcessingAttemptCommand(
    Guid ProcessingAttemptId);

public sealed record CancelRequirementProcessingByRequirementCommand(
    Guid RequirementId);

public enum CancelRequirementProcessingAttemptFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    ProcessingAttemptNotFound,
    QueryError,
    PersistenceError
}

public sealed record CancelRequirementProcessingAttemptResult(
    bool IsSuccess,
    CancelRequirementProcessingAttemptFailure Failure,
    ProcessedRequirementAttemptResult? Attempt)
{
    public static CancelRequirementProcessingAttemptResult Success(
        ProcessedRequirementAttemptResult attempt) =>
        new(true, CancelRequirementProcessingAttemptFailure.None, attempt);

    public static CancelRequirementProcessingAttemptResult Failed(
        CancelRequirementProcessingAttemptFailure failure) =>
        new(false, failure, null);
}

public sealed class CancelRequirementProcessingAttemptService(
    ICurrentUser currentUser,
    IRequirementRepository requirementRepository,
    IOperationCancellationRegistry cancellationRegistry,
    TimeProvider timeProvider)
{
    public async Task<CancelRequirementProcessingAttemptResult> ExecuteAsync(
        CancelRequirementProcessingAttemptCommand command,
        CancellationToken cancellationToken)
    {
        if (command.ProcessingAttemptId == Guid.Empty)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.Unauthorized);
        }

        RequirementProcessingAttempt? attempt;
        try
        {
            attempt = await requirementRepository.FindProcessingAttemptByIdAsync(
                command.ProcessingAttemptId,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.QueryError);
        }

        if (attempt is null)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure
                    .ProcessingAttemptNotFound);
        }

        return await CancelAsync(attempt, cancellationToken);
    }

    public async Task<CancelRequirementProcessingAttemptResult> ExecuteAsync(
        CancelRequirementProcessingByRequirementCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.Unauthorized);
        }

        RequirementProcessingAttempt? attempt;
        try
        {
            attempt = await requirementRepository
                .FindActiveProcessingAttemptByRequirementIdAsync(
                    command.RequirementId,
                    cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.QueryError);
        }

        return attempt is null
            ? CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure
                    .ProcessingAttemptNotFound)
            : await CancelAsync(attempt, cancellationToken);
    }

    private async Task<CancelRequirementProcessingAttemptResult> CancelAsync(
        RequirementProcessingAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.ProcessingState == DocumentProcessingState.Finished)
        {
            return CancelRequirementProcessingAttemptResult.Success(
                MapFinished(attempt));
        }

        cancellationRegistry.TryCancel(
            RequirementOperationKeys.ProcessingAttempt(attempt.Id));

        try
        {
            var finalization =
                await requirementRepository.FinalizeProcessingCancellationAsync(
                    attempt.RequirementId,
                    attempt.Id,
                    timeProvider.GetUtcNow(),
                    cancellationToken);

            return finalization is null
                ? CancelRequirementProcessingAttemptResult.Success(
                    MapPendingOrProcessing(attempt))
                : CancelRequirementProcessingAttemptResult.Success(
                    Map(finalization));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.PersistenceError);
        }
    }

    private static ProcessedRequirementAttemptResult Map(
        RequirementProcessingCancellationFinalization finalization) =>
        new(
            finalization.RequirementId,
            finalization.ProcessingAttemptId,
            finalization.CorrelationId,
            finalization.ProcessingState,
            finalization.Outcome,
            null,
            finalization.StartedAtUtc,
            finalization.CompletedAtUtc,
            null);

    private static ProcessedRequirementAttemptResult MapFinished(
        RequirementProcessingAttempt attempt) =>
        new(
            attempt.RequirementId,
            attempt.Id,
            attempt.CorrelationId,
            attempt.ProcessingState,
            attempt.Outcome ?? DocumentProcessingOutcome.Cancelled,
            attempt.ErrorCode,
            attempt.StartedAtUtc ?? attempt.CreatedAtUtc,
            attempt.CompletedAtUtc ?? attempt.StartedAtUtc
                ?? attempt.CreatedAtUtc,
            null);

    private static ProcessedRequirementAttemptResult MapPendingOrProcessing(
        RequirementProcessingAttempt attempt) =>
        new(
            attempt.RequirementId,
            attempt.Id,
            attempt.CorrelationId,
            attempt.ProcessingState,
            attempt.Outcome ?? DocumentProcessingOutcome.Cancelled,
            attempt.ErrorCode,
            attempt.StartedAtUtc ?? attempt.CreatedAtUtc,
            attempt.CompletedAtUtc ?? attempt.StartedAtUtc
                ?? attempt.CreatedAtUtc,
            null);
}
