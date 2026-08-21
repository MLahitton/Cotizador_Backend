using Domain.Catalogs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class CanonicalCatalogSeedTests
{
    private static readonly string[] RequiredSystemCodes =
    [
        "3890", "K100", "K40", "K50", "K55", "K70", "S35",
        "S50", "S80", "SG45", "SG_BATH_DIV_INOX", "SG_LOUVER",
        "SG_PERGOLA", "SG_PRIM_SIENA_CASEMENT",
        "SG_PRIM_SIENA_DBL_CASE", "SG_SKYLIGHT", "SG_SYS_NA",
        "SG_VEN70_POCKET_DOOR", "SYS_DESLIZANTE_TWIN_DN",
        "SYS_PLEGABLE_TAURO", "SYS_APILABLE_SIGMA"
    ];

    private static readonly string[] FrameCodes = ["MARCO_47", "MARCO_58"];

    private static readonly string[] FinishCodes =
    [
        "ANODIZED_GRAY", "BLACK_MATTE", "FINISH_AN001",
        "FINISH_CHAMPAGNE_POLY", "FINISH_GRAY_POLYESTER", "FINISH_INOX",
        "FINISH_NA", "FINISH_PP003", "SPECIAL", "STANDARD_NATURAL",
        "UNKNOWN"
    ];

    [Fact]
    public void ProductSystemSeed_ContainsRequiredCodesAndFlags()
    {
        var systems = SeedData<ProductSystem>();
        var byCode = systems.ToDictionary(value => Text(value, "Code"));

        Assert.Equal(77, byCode.Count);
        foreach (var code in RequiredSystemCodes)
        {
            Assert.Contains(code, byCode.Keys);
        }

        Assert.All(systems, value => Assert.True((bool)value["IsActive"]!));
        Assert.Equal(
            systems.Count,
            systems.Select(value => Text(value, "Code"))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            systems.Count,
            systems.Select(value => Text(value, "Name"))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(systems, value => Assert.False(string.IsNullOrWhiteSpace(
            Text(value, "TechnicalName"))));
        Assert.Equal("VENECIA NAPOLES", Text(byCode["K70"], "CommercialName"));
        Assert.Equal("SLIDING_DOOR", Text(byCode["K70"], "FunctionalType"));
        Assert.Equal(
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES",
            Text(byCode["K70"], "Name"));
        Assert.Equal(
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO SISTEMA VENECIA SERIE 70",
            Text(byCode["K70"], "TechnicalName"));
        Assert.True((bool)byCode["K70"]["IsSelectable"]!);
        Assert.Equal(
            "CUERPO PROYECTANTE LINEA CLASSIC PRIMAVERA SIENA",
            Text(byCode["S35"], "Name"));
        Assert.Equal(
            "CUERPO PROYECTANTE LINEA CLASSIC SISTEMA PRIMAVERA SERIE SG 4",
            Text(byCode["S35"], "TechnicalName"));
        Assert.Equal("PRIMAVERA SIENA", Text(byCode["S35"], "Family"));
        Assert.Equal(
            "CUERPO FIJO LINEA PREMIUM TIPO EUROPEO VENECIA FERMO",
            Text(byCode["K40"], "Name"));
        Assert.Equal(
            "VENTANA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA MONZA",
            Text(byCode["K50"], "Name"));
        Assert.Equal(
            "PUERTA CORREDIZA LINEA PREMIUM TIPO EUROPEO VENECIA NAPOLES TIPO POKET",
            Text(byCode["SG_VEN70_POCKET_DOOR"], "Name"));
        Assert.Equal("POCKET", Text(byCode["SG_VEN70_POCKET_DOOR"], "Variant"));
        Assert.Equal("CASEMENT", Text(byCode["SG_PRIM_SIENA_CASEMENT"], "FunctionalType"));
        Assert.Equal("GRILLE", Text(byCode["SG_LOUVER"], "FunctionalType"));
        Assert.Equal("SHOWER_DIVISION", Text(byCode["SG_BATH_DIV_INOX"], "FunctionalType"));
        Assert.False((bool)byCode["SG_SYS_NA"]["IsSelectable"]!);
        Assert.True((bool)byCode["SG_SYS_NA"]["RequiresReview"]!);
        Assert.True((bool)byCode["SYS_APILABLE_SIGMA"]["RequiresReview"]!);
    }

    [Fact]
    public void ProductSystemSeed_DoesNotDuplicateCriticalMigrationCodes()
    {
        var systems = SeedData<ProductSystem>();
        var criticalCodes = new[]
        {
            "SG_PRIM_SIENA_CASEMENT",
            "SG_PRIM_SIENA_DBL_CASE",
            "SG_VEN70_POCKET_DOOR",
            "K40",
            "K50",
            "K55",
            "K70",
            "K100",
            "S35",
            "S50",
            "S80",
            "3890"
        };

        foreach (var code in criticalCodes)
        {
            var matches = systems
                .Where(value => string.Equals(
                    Text(value, "Code"),
                    code,
                    StringComparison.Ordinal))
                .ToArray();

            Assert.Single(matches);
        }

        var byCode = systems.ToDictionary(value => Text(value, "Code"));
        Assert.Equal("CASEMENT", Text(byCode["SG_PRIM_SIENA_CASEMENT"], "FunctionalType"));
        Assert.Equal("PRIMAVERA SIENA", Text(byCode["SG_PRIM_SIENA_CASEMENT"], "Family"));
        Assert.Equal("SG 4", Text(byCode["SG_PRIM_SIENA_CASEMENT"], "Series"));
        Assert.Equal("STANDARD", Text(byCode["SG_PRIM_SIENA_CASEMENT"], "Variant"));
        Assert.Equal("DOUBLE_CASEMENT", Text(byCode["SG_PRIM_SIENA_DBL_CASE"], "FunctionalType"));
        Assert.Equal("POCKET", Text(byCode["SG_VEN70_POCKET_DOOR"], "Variant"));
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
        Assert.Equal(
            "ALUCOLOR POLIESTER NEGRO MATE PP13",
            Text(byCode["BLACK_MATTE"], "Name"));
        Assert.Equal("PAINTED", Text(byCode["BLACK_MATTE"], "NormalizedType"));
        Assert.Equal("BLACK", Text(byCode["BLACK_MATTE"], "Color"));
        Assert.Equal("MATTE", Text(byCode["BLACK_MATTE"], "Texture"));
        Assert.Equal("POLYESTER", Text(byCode["BLACK_MATTE"], "Process"));
        Assert.Equal("PP13", Text(byCode["BLACK_MATTE"], "CommercialCode"));
        Assert.Equal("ALUMINUM", Text(byCode["BLACK_MATTE"], "Material"));
        Assert.True((bool)byCode["BLACK_MATTE"]["IsSelectable"]!);
        Assert.Equal(
            "ALUCOLOR POLIESTER BLANCO PP003",
            Text(byCode["FINISH_PP003"], "Name"));
        Assert.Equal("PP003", Text(byCode["FINISH_PP003"], "CommercialCode"));
        Assert.Equal(
            "ALUCOLOR POLIESTER PINTURA GRIS",
            Text(byCode["FINISH_GRAY_POLYESTER"], "Name"));
        Assert.Equal(
            "ALUCOLOR POLIESTER PINTURA CHAMPAÑA",
            Text(byCode["FINISH_CHAMPAGNE_POLY"], "Name"));
        Assert.Equal(
            "ANODIZADO BLANCO MATE AN001",
            Text(byCode["FINISH_AN001"], "Name"));
        Assert.Equal("AN001", Text(byCode["FINISH_AN001"], "CommercialCode"));
        Assert.Equal("INOX", Text(byCode["FINISH_INOX"], "Name"));
        Assert.Equal("STAINLESS_STEEL", Text(byCode["FINISH_INOX"], "Material"));
        Assert.Equal("N.A", Text(byCode["FINISH_NA"], "Name"));
        Assert.True((bool)byCode["FINISH_NA"]["RequiresReview"]!);
        Assert.False((bool)byCode["STANDARD_NATURAL"]["IsSelectable"]!);
        Assert.False((bool)byCode["ANODIZED_GRAY"]["IsSelectable"]!);
        Assert.True((bool)byCode["SPECIAL"]["RequiresReview"]!);
        Assert.True((bool)byCode["UNKNOWN"]["RequiresReview"]!);
        Assert.Equal(
            finishes.Count,
            finishes.Select(value => Text(value, "Name"))
                .Distinct(StringComparer.Ordinal)
                .Count());
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
        Assert.Equal("BLACK_MATTE", Text(byAlias["PP13"], "CanonicalCode"));
        Assert.Equal("FINISH_PP003", Text(byAlias["PP003"], "CanonicalCode"));
        Assert.Equal("FINISH_AN001", Text(byAlias["AN001"], "CanonicalCode"));
        Assert.Equal("FINISH_INOX", Text(byAlias["INOX"], "CanonicalCode"));
        Assert.Equal("FINISH_NA", Text(byAlias["N.A"], "CanonicalCode"));
        Assert.DoesNotContain("V40", byAlias.Keys);
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
            model.FindEntityType(typeof(FinishType))!.GetIndexes(),
            value => value.IsUnique
                && value.GetDatabaseName() == "ux_finish_types_name");
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
