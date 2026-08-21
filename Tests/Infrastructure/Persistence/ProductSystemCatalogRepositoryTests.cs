using System.Reflection;
using Application.Common.Abstractions.Catalogs;
using Domain.Catalogs;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class ProductSystemCatalogRepositoryTranslationTests
{
    [Fact]
    public void ListActiveSelectableQuery_FiltersEntityBeforeProjection()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=translation;Username=translation")
            .Options;
        using var context = new ApplicationDbContext(options);
        var repository = new ProductSystemCatalogRepository(context);
        var queryMethod = typeof(ProductSystemCatalogRepository).GetMethod(
            "Query",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(queryMethod);
        var query = Assert.IsAssignableFrom<
            IQueryable<ProductSystemCatalogReadModel>>(
                queryMethod.Invoke(repository, [true]));
        var expression = query.Expression.ToString();
        var sql = query.ToQueryString().Replace("\"", string.Empty);

        Assert.Contains("IsSelectable", expression, StringComparison.Ordinal);
        Assert.True(
            expression.IndexOf("Where", StringComparison.Ordinal)
            < expression.IndexOf("Select", StringComparison.Ordinal));
        Assert.Contains("is_selectable", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".code", sql, StringComparison.OrdinalIgnoreCase);
    }
}

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class ProductSystemCatalogRepositoryPostgreSqlTests(
    PostgreSqlIntegrationFixture fixture)
{
    [Fact]
    public async Task ListActiveSelectableAsync_ReturnsSelectableSystemsWithActiveConstraints()
    {
        fixture.RequireAvailable();
        await using var context = fixture.CreateDbContext();
        var productSystem = await context.ProductSystems
            .Where(value => value.IsActive && value.IsSelectable)
            .OrderBy(value => value.Code)
            .FirstAsync(TestContext.Current.CancellationToken);
        context.ProductSystemConstraints.AddRange(
            ProductSystemConstraint.Create(
                productSystem.Id,
                "A_ACTIVE",
                ProductSystemConstraintType.MinWidth,
                ProductSystemConstraintScope.Opening,
                ConstraintEvaluationStage.PreSelection,
                ProductSystemConstraintSeverity.Hard,
                ProductSystemConstraintKnowledgeClass.VerifiedTechnical,
                requiresReviewWhenUnknown: false,
                ProductSystemConstraintSourceType.Manual,
                DateTimeOffset.UtcNow,
                minValue: 100,
                unit: "mm"),
            ProductSystemConstraint.Create(
                productSystem.Id,
                "M_INACTIVE",
                ProductSystemConstraintType.MaxWidth,
                ProductSystemConstraintScope.Opening,
                ConstraintEvaluationStage.PreSelection,
                ProductSystemConstraintSeverity.Hard,
                ProductSystemConstraintKnowledgeClass.VerifiedTechnical,
                requiresReviewWhenUnknown: false,
                ProductSystemConstraintSourceType.Manual,
                DateTimeOffset.UtcNow,
                maxValue: 1_000,
                unit: "mm",
                isActive: false),
            ProductSystemConstraint.Create(
                productSystem.Id,
                "Z_ACTIVE",
                ProductSystemConstraintType.MaxWidth,
                ProductSystemConstraintScope.Opening,
                ConstraintEvaluationStage.PreSelection,
                ProductSystemConstraintSeverity.Hard,
                ProductSystemConstraintKnowledgeClass.VerifiedTechnical,
                requiresReviewWhenUnknown: false,
                ProductSystemConstraintSourceType.Manual,
                DateTimeOffset.UtcNow,
                maxValue: 2_000,
                unit: "mm"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new ProductSystemCatalogRepository(context);
        var systems = await repository.ListActiveSelectableAsync(
            TestContext.Current.CancellationToken);

        Assert.NotEmpty(systems);
        Assert.All(systems, value =>
        {
            Assert.True(value.IsActive);
            Assert.True(value.IsSelectable);
        });
        Assert.Equal(
            systems.Select(value => value.Code).Order(StringComparer.Ordinal),
            systems.Select(value => value.Code));
        Assert.Equal(systems.Select(value => value.Id).Distinct().Count(),
            systems.Count);
        var projected = Assert.Single(
            systems,
            value => value.Id == productSystem.Id);
        Assert.Equal(
            ["A_ACTIVE", "Z_ACTIVE"],
            projected.Constraints.Select(value => value.Code).ToArray());
    }
}
