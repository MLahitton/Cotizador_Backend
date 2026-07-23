namespace Application.Projects.UpdateProject;

public sealed record UpdateProjectCommand(
    Guid ProjectId,
    string? Code,
    string? Name,
    string? Description,
    string? Location);
