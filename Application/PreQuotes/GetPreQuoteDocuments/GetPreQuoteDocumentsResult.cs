using Application.Common.Abstractions.PreQuotes;

namespace Application.PreQuotes.GetPreQuoteDocuments;

public enum GetPreQuoteDocumentsFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    NotFound,
    QueryError
}

public sealed record GetPreQuoteDocumentsResult(
    GetPreQuoteDocumentsFailure Failure,
    PreQuoteDocumentsPageReadModel? Documents)
{
    public bool IsSuccess =>
        Failure == GetPreQuoteDocumentsFailure.None && Documents is not null;

    public static GetPreQuoteDocumentsResult Success(
        PreQuoteDocumentsPageReadModel documents) =>
        new(GetPreQuoteDocumentsFailure.None, documents);

    public static GetPreQuoteDocumentsResult Failed(
        GetPreQuoteDocumentsFailure failure) =>
        new(failure, null);
}
