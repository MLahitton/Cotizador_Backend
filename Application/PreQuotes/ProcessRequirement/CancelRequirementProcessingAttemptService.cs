using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Operations;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
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
    PersistenceError,
    InactiveUser
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
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IRequirementRepository requirementRepository,
    IOperationCancellationRegistry cancellationRegistry,
    TimeProvider timeProvider)
{
    public Task<CancelRequirementProcessingAttemptResult> ExecuteAsync(
        CancelRequirementProcessingAttemptCommand command,
        CancellationToken cancellationToken) =>
        ExecuteAsync(command.ProcessingAttemptId, byRequirement: false, cancellationToken);

    public Task<CancelRequirementProcessingAttemptResult> ExecuteAsync(
        CancelRequirementProcessingByRequirementCommand command,
        CancellationToken cancellationToken) =>
        ExecuteAsync(command.RequirementId, byRequirement: true, cancellationToken);

    private async Task<CancelRequirementProcessingAttemptResult> ExecuteAsync(
        Guid id,
        bool byRequirement,
        CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.InvalidRequest);

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.Unauthorized);

        RequirementProcessingAttempt? attempt;
        try
        {
            var user = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
            if (user is null)
                return CancelRequirementProcessingAttemptResult.Failed(
                    CancelRequirementProcessingAttemptFailure.Unauthorized);
            if (!user.IsActive)
                return CancelRequirementProcessingAttemptResult.Failed(
                    CancelRequirementProcessingAttemptFailure.InactiveUser);

            attempt = byRequirement
                ? await requirementRepository.FindActiveProcessingAttemptByRequirementIdAsync(id, cancellationToken)
                : await requirementRepository.FindProcessingAttemptByIdAsync(id, cancellationToken);
            if (attempt is null)
                return CancelRequirementProcessingAttemptResult.Failed(
                    CancelRequirementProcessingAttemptFailure.ProcessingAttemptNotFound);

            // Authorize through current Project ownership, never the attempt's original actor.
            // This also protects the read of an already finished attempt below.
            var requirement = await requirementRepository.FindByIdAsync(attempt.RequirementId, cancellationToken);
            var preQuote = requirement is null ? null
                : await preQuoteRepository.FindByIdAsync(requirement.PreQuoteId, cancellationToken);
            var project = preQuote is null ? null
                : await projectRepository.FindByIdAsync(preQuote.ProjectId, cancellationToken);
            if (project is null || project.CreatedByUserId != userId)
                return CancelRequirementProcessingAttemptResult.Failed(
                    CancelRequirementProcessingAttemptFailure.ProcessingAttemptNotFound);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return CancelRequirementProcessingAttemptResult.Failed(
                CancelRequirementProcessingAttemptFailure.QueryError);
        }

        return await CancelAsync(attempt, cancellationToken);
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
