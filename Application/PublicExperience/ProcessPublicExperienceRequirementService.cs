using System.Diagnostics;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Microsoft.Extensions.Logging;

namespace Application.PublicExperience;

public enum PublicExperienceRequirementProcessingState
{
    Completed, RequiresReview, AlreadyProcessed, AlreadyProcessing, Failed,
    NotPrepared, InvalidLinks, CancelledOrSuperseded, ProcessingError
}

public sealed record PublicExperienceRequirementProcessingResult(
    string SubmissionId,
    PublicExperienceRequirementProcessingState State,
    Guid? ProjectId,
    Guid? PreQuoteId,
    Guid? RequirementId,
    Guid? ProcessingAttemptId,
    Guid? TechnicalProposalId,
    RequirementStatus? RequirementStatus,
    DocumentProcessingOutcome? ProcessingOutcome,
    string? ErrorCode,
    string? ReasonCode = null,
    ProcessRequirementResult? CoreResult = null)
{
    public bool IsSuccess => State is PublicExperienceRequirementProcessingState.Completed
        or PublicExperienceRequirementProcessingState.RequiresReview
        or PublicExperienceRequirementProcessingState.AlreadyProcessed;
}

public sealed class ProcessPublicExperienceRequirementService(
    IIdentityRepository identityRepository,
    IPublicExperienceSubmissionRepository submissions,
    IProjectRepository projects,
    IPreQuoteRepository preQuotes,
    IRequirementRepository requirements,
    IRequirementProcessingLock processingLock,
    ProcessRequirementCoreService core,
    ILogger<ProcessPublicExperienceRequirementService> logger)
{
    public async Task<PublicExperienceRequirementProcessingResult> ExecuteAsync(
        string submissionId, Guid actorUserId, CancellationToken cancellationToken)
    {
        PublicExperienceSubmission? submission = null;
        Requirement? requirement = null;
        var authorized = false;
        try
        {
            if (actorUserId == Guid.Empty)
                return Rejected(ProcessRequirementFailure.InvalidRequest);
            var actor = await identityRepository.FindUserByIdAsync(actorUserId, cancellationToken);
            if (actor is null) return Rejected(ProcessRequirementFailure.Unauthorized);
            if (!actor.IsActive) return Rejected(ProcessRequirementFailure.InactiveUser);

            submission = await submissions.FindAsync(submissionId, cancellationToken);
            if (submission?.ProjectId is null || submission.PreQuoteId is null || submission.RequirementId is null)
                return Result(PublicExperienceRequirementProcessingState.NotPrepared,
                    "PUBLIC_EXPERIENCE_REQUIREMENT_NOT_PREPARED");

            var project = await projects.FindByIdAsync(submission.ProjectId.Value, cancellationToken);
            if (project is not null && project.CreatedByUserId != actorUserId)
                return Rejected(ProcessRequirementFailure.RequirementNotFound);
            var preQuote = await preQuotes.FindByIdAsync(submission.PreQuoteId.Value, cancellationToken);
            requirement = await requirements.FindByIdAsync(submission.RequirementId.Value, cancellationToken);
            if (project is null || project.Id != submission.ProjectId || preQuote is null
                || preQuote.Id != submission.PreQuoteId || preQuote.ProjectId != project.Id
                || requirement is null || requirement.Id != submission.RequirementId
                || requirement.PreQuoteId != preQuote.Id)
                return Result(PublicExperienceRequirementProcessingState.InvalidLinks,
                    "PUBLIC_EXPERIENCE_PROCESSING_LINK_INVALID");

            authorized = true;
            await using var lease = await processingLock.TryAcquireAsync(requirement.Id, cancellationToken);
            if (lease is null)
                return Result(PublicExperienceRequirementProcessingState.AlreadyProcessing, "ALREADY_PROCESSING");

            if (!requirement.IsCurrent)
                return Result(PublicExperienceRequirementProcessingState.CancelledOrSuperseded,
                    "PUBLIC_EXPERIENCE_REQUIREMENT_TERMINAL");
            if (requirement.Status == RequirementStatus.Processing)
                return Result(PublicExperienceRequirementProcessingState.AlreadyProcessing, "ALREADY_PROCESSING");
            if (requirement.Status == RequirementStatus.Failed)
                return Result(PublicExperienceRequirementProcessingState.Failed,
                    LatestAttempt()?.ErrorCode ?? "PROCESSING_FAILED", reason: "PROCESSING_FAILED");
            if (requirement.Status == RequirementStatus.Processed)
                return await SuccessAsync(null, replay: true);

            // The shared DB lease covers the state decision and the whole core execution.
            var processed = await core.ExecuteAsync(requirement.Id, actorUserId,
                Stopwatch.StartNew(), cancellationToken, lease);
            if (processed.Failure is ProcessRequirementFailure.Unauthorized or ProcessRequirementFailure.InactiveUser
                or ProcessRequirementFailure.RequirementNotFound)
                return Rejected(processed.Failure);
            // Failure finalization clears EF tracking; the earlier instance can still say Processing.
            try
            {
                requirement = await requirements.FindByIdAsync(submission.RequirementId.Value,
                    cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Could not reload requirement after processing. SubmissionId={SubmissionId}", submissionId);
                requirement = null;
            }
            if (!processed.IsSuccess)
                return Result(processed.Failure == ProcessRequirementFailure.AlreadyProcessing
                        ? PublicExperienceRequirementProcessingState.AlreadyProcessing
                        : PublicExperienceRequirementProcessingState.ProcessingError,
                    processed.Failure == ProcessRequirementFailure.ProcessingManagedByJob
                        ? "PROCESSING_MANAGED_BY_JOB"
                        : processed.Attempt?.ErrorCode ?? processed.Failure.ToString(), processed);
            if (requirement is null)
                return Result(PublicExperienceRequirementProcessingState.ProcessingError,
                    "PUBLIC_EXPERIENCE_PROCESSING_ERROR", processed);
            return await SuccessAsync(processed, replay: false);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Public experience requirement processing adapter failed. SubmissionId={SubmissionId}", submissionId);
            return Result(PublicExperienceRequirementProcessingState.ProcessingError,
                "PUBLIC_EXPERIENCE_PROCESSING_ERROR");
        }

        RequirementProcessingAttempt? LatestAttempt() => requirement?.ProcessingAttempts
            .OrderByDescending(attempt => attempt.CreatedAtUtc).ThenByDescending(attempt => attempt.Id).FirstOrDefault();

        PublicExperienceRequirementProcessingResult Rejected(ProcessRequirementFailure failure) =>
            new(submissionId, PublicExperienceRequirementProcessingState.ProcessingError,
                null, null, null, null, null, null, null, failure.ToString());

        async Task<PublicExperienceRequirementProcessingResult> SuccessAsync(ProcessRequirementResult? processed, bool replay)
        {
            var proposal = await requirements.GetCurrentTechnicalProposalAsync(requirement!.Id, cancellationToken);
            if (proposal is null)
                return Result(PublicExperienceRequirementProcessingState.ProcessingError,
                    "PUBLIC_EXPERIENCE_TECHNICAL_PROPOSAL_MISSING", processed);
            var outcome = processed?.Attempt?.Outcome ?? LatestAttempt()?.Outcome;
            if (proposal.Status == RequirementTechnicalProposalStatus.RequiresReview)
                outcome = DocumentProcessingOutcome.RequiresReview;
            return Result(replay ? PublicExperienceRequirementProcessingState.AlreadyProcessed
                    : outcome == DocumentProcessingOutcome.RequiresReview
                        || proposal.Status == RequirementTechnicalProposalStatus.RequiresReview
                        ? PublicExperienceRequirementProcessingState.RequiresReview
                        : PublicExperienceRequirementProcessingState.Completed,
                null, processed, proposal.Id, outcome: outcome);
        }

        PublicExperienceRequirementProcessingResult Result(PublicExperienceRequirementProcessingState state,
            string? error, ProcessRequirementResult? processed = null, Guid? proposalId = null, string? reason = null,
            DocumentProcessingOutcome? outcome = null) =>
            new(submissionId, state, authorized ? submission?.ProjectId : null,
                authorized ? submission?.PreQuoteId : null, authorized ? submission?.RequirementId : null,
                authorized ? processed?.Attempt?.ProcessingAttemptId ?? LatestAttempt()?.Id : null,
                authorized ? proposalId : null, authorized ? requirement?.Status : null,
                authorized ? outcome ?? processed?.Attempt?.Outcome ?? LatestAttempt()?.Outcome : null,
                error, reason, processed);
    }
}
