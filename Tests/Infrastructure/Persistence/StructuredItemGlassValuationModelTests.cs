using Domain.PreQuotes;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class StructuredItemGlassValuationModelTests
{
    [Fact]
    public void Model_MapsValuationSnapshotAndOneToOneConstraint()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model;Username=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        var entity = context.Model.FindEntityType(
            typeof(StructuredExtractionItemGlassValuation));

        Assert.NotNull(entity);
        Assert.Equal("structured_extraction_item_glass_valuations",
            entity.GetTableName());
        Assert.Equal("core", entity.GetSchema());
        Assert.Equal(typeof(Guid), entity.FindPrimaryKey()!.Properties.Single().ClrType);
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Single().Name == "StructuredExtractionItemId");
        Assert.Equal((18, 6), Precision(entity, "UnitAreaSquareMeters"));
        Assert.Equal((18, 6), Precision(entity, "TotalAreaSquareMeters"));
        foreach (var property in new[] { "MinimumPricePerSquareMeter",
            "MaximumPricePerSquareMeter", "MinimumAmount", "MaximumAmount" })
            Assert.Equal((18, 2), Precision(entity, property));
        Assert.Equal(3, entity.FindProperty("Currency")!.GetMaxLength());
        Assert.False(entity.FindProperty("Status")!.IsNullable);
        Assert.True(entity.FindProperty("Reason")!.IsNullable);
        Assert.False(entity.FindProperty("CalculatedAtUtc")!.IsNullable);
        Assert.Equal(3, entity.GetForeignKeys().Count());
        Assert.Contains(entity.GetForeignKeys(), foreignKey =>
            foreignKey.PrincipalEntityType.ClrType == typeof(StructuredExtractionItem)
            && foreignKey.DeleteBehavior == DeleteBehavior.Cascade
            && foreignKey.IsUnique);
        Assert.Equal(2, entity.GetForeignKeys().Count(foreignKey =>
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict));
    }

    private static (int? Precision, int? Scale) Precision(
        IEntityType entity, string property) =>
        (entity.FindProperty(property)!.GetPrecision(),
            entity.FindProperty(property)!.GetScale());
}
