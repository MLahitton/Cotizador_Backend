using Application.Common.Abstractions.PreQuotes;

namespace Application.PreQuotes.GetStructuredDocumentExtraction;

public enum GetStructuredDocumentExtractionFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    NotFound,
    QueryError
}

public sealed record GetStructuredDocumentExtractionResult(
    GetStructuredDocumentExtractionFailure Failure,
    StructuredDocumentExtractionQueryReadModel? Details)
{
    public bool IsSuccess =>
        Failure == GetStructuredDocumentExtractionFailure.None
        && Details is not null;

    public static GetStructuredDocumentExtractionResult Success(
        StructuredDocumentExtractionQueryReadModel details) =>
        new(GetStructuredDocumentExtractionFailure.None, details);

    public static GetStructuredDocumentExtractionResult Failed(
        GetStructuredDocumentExtractionFailure failure) =>
        new(failure, null);
}
