namespace Contracts.Projects;

public sealed record GetProjectsResponse(
    IReadOnlyList<AdministrativeProjectListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdministrativeProjectListItemResponse(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ProjectClientSummaryResponse Client);

public sealed record ProjectClientSummaryResponse(
    Guid Id,
    string ClientType,
    string LegalName,
    string? TradeName,
    string? DocumentType,
    string? DocumentNumber);
