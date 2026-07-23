namespace Contracts.Projects;

public sealed record GetClientProjectsResponse(
    IReadOnlyList<ProjectListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
