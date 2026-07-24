namespace Application.PreQuotes.GetPreQuoteById;

public enum GetPreQuoteByIdFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    QueryError = 5
}

public sealed record PreQuoteDetailsResult(
    Guid Id,
    Guid ProjectId,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GetPreQuoteByIdResult(
    GetPreQuoteByIdFailure Failure,
    PreQuoteDetailsResult? PreQuote)
{
    public bool IsSuccess => Failure == GetPreQuoteByIdFailure.None;

    public static GetPreQuoteByIdResult Success(
        PreQuoteDetailsResult preQuote)
    {
        return new GetPreQuoteByIdResult(
            GetPreQuoteByIdFailure.None,
            preQuote);
    }

    public static GetPreQuoteByIdResult Failed(
        GetPreQuoteByIdFailure failure)
    {
        return new GetPreQuoteByIdResult(failure, null);
    }
}
