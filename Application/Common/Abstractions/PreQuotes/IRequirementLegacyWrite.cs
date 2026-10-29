using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;

namespace Application.Common.Abstractions.PreQuotes;

public interface IRequirementLegacyWrite : IAsyncDisposable
{
    Requirement Requirement { get; }
    PreQuote PreQuote { get; }
    IReadOnlyList<RequirementProcessingAttempt> Attempts { get; }
    Task CommitAsync(CancellationToken cancellationToken);
}

public sealed class LegacyProcessingRejectedException(ProcessRequirementFailure failure) : Exception
{
    public ProcessRequirementFailure Failure { get; } = failure;
}
