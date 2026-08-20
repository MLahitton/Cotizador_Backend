using Domain.Catalogs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class CanonicalCatalogSeedTests
{
    private static readonly string[] SystemCodes =
    [
        "3890", "BARANDA", "DIVISION_BANO", "K100", "K40", "K50",
        "K55", "K70", "K90", "S35", "S50", "S80", "SG45",
        "SG_BATH_DIV_INOX", "SG_LOUVER", "SG_PERGOLA",
        "SG_PRIM_SIENA_CASEMENT", "SG_PRIM_SIENA_DBL_CASE",
        "SG_SKYLIGHT", "SG_SYS_NA", "SG_VEN70_POCKET_DOOR"
    ];

    private static readonly string[] FrameCodes = ["MARCO_47", "MARCO_58"];

    private static readonly string[] FinishCodes =
    [
        "ANODIZED_GRAY", "BLACK_MATTE", "SPECIAL",
        "STANDARD_NATURAL", "UNKNOWN"
    ];

    [Fact]
    public void ProductSystemSeed_ContainsRequiredCodesAndFlags()
    {
        var systems = SeedData<ProductSystem>();
        var byCode = systems.ToDictionary(value => Text(value, "Code"));

        Assert.Equal(SystemCodes, byCode.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(21, byCode.Count);
        Assert.All(systems, value => Assert.True((bool)value["IsActive"]!));
        Assert.False((bool)byCode["BARANDA"]["Priceable"]!);
        Assert.False((bool)byCode["DIVISION_BANO"]["Priceable"]!);
        Assert.True((bool)byCode["BARANDA"]["ActiveForRecognition"]!);
        Assert.True((bool)byCode["DIVISION_BANO"]["ActiveForRecognition"]!);
        Assert.True((bool)byCode["BARANDA"]["FuturePriceable"]!);
        Assert.True((bool)byCode["DIVISION_BANO"]["FuturePriceable"]!);
        Assert.True((bool)byCode["BARANDA"]["RequiresReview"]!);
        Assert.True((bool)byCode["DIVISION_BANO"]["RequiresReview"]!);
        Assert.All(
            byCode.Where(pair => pair.Key is not ("BARANDA"
                or "DIVISION_BANO"
                or "SG_BATH_DIV_INOX"
                or "SG_LOUVER"
                or "SG_PERGOLA"
                or "SG_SKYLIGHT"
                or "SG_SYS_NA")),
            pair => Assert.True((bool)pair.Value["Priceable"]!));
        Assert.Equal("VENECIA NAPOLES", Text(byCode["K70"], "CommercialName"));
        Assert.Equal("SLIDING_DOOR", Text(byCode["K70"], "FunctionalType"));
        Assert.True((bool)byCode["K70"]["IsSelectable"]!);
        Assert.Equal("POCKET", Text(byCode["SG_VEN70_POCKET_DOOR"], "Variant"));
        Assert.Equal("CASEMENT", Text(byCode["SG_PRIM_SIENA_CASEMENT"], "FunctionalType"));
        Assert.False((bool)byCode["SG_SYS_NA"]["IsSelectable"]!);
    }

    [Fact]
    public void FrameTypeSeed_ContainsOnlyConfirmedFrames()
    {
        var codes = SeedData<FrameType>()
            .Select(value => Text(value, "Code"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(FrameCodes, codes);
        Assert.DoesNotContain("SG0047", codes);
        Assert.DoesNotContain("SGT103", codes);
        Assert.DoesNotContain("SG1871", codes);
        Assert.DoesNotContain("SGT0087", codes);
    }

    [Fact]
    public void FinishTypeSeed_ContainsRequiredFinishes()
    {
        var finishes = SeedData<FinishType>();
        var byCode = finishes.ToDictionary(value => Text(value, "Code"));

        Assert.Equal(FinishCodes, byCode.Keys.Order(StringComparer.Ordinal));
        Assert.False((bool)byCode["BLACK_MATTE"]["RequiresReview"]!);
        Assert.True((bool)byCode["SPECIAL"]["RequiresReview"]!);
        Assert.True((bool)byCode["UNKNOWN"]["RequiresReview"]!);
        Assert.DoesNotContain("INOX", byCode.Keys);
    }

    [Fact]
    public void CatalogAliasSeed_ContainsConfirmedAliasesOnly()
    {
        var aliases = SeedData<CatalogAlias>();
        var byAlias = aliases.ToDictionary(value => Text(value, "NormalizedAlias"));

        Assert.Equal("K40", Text(byAlias["VENECIA SERIE 40"], "CanonicalCode"));
        Assert.Equal("K50", Text(byAlias["VENECIA SERIE 50"], "CanonicalCode"));
        Assert.Equal("K70", Text(byAlias["VENECIA SERIE 70"], "CanonicalCode"));
        Assert.Equal("MARCO_47", Text(byAlias["SG0047"], "CanonicalCode"));
        Assert.Equal("MARCO_58", Text(byAlias["SG0058"], "CanonicalCode"));
        Assert.Equal("BLACK_MATTE", Text(byAlias["NEGRO MATE"], "CanonicalCode"));
        Assert.DoesNotContain("V40", byAlias.Keys);
        Assert.DoesNotContain("INOX", byAlias.Keys);
        Assert.DoesNotContain("40", byAlias.Keys);
        Assert.Equal(aliases.Count, aliases.Select(value =>
            $"{value["Category"]}:{value["NormalizedAlias"]}")
            .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Model_ConfiguresUniqueIndexes()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Contains(
            model.FindEntityType(typeof(ProductSystem))!.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName() == "ux_product_systems_code");
        Assert.Contains(
            model.FindEntityType(typeof(FrameType))!.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName() == "ux_frame_types_code");
        Assert.Contains(
            model.FindEntityType(typeof(FinishType))!.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName() == "ux_finish_types_code");
        Assert.Contains(
            model.FindEntityType(typeof(CatalogAlias))!.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName()
                    == "ux_catalog_aliases_category_normalized_alias");
        var constraints = model.FindEntityType(typeof(ProductSystemConstraint))!;
        Assert.Equal("core", constraints.GetSchema());
        Assert.Equal("product_system_constraints", constraints.GetTableName());
        Assert.Empty(constraints.GetSeedData());
        Assert.Contains(
            constraints.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName()
                    == "ux_product_system_constraints_system_code");
        Assert.Contains(
            constraints.GetIndexes(),
            value => value.GetDatabaseName()
                == "ix_product_system_constraints_system_active_stage");
    }

    private static IReadOnlyList<IDictionary<string, object?>> SeedData<TEntity>()
    {
        using var context = CreateContext();
        return context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(TEntity))!.GetSeedData().ToArray();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model;Username=model")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static string Text(
        IDictionary<string, object?> value,
        string property) => (string)value[property]!;
}
