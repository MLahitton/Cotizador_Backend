namespace Contracts.Projects;

public sealed record UpdateProjectRequest(
    string? Code,
    string? Name,
    string? Description,
    string? Location);
