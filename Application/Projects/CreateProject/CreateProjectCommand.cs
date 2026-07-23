namespace Application.Projects.CreateProject;

public sealed record CreateProjectCommand(
    Guid? ClientId,
    string? Code,
    string? Name,
    string? Description,
    string? Location);
