namespace Application.Common.Diagnostics;

public sealed class NewPipePerformanceContext
{
    private static readonly AsyncLocal<NewPipePerformanceContext?> CurrentHolder = new();

    private NewPipePerformanceContext(Guid requirementId, Guid attemptId)
    {
        RequirementId = requirementId;
        AttemptId = attemptId;
    }

    public Guid RequirementId { get; }
    public Guid AttemptId { get; }
    public int SimilarityCallCount { get; private set; }
    public int SimilarityCandidateCountTotal { get; private set; }
    public int CorpusReloadCount { get; private set; }
    public long CorpusReloadElapsedMs { get; private set; }
    public long HistoricalShortlistElapsedMs { get; private set; }

    public static NewPipePerformanceContext? Current => CurrentHolder.Value;

    public static IDisposable Begin(Guid requirementId, Guid attemptId)
    {
        var previous = CurrentHolder.Value;
        CurrentHolder.Value = new NewPipePerformanceContext(
            requirementId,
            attemptId);
        return new Scope(previous);
    }

    public void RecordSimilarityCall(int candidateCount)
    {
        SimilarityCallCount++;
        SimilarityCandidateCountTotal += Math.Max(0, candidateCount);
    }

    public void RecordCorpusReload(long elapsedMs)
    {
        CorpusReloadCount++;
        CorpusReloadElapsedMs += Math.Max(0, elapsedMs);
    }

    public void RecordHistoricalShortlist(long elapsedMs)
    {
        HistoricalShortlistElapsedMs += Math.Max(0, elapsedMs);
    }

    private sealed class Scope(NewPipePerformanceContext? previous)
        : IDisposable
    {
        public void Dispose()
        {
            CurrentHolder.Value = previous;
        }
    }
}
