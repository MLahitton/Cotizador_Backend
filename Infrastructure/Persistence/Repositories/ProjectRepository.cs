using System.Data.Common;
using Application.Common.Abstractions.Projects;
using Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository(ApplicationDbContext dbContext)
    : IProjectRepository
{
    public async Task<Project?> FindByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Projects
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new ProjectQueryException(exception);
        }
    }

    public async Task<Project?> FindForUpdateByIdAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Projects
                .SingleOrDefaultAsync(
                    project => project.Id == projectId,
                    cancellationToken);
        }
        catch (DbException exception)
        {
            throw new ProjectQueryException(exception);
        }
    }

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

    public async Task<AdministrativeProjectSearchPage> SearchAsync(
        ProjectSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = dbContext.Projects
                .AsNoTracking()
                .AsQueryable();

            if (criteria.IsActive is { } isActive)
            {
                query = query.Where(project =>
                    project.IsActive == isActive);
            }

            if (criteria.ClientId is { } clientId)
            {
                query = query.Where(project =>
                    project.ClientId == clientId);
            }

            if (criteria.ClientType is { } clientType)
            {
                query = query.Where(project =>
                    project.Client.ClientType == clientType);
            }

            if (criteria.DocumentType is { } documentType)
            {
                query = query.Where(project =>
                    project.Client.DocumentType == documentType);
            }

            if (criteria.Search is { } search)
            {
                var pattern = $"%{EscapeLikePattern(search)}%";
                var normalizedSearch = NormalizeDocumentNumber(search);
                var documentPattern =
                    $"%{EscapeLikePattern(normalizedSearch)}%";

                query = query.Where(project =>
                    EF.Functions.ILike(project.Code, pattern, "\\")
                    || EF.Functions.ILike(project.Name, pattern, "\\")
                    || (project.Description != null
                        && EF.Functions.ILike(
                            project.Description,
                            pattern,
                            "\\"))
                    || (project.Location != null
                        && EF.Functions.ILike(
                            project.Location,
                            pattern,
                            "\\"))
                    || EF.Functions.ILike(
                        project.Client.LegalName,
                        pattern,
                        "\\")
                    || (project.Client.TradeName != null
                        && EF.Functions.ILike(
                            project.Client.TradeName,
                            pattern,
                            "\\"))
                    || (project.Client.DocumentNumber != null
                        && normalizedSearch.Length > 0
                        && EF.Functions.ILike(
                            project.Client.DocumentNumber
                                .Replace(" ", "")
                                .Replace(".", "")
                                .Replace("-", "")
                                .Replace("/", ""),
                            documentPattern,
                            "\\"))
                    || (project.Client.Email != null
                        && EF.Functions.ILike(
                            project.Client.Email,
                            pattern,
                            "\\"))
                    || (project.Client.Phone != null
                        && EF.Functions.ILike(
                            project.Client.Phone,
                            pattern,
                            "\\"))
                    || (project.Client.City != null
                        && EF.Functions.ILike(
                            project.Client.City,
                            pattern,
                            "\\")));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var skip = ((long)criteria.Page - 1L)
                * criteria.PageSize;

            if (totalCount == 0
                || skip >= totalCount
                || skip > int.MaxValue)
            {
                return new AdministrativeProjectSearchPage(
                    Array.Empty<AdministrativeProjectSearchItem>(),
                    totalCount);
            }

            var items = await query
                .OrderBy(project => project.Name)
                .ThenBy(project => project.Code)
                .ThenBy(project => project.Id)
                .Skip((int)skip)
                .Take(criteria.PageSize)
                .Select(project =>
                    new AdministrativeProjectSearchItem(
                        project.Id,
                        project.ClientId,
                        project.Code,
                        project.Name,
                        project.Description,
                        project.Location,
                        project.IsActive,
                        project.CreatedAtUtc,
                        project.UpdatedAtUtc,
                        project.Client.ClientType,
                        project.Client.LegalName,
                        project.Client.TradeName,
                        project.Client.DocumentType,
                        project.Client.DocumentNumber))
                .ToListAsync(cancellationToken);

            return new AdministrativeProjectSearchPage(
                items,
                totalCount);
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

    public async Task<bool> ExistsByCodeForOtherProjectAsync(
        Guid projectId,
        string normalizedCode,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Projects
                .AsNoTracking()
                .AnyAsync(
                    project => project.Id != projectId
                        && project.Code == normalizedCode,
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

    private static string NormalizeDocumentNumber(string value)
    {
        return value
            .Trim()
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace(".", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("/", "", StringComparison.Ordinal);
    }
}
