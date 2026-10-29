using Application.PreQuotes.ManageRequirementDocuments;
using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public sealed record RequirementDocumentCapabilities(bool CanEditDocuments, bool CanCancel, bool CanReplace)
{
    public static RequirementDocumentCapabilities None { get; } = new(false, false, false);
}

public static class RequirementDocumentPolicy
{
    // Callers must supply complete persisted sets, not optionally loaded navigations.
    public static RequirementDocumentCapabilities Evaluate(Requirement requirement,
        IReadOnlyList<RequirementProcessingJob> jobs,
        IReadOnlyList<RequirementProcessingAttempt> attempts, bool authorized)
    {
        if (!authorized || !requirement.IsCurrent)
            return RequirementDocumentCapabilities.None;

        var editable = requirement.Status == RequirementStatus.Pending
            && jobs.Count == 0 && attempts.Count == 0;
        var active = jobs.Any(j => j.State is RequirementProcessingJobState.Queued or RequirementProcessingJobState.Running)
            || attempts.Any(a => a.ProcessingState is DocumentProcessingState.Pending or DocumentProcessingState.Processing);

        // Multiple terminal executions have no authoritative ordering in this model.
        var cancelled = jobs.Count == 1 && jobs[0].State == RequirementProcessingJobState.Cancelled
            && (jobs[0].ProcessingAttemptId is Guid attemptId
                ? attempts.Count == 1 && attempts[0].Id == attemptId
                    && attempts[0].ProcessingState == DocumentProcessingState.Finished
                    && attempts[0].Outcome == DocumentProcessingOutcome.Cancelled
                : attempts.Count == 0);
        var replaceable = !active && (requirement.CanReplace
            || requirement.Status == RequirementStatus.Pending && cancelled);
        return new(editable, editable, replaceable);
    }
}

public interface IRequirementDocumentWrite : IAsyncDisposable
{
    Requirement Requirement { get; }
    PreQuote PreQuote { get; }
    IReadOnlyList<RequirementFile> Files { get; }
    RequirementDocumentCapabilities Capabilities { get; }
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed class RequirementDocumentRejectedException(ManageRequirementDocumentsFailure failure) : Exception
{
    public ManageRequirementDocumentsFailure Failure { get; } = failure;
}
