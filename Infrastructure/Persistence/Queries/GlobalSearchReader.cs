using Application.Common.Abstractions.Search;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Queries;

public sealed class GlobalSearchReader(
    ApplicationDbContext dbContext)
    : IGlobalSearchReader
{
    public async Task<GlobalSearchSnapshot> SearchAsync(
        Guid userId,
        bool isAdmin,
        string search,
        int limitPerCategory,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = search.Trim();
        var pattern = $"%{EscapeLikePattern(normalizedSearch)}%";

        var projectsQuery = dbContext.Projects
            .AsNoTracking()
            .AsQueryable();

        if (!isAdmin)
        {
            projectsQuery = projectsQuery.Where(
                project =>
                    project.CreatedByUserId == userId);
        }

        var projects = await projectsQuery
            .Where(project =>
                EF.Functions.ILike(
                    project.Code,
                    pattern,
                    "\\")
                || EF.Functions.ILike(
                    project.Name,
                    pattern,
                    "\\")
                || EF.Functions.ILike(
                    project.Client.LegalName,
                    pattern,
                    "\\")
                || (
                    project.Client.TradeName != null
                    && EF.Functions.ILike(
                        project.Client.TradeName,
                        pattern,
                        "\\")
                ))
            .OrderBy(project =>
                project.Name)
            .ThenBy(project =>
                project.Code)
            .Take(limitPerCategory)
            .Select(project =>
                new GlobalSearchProjectSnapshot(
                    project.Id,
                    project.Code,
                    project.Name,
                    project.Client.TradeName
                        ?? project.Client.LegalName))
            .ToArrayAsync(cancellationToken);

        var preQuotesQuery = dbContext.PreQuotes
            .AsNoTracking()
            .AsQueryable();

        if (!isAdmin)
        {
            preQuotesQuery = preQuotesQuery.Where(
                preQuote =>
                    preQuote.CreatedByUserId == userId);
        }

        var preQuotes = await preQuotesQuery
            .Where(preQuote =>
                EF.Functions.ILike(
                    preQuote.Serial,
                    pattern,
                    "\\")
                || (
                    preQuote.Name != null
                    && EF.Functions.ILike(
                        preQuote.Name,
                        pattern,
                        "\\")
                )
                || EF.Functions.ILike(
                    preQuote.Project.Code,
                    pattern,
                    "\\")
                || EF.Functions.ILike(
                    preQuote.Project.Name,
                    pattern,
                    "\\"))
            .OrderBy(preQuote =>
                preQuote.Serial)
            .Take(limitPerCategory)
            .Select(preQuote =>
                new GlobalSearchPreQuoteSnapshot(
                    preQuote.Id,
                    preQuote.Serial,
                    preQuote.Name,
                    preQuote.ProjectId,
                    preQuote.Project.Code,
                    preQuote.Project.Name))
            .ToArrayAsync(cancellationToken);

        var clientsQuery = dbContext.Clients
            .AsNoTracking()
            .AsQueryable();

        if (!isAdmin)
        {
            clientsQuery = clientsQuery.Where(
                client =>
                    client.CreatedByUserId == userId);
        }

        var clients = await clientsQuery
            .Where(client =>
                EF.Functions.ILike(
                    client.LegalName,
                    pattern,
                    "\\")
                || (
                    client.TradeName != null
                    && EF.Functions.ILike(
                        client.TradeName,
                        pattern,
                        "\\")
                )
                || (
                    client.DocumentNumber != null
                    && EF.Functions.ILike(
                        client.DocumentNumber,
                        pattern,
                        "\\")
                ))
            .OrderBy(client =>
                client.TradeName ?? client.LegalName)
            .Take(limitPerCategory)
            .Select(client =>
                new GlobalSearchClientSnapshot(
                    client.Id,
                    client.TradeName
                        ?? client.LegalName,
                    client.DocumentType != null
                        ? client.DocumentType.ToString()
                        : null,
                    client.DocumentNumber))
            .ToArrayAsync(cancellationToken);

        return new GlobalSearchSnapshot(
            projects,
            preQuotes,
            clients);
    }

    private static string EscapeLikePattern(
        string value)
    {
        return value
            .Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "%",
                "\\%",
                StringComparison.Ordinal)
            .Replace(
                "_",
                "\\_",
                StringComparison.Ordinal);
    }
}