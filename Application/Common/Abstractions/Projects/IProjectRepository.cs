using Domain.Projects;
using Domain.Clients;

namespace Application.Common.Abstractions.Projects;

public interface IProjectRepository
{
    Task<Project?> FindByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<Project?> FindForUpdateByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<ProjectSearchPage> SearchActiveByClientAsync(
        Guid clientId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AdministrativeProjectSearchPage> SearchAsync(
        ProjectSearchCriteria criteria,
        CancellationToken cancellationToken);

    Task<bool> ExistsByCodeAsync(
        string normalizedCode,
        CancellationToken cancellationToken);

    Task<bool> ExistsByCodeForOtherProjectAsync(
        Guid projectId,
        string normalizedCode,
        CancellationToken cancellationToken);

    void Add(Project project);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record ProjectSearchPage(
    IReadOnlyList<Project> Items,
    int TotalCount);

public sealed record ProjectSearchCriteria(
    string? Search,
    bool? IsActive,
    Guid? ClientId,
    ClientType? ClientType,
    ClientDocumentType? DocumentType,
    int Page,
    int PageSize);

public sealed record AdministrativeProjectSearchItem(
    Guid Id,
    Guid ClientId,
    string Code,
    string Name,
    string? Description,
    string? Location,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ClientType ClientType,
    string ClientLegalName,
    string? ClientTradeName,
    ClientDocumentType? ClientDocumentType,
    string? ClientDocumentNumber);

public sealed record AdministrativeProjectSearchPage(
    IReadOnlyList<AdministrativeProjectSearchItem> Items,
    int TotalCount);

public sealed class ProjectQueryException : Exception
{
    public ProjectQueryException(Exception innerException)
        : base(
            "No fue posible consultar los proyectos.",
            innerException)
    {
    }
}

public sealed class ProjectConflictException : Exception
{
    public ProjectConflictException(Exception innerException)
        : base(
            "Se detectó un conflicto al guardar el proyecto.",
            innerException)
    {
    }
}

public sealed class ProjectPersistenceException : Exception
{
    public ProjectPersistenceException(Exception innerException)
        : base(
            "No fue posible guardar el proyecto.",
            innerException)
    {
    }
}
