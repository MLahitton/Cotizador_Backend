namespace Application.Projects.GetProjects;

public sealed record GetProjectsQuery(
    string? Search,
    string? Status,
    Guid? ClientId,
    string? ClientType,
    string? DocumentType,
    string? Attention,
    int Page,
    int PageSize);