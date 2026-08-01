using System.Reflection;
using Application.Common.Abstractions.Catalogs;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class GlassTypeCatalogRepositoryTranslationTests
{
    [Fact]
    public void Query_TranslatesFilteringProjectionAndEntityOrderingToSql()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=translation;Username=translation")
            .Options;
        using var context = new ApplicationDbContext(options);
        var repository = new GlassTypeCatalogRepository(context);
        var queryMethod = typeof(GlassTypeCatalogRepository).GetMethod(
            "Query",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(queryMethod);
        var query = Assert.IsAssignableFrom<
            IQueryable<GlassTypeCatalogReadModel>>(
                queryMethod.Invoke(repository, null));
        var expression = query.Expression.ToString();
        var sql = query.ToQueryString().Replace("\"", string.Empty);

        Assert.True(
            expression.IndexOf("OrderBy", StringComparison.Ordinal)
            < expression.IndexOf("Select", StringComparison.Ordinal));
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".code", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("is_active", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid_to_utc IS NULL", sql,
            StringComparison.OrdinalIgnoreCase);
    }
}
