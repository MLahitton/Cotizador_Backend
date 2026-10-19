namespace Application.Common.Abstractions.PreQuotes;

public interface IRequirementProcessingLease : IAsyncDisposable
{
    Guid RequirementId { get; }
}

public interface IRequirementProcessingLock
{
    Task<IRequirementProcessingLease?> TryAcquireAsync(Guid requirementId, CancellationToken cancellationToken);
}
