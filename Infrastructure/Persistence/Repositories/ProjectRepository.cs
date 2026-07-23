using System.Data.Common;
using Application.Common.Abstractions.Projects;
using Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository(ApplicationDbContext dbContext)
    : IProjectRepository
{
    public async Task<bool> ExistsByCodeAsync(
        string normalizedCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Projects
                .AsNoTracking()
                .AnyAsync(
                    project => project.Code == normalizedCode,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new ProjectQueryException(exception);
        }
    }

    public void Add(Project project)
    {
        dbContext.Projects.Add(project);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            throw new ProjectConflictException(exception);
        }
        catch (DbUpdateException exception)
        {
            throw new ProjectPersistenceException(exception);
        }
    }
}
