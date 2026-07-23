namespace Contracts.Projects;

public sealed record CreateProjectResponse(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
