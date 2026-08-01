namespace Contracts.Common;

public sealed record ApiProblemDetailsResponse(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    string ErrorCode,
    string TraceId);
