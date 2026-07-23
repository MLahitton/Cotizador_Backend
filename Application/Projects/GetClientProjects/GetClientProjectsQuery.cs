namespace Application.Projects.GetClientProjects;

public sealed record GetClientProjectsQuery(
    Guid ClientId,
    string? Search,
    int Page,
    int PageSize);
