using Domain.Catalogs;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class GlassCatalogSeedTests
{
    private static readonly string[] RequiredCodes =
    [
        "TEMP_5",
        "TEMP_6",
        "TEMP_8",
        "TEMP_10",
        "TEMP_4",
        "RAW_4_INC",
        "RAW_4_MINI_BOREAL",
        "RAW_5_INC",
        "RAW_6_INC",
        "LAM_4_4",
        "LAM_4_4_GRAY",
        "LAM_5_5",
        "LAM_5_5_GRAY",
        "LAM_4_038_6_INC",
        "LAM_4_076_6_INC",
        "LAM_4_114_6_INC",
        "LAM_6_076_AC_8_INC",
        "LAMT_5_114_5_INC",
        "LAMT_6_152_6_INC",
        "IGU_T5_CAM12_T6",
        "QG_PREMIUM_CL120",
        "QG_PREMIUM_CL150",
        "QG_PREMIUM_CL167",
        "QG_CLASSIC_BLUE",
        "QG_CLASSIC_BRONZE",
        "QG_CLASSIC_GREEN",
        "GLASS_NA",
        "UNKNOWN_GLASS"
    ];

    private static readonly Dictionary<string, (decimal Minimum, decimal Expected, decimal Maximum)>
        ExpectedPrices = new(StringComparer.Ordinal)
        {
            ["TEMP_5"] = (74000m, 74000m, 74000m),
            ["TEMP_6"] = (86000m, 86000m, 86000m),
            ["TEMP_8"] = (90000m, 90000m, 90000m),
            ["TEMP_10"] = (126000m, 126000m, 126000m),
            ["LAM_4_4"] = (90000m, 100000m, 110000m),
            ["LAM_5_5"] = (120000m, 130000m, 140000m)
        };

    private static readonly Dictionary<string, string> ExpectedNames =
        new(StringComparer.Ordinal)
        {
            ["TEMP_5"] = "COMPOSICION MONOLITICO TEMPLADO 5 MM INC",
            ["TEMP_6"] = "COMPOSICION MONOLITICO TEMPLADO 6 MM INC",
            ["TEMP_8"] = "COMPOSICION MONOLITICO TEMPLADO 8 MM INC",
            ["TEMP_10"] = "COMPOSICION MONOLITICO TEMPLADO 10 MM INC",
            ["TEMP_4"] = "COMPOSICION MONOLITICO TEMPLADO 4 MM INC",
            ["RAW_4_INC"] = "COMPOSICION MONOLITICO CRUDO 4 MM INC",
            ["RAW_4_MINI_BOREAL"] = "COMPOSICION MONOLITICO CRUDO 4 MM MINI BOREAL",
            ["RAW_5_INC"] = "COMPOSICION MONOLITICO CRUDO 5 MM INC",
            ["RAW_6_INC"] = "COMPOSICION MONOLITICO CRUDO 6 MM INC",
            ["LAM_4_4"] = "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM INC",
            ["LAM_4_4_GRAY"] = "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM GRIS + 4 MM INC",
            ["LAM_5_5"] = "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM INC + 5 MM INC",
            ["LAM_5_5_GRAY"] = "COMPOSICION LAMINADO CRUDO 5 MM INC + PVB 0,38 MM GRIS + 5 MM INC",
            ["LAM_4_038_6_INC"] = "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 6 MM INC",
            ["LAM_4_076_6_INC"] = "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 0,76 MM INC + 6 MM INC",
            ["LAM_4_114_6_INC"] = "COMPOSICION LAMINADO CRUDO 4 MM INC + PVB 1,14 MM INC + 6 MM INC",
            ["LAM_6_076_AC_8_INC"] = "COMPOSICION LAMINADO CRUDO 6 MM INC + PVB 0,76 MM ACÚSTICO + 8 MM INC",
            ["LAMT_5_114_5_INC"] = "COMPOSICION LAMINADO TEMPLADO 5 MM INC + PVB 1,14 MM INC + 5 MM INC",
            ["LAMT_6_152_6_INC"] = "COMPOSICION LAMINADO TEMPLADO 6 MM INC + PVB 1,52 MM INC + 6 MM INC",
            ["IGU_T5_CAM12_T6"] = "COMPOSICION TEMPLADO 5 MM INC + CÁMARA 12 MM + TEMPLADO 6 MM INC",
            ["QG_PREMIUM_CL120"] = "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL120",
            ["QG_PREMIUM_CL150"] = "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL150",
            ["QG_PREMIUM_CL167"] = "COMPOSICION CONTROL SOLAR QUALITY GLASS PREMIUM LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM CL167",
            ["QG_CLASSIC_BLUE"] = "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM BLUE",
            ["QG_CLASSIC_BRONZE"] = "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM BRONZE",
            ["QG_CLASSIC_GREEN"] = "COMPOSICION CONTROL SOLAR QUALITY GLASS CLASSIC LAMINADO CRUDO 4 MM INC + PVB 0,38 MM INC + 4 MM GREEN",
            ["GLASS_NA"] = "N.A.",
            ["UNKNOWN_GLASS"] = "Tipo de vidrio por confirmar"
        };

    [Fact]
    public void GlassTypeSeed_ContainsRequiredCanonicalCodes()
    {
        var glassTypes = GlassTypeSeeds();
        var codes = glassTypes.Select(value => Text(value, "Code")).ToArray();

        Assert.Equal(
            RequiredCodes.Order(StringComparer.Ordinal),
            codes.Order(StringComparer.Ordinal));
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            glassTypes.Count,
            glassTypes.Select(value => Text(value, "Name"))
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(codes, code => Assert.Equal(code.ToUpperInvariant(), code));
        Assert.All(glassTypes, value => Assert.True((bool)value["IsActive"]!));
        Assert.Equal(
            ExpectedNames,
            glassTypes.ToDictionary(
                value => Text(value, "Code"),
                value => Text(value, "Name"),
                StringComparer.Ordinal));
        Assert.Equal(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Id(glassTypes.Single(value => Text(value, "Code") == "LAM_4_4")));
        Assert.Equal(
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Id(glassTypes.Single(value => Text(value, "Code") == "LAM_4_4_GRAY")));
        Assert.Equal(
            Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Id(glassTypes.Single(value => Text(value, "Code") == "LAM_5_5")));
        Assert.Equal(
            Guid.Parse("10000000-0000-0000-0000-000000000004"),
            Id(glassTypes.Single(value => Text(value, "Code") == "LAM_5_5_GRAY")));
    }

    [Fact]
    public void GlassTypeSeed_ContainsHistoricalBdGnMetadata()
    {
        var glassTypes = GlassTypeSeeds()
            .ToDictionary(value => Text(value, "Code"), StringComparer.Ordinal);

        Assert.Equal("MONOLITHIC", Text(glassTypes["TEMP_4"], "Family"));
        Assert.Equal("TEMPERED", Text(glassTypes["TEMP_4"], "Composition"));
        Assert.Equal(4m, Decimal(glassTypes["TEMP_4"], "OuterThicknessMm"));
        Assert.Equal("RAW", Text(glassTypes["RAW_4_MINI_BOREAL"], "Composition"));
        Assert.Equal("MINI_BOREAL", Text(glassTypes["RAW_4_MINI_BOREAL"], "Pattern"));
        Assert.Equal(0.76m, Decimal(glassTypes["LAM_4_076_6_INC"], "PvbThicknessMm"));
        Assert.Equal(6m, Decimal(glassTypes["LAM_4_076_6_INC"], "InnerThicknessMm"));
        Assert.Equal("ACOUSTIC", Text(glassTypes["LAM_6_076_AC_8_INC"], "PvbType"));
        Assert.Equal(12m, Decimal(glassTypes["IGU_T5_CAM12_T6"], "ChamberThicknessMm"));
        Assert.Equal("QUALITY_GLASS_PREMIUM", Text(glassTypes["QG_PREMIUM_CL167"], "ProductLine"));
        Assert.Equal("CL167", Text(glassTypes["QG_PREMIUM_CL167"], "ProductToken"));
        Assert.Equal("QUALITY_GLASS_CLASSIC", Text(glassTypes["QG_CLASSIC_GREEN"], "ProductLine"));
        Assert.Equal("GREEN", Text(glassTypes["QG_CLASSIC_GREEN"], "ProductToken"));
        Assert.Equal("NOT_APPLICABLE", Text(glassTypes["GLASS_NA"], "Family"));
        Assert.True((bool)glassTypes["GLASS_NA"]["RequiresReview"]!);
        Assert.False((bool)glassTypes["UNKNOWN_GLASS"]["IsSelectable"]!);
    }

    [Fact]
    public void GlassPriceRangeSeed_ContainsOnlyConfirmedPreliminaryPrices()
    {
        var glassTypes = GlassTypeSeeds()
            .ToDictionary(value => Id(value), value => Text(value, "Code"));
        var priceRanges = CurrentPriceRangeSeeds();

        Assert.Equal(6, priceRanges.Count);
        Assert.Equal(
            ExpectedPrices.Keys.Order(StringComparer.Ordinal),
            priceRanges.Select(value => glassTypes[Id(value, "GlassTypeId")])
                .Order(StringComparer.Ordinal));

        foreach (var range in priceRanges)
        {
            var code = glassTypes[Id(range, "GlassTypeId")];
            var expected = ExpectedPrices[code];
            var minimum = Money(range, "MinimumPricePerSquareMeter");
            var actualExpected = Money(range, "ExpectedAmountPerM2");
            var maximum = Money(range, "MaximumPricePerSquareMeter");

            Assert.Equal(expected.Minimum, minimum);
            Assert.Equal(expected.Expected, actualExpected);
            Assert.Equal(expected.Maximum, maximum);
            Assert.True(minimum <= maximum);
            Assert.True(minimum <= actualExpected);
            Assert.True(actualExpected <= maximum);
            Assert.Equal("COP", Text(range, "Currency"));
            Assert.Equal(GlassPriceRangeStatus.Preliminary, range["Status"]);
            Assert.Null(range["ValidToUtc"]);

            if (code.StartsWith("TEMP_", StringComparison.Ordinal))
            {
                Assert.Equal(minimum, maximum);
            }
        }
    }

    [Fact]
    public void GlassPriceRangeSeed_DoesNotExposeGrayOrUnknownAsCurrent()
    {
        var glassTypes = GlassTypeSeeds()
            .ToDictionary(value => Text(value, "Code"), value => Id(value));
        var pricedGlassTypeIds = CurrentPriceRangeSeeds()
            .Select(value => Id(value, "GlassTypeId"))
            .ToHashSet();

        Assert.DoesNotContain(glassTypes["LAM_4_4_GRAY"], pricedGlassTypeIds);
        Assert.DoesNotContain(glassTypes["LAM_5_5_GRAY"], pricedGlassTypeIds);
        Assert.DoesNotContain(glassTypes["UNKNOWN_GLASS"], pricedGlassTypeIds);
        Assert.DoesNotContain(glassTypes["TEMP_4"], pricedGlassTypeIds);
        Assert.DoesNotContain(glassTypes["RAW_4_INC"], pricedGlassTypeIds);
        Assert.DoesNotContain(glassTypes["QG_PREMIUM_CL167"], pricedGlassTypeIds);
        Assert.DoesNotContain(glassTypes["GLASS_NA"], pricedGlassTypeIds);
    }

    [Fact]
    public void GlassPriceRangeSeed_PreservesRetiredGrayHistory()
    {
        var glassTypes = GlassTypeSeeds()
            .ToDictionary(value => Id(value), value => Text(value, "Code"));
        var retiredGrayRanges = PriceRangeSeeds()
            .Where(value => value["ValidToUtc"] is not null)
            .ToArray();

        Assert.Equal(2, retiredGrayRanges.Length);
        Assert.Equal(
            ["LAM_4_4_GRAY", "LAM_5_5_GRAY"],
            retiredGrayRanges.Select(value => glassTypes[Id(value, "GlassTypeId")])
                .Order(StringComparer.Ordinal));
        Assert.All(
            retiredGrayRanges,
            value => Assert.Equal(GlassPriceRangeStatus.Retired, value["Status"]));
    }

    private static IReadOnlyList<IDictionary<string, object?>> GlassTypeSeeds() =>
        SeedData<GlassType>();

    private static IReadOnlyList<IDictionary<string, object?>> PriceRangeSeeds() =>
        SeedData<GlassPriceRangeVersion>();

    private static IReadOnlyList<IDictionary<string, object?>> CurrentPriceRangeSeeds() =>
        PriceRangeSeeds().Where(value => value["ValidToUtc"] is null).ToArray();

    private static IReadOnlyList<IDictionary<string, object?>> SeedData<TEntity>()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model;Username=model")
            .Options;
        using var context = new ApplicationDbContext(options);
        return context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(TEntity))!.GetSeedData().ToArray();
    }

    private static string Text(
        IDictionary<string, object?> value,
        string property) => (string)value[property]!;

    private static Guid Id(
        IDictionary<string, object?> value,
        string property = "Id") => (Guid)value[property]!;

    private static decimal Money(
        IDictionary<string, object?> value,
        string property) => (decimal)value[property]!;

    private static decimal Decimal(
        IDictionary<string, object?> value,
        string property) => (decimal)value[property]!;
}
