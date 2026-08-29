namespace Domain.PreQuotes;

public enum DocumentProcessingOutcome
{
    Completed = 1,
    RequiresReview = 2,
    Failed = 3,
    Cancelled = 4
}
