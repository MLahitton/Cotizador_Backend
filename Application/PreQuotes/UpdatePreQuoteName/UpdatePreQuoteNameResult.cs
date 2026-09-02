namespace Application.PreQuotes.UpdatePreQuoteName;

public enum UpdatePreQuoteNameFailure
{
    None = 0,
    InvalidRequest = 1,
    Unauthorized = 2,
    InactiveUser = 3,
    NotFound = 4,
    QueryError = 5,
    PersistenceError = 6
}

public sealed record UpdatedPreQuoteNameResult(
    Guid Id,
    Guid ProjectId,
    string Serial,
    string? Name,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record UpdatePreQuoteNameResult(
    UpdatePreQuoteNameFailure Failure,
    UpdatedPreQuoteNameResult? PreQuote)
{
    public bool IsSuccess => Failure == UpdatePreQuoteNameFailure.None;

    public static UpdatePreQuoteNameResult Success(
        UpdatedPreQuoteNameResult preQuote) => new(
            UpdatePreQuoteNameFailure.None,
            preQuote);

    public static UpdatePreQuoteNameResult Failed(
        UpdatePreQuoteNameFailure failure) => new(failure, null);
}