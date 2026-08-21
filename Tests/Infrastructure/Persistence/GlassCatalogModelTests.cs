using Domain.Catalogs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class GlassCatalogModelTests
{
    [Fact]
    public void Model_ConfiguresCatalogConstraintsIndexesAndSeed()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=model;Username=model;Password=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        var model = context.GetService<IDesignTimeModel>().Model;
        var glass = model.FindEntityType(typeof(GlassType))!;
        var range = model.FindEntityType(
            typeof(GlassPriceRangeVersion))!;

        Assert.Equal("core", glass.GetSchema());
        Assert.Equal("glass_types", glass.GetTableName());
        Assert.Equal(
            "glass_price_range_versions",
            range.GetTableName());
        Assert.Equal(
            "numeric(18,2)",
            range.FindProperty(
                nameof(GlassPriceRangeVersion.MinimumPricePerSquareMeter))!
                .GetColumnType());
        Assert.Equal(
            "numeric(18,2)",
            range.FindProperty(
                nameof(GlassPriceRangeVersion.MaximumPricePerSquareMeter))!
                .GetColumnType());
        Assert.Equal(
            "numeric(18,2)",
            range.FindProperty(
                nameof(GlassPriceRangeVersion.ExpectedAmountPerM2))!
                .GetColumnType());
        Assert.Contains(
            glass.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName() == "ux_glass_types_code");
        Assert.Contains(
            glass.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName() == "ux_glass_types_name");
        Assert.Equal(
            "numeric(8,3)",
            glass.FindProperty(nameof(GlassType.OuterThicknessMm))!
                .GetColumnType());
        Assert.Equal(
            "numeric(8,3)",
            glass.FindProperty(nameof(GlassType.InnerThicknessMm))!
                .GetColumnType());
        Assert.Equal(
            "numeric(8,3)",
            glass.FindProperty(nameof(GlassType.PvbThicknessMm))!
                .GetColumnType());
        Assert.Equal(
            "numeric(8,3)",
            glass.FindProperty(nameof(GlassType.ChamberThicknessMm))!
                .GetColumnType());
        Assert.Contains(
            range.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName()
                    == "ux_glass_price_range_versions_type_version");
        var openIndex = Assert.Single(
            range.GetIndexes(),
            value => value.GetDatabaseName()
                == "ux_glass_price_range_versions_open_type");
        Assert.True(openIndex.IsUnique);
        Assert.Equal("\"valid_to_utc\" IS NULL", openIndex.GetFilter());
        Assert.Equal(
            DeleteBehavior.Restrict,
            Assert.Single(range.GetForeignKeys()).DeleteBehavior);
        Assert.Equal(28, glass.GetSeedData().Count());
        Assert.Equal(8, range.GetSeedData().Count());
        Assert.Equal(6, range.GetSeedData().Count(value =>
            value["ValidToUtc"] is null));
        Assert.Equal(2, range.GetSeedData().Count(value =>
            value["Status"] is GlassPriceRangeStatus.Retired));
        Assert.All(
            range.GetSeedData().Where(value => value["ValidToUtc"] is null),
            value => Assert.Equal(
                GlassPriceRangeStatus.Preliminary,
                value[nameof(GlassPriceRangeVersion.Status)]));
    }
}
