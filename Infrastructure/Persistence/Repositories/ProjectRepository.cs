using System.Data.Common;
using Application.Common.Abstractions.Projects;
using Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository(ApplicationDbContext dbContext)
    : IProjectRepository
{
    public async Task<ProjectSearchPage> SearchActiveByClientAsync(
        Guid clientId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.Projects
                .AsNoTracking()
                .Where(project =>
                    project.ClientId == clientId
                    && project.IsActive);

            if (search is not null)
            {
                var escapedSearch = EscapeLikePattern(search);
                var pattern = $"%{escapedSearch}%";

                query = query.Where(project =>
                    EF.Functions.ILike(
                        project.Code,
                        pattern,
                        "\\")
                    || EF.Functions.ILike(
                        project.Name,
                        pattern,
                        "\\")
                    || (project.Description != null
                        && EF.Functions.ILike(
                            project.Description,
                            pattern,
                            "\\"))
                    || (project.Location != null
                        && EF.Functions.ILike(
                            project.Location,
                            pattern,
                            "\\")));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var skip = ((long)page - 1L) * pageSize;

            if (totalCount == 0
                || skip >= totalCount
                || skip > int.MaxValue)
            {
                return new ProjectSearchPage(
                    Array.Empty<Project>(),
                    totalCount);
            }

            var items = await query
                .OrderBy(project => project.Name)
                .ThenBy(project => project.Code)
                .Skip((int)skip)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new ProjectSearchPage(items, totalCount);
        }
        catch (DbException exception)
        {
            throw new ProjectQueryException(exception);
        }
    }

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

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
