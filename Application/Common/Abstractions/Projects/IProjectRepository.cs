using Domain.Projects;

namespace Application.Common.Abstractions.Projects;

public interface IProjectRepository
{
    Task<bool> ExistsByCodeAsync(
        string normalizedCode,
        CancellationToken cancellationToken);

    void Add(Project project);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

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
