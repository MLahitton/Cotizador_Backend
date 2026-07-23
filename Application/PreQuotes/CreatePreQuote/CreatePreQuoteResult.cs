namespace Application.PreQuotes.CreatePreQuote;

public enum CreatePreQuoteFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    ProjectNotFound = 4,
    InactiveProject = 5,
    ClientNotFound = 6,
    InactiveClient = 7,
    QueryError = 8,
    PersistenceError = 9
}

public sealed record CreatedPreQuoteResult(
    Guid Id,
    Guid ProjectId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreatePreQuoteResult(
    CreatePreQuoteFailure Failure,
    CreatedPreQuoteResult? PreQuote)
{
    public bool IsSuccess => Failure == CreatePreQuoteFailure.None;

    public static CreatePreQuoteResult Success(
        CreatedPreQuoteResult preQuote)
    {
        return new CreatePreQuoteResult(
            CreatePreQuoteFailure.None,
            preQuote);
    }

    public static CreatePreQuoteResult Failed(
        CreatePreQuoteFailure failure)
    {
        return new CreatePreQuoteResult(failure, null);
    }
}
