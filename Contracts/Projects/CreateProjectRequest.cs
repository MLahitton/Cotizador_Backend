namespace Contracts.Projects;

public sealed record CreateProjectRequest(
    Guid? ClientId,
    string? Code,
    string? Name,
    string? Description,
    string? Location);
