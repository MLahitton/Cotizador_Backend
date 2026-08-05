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
        "LAM_4_4",
        "LAM_4_4_GRAY",
        "LAM_5_5",
        "LAM_5_5_GRAY",
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

    [Fact]
    public void GlassTypeSeed_ContainsRequiredCanonicalCodes()
    {
        var glassTypes = GlassTypeSeeds();
        var codes = glassTypes.Select(value => Text(value, "Code")).ToArray();

        Assert.Equal(
            RequiredCodes.Order(StringComparer.Ordinal),
            codes.Order(StringComparer.Ordinal));
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Equal(code.ToUpperInvariant(), code));
        Assert.All(glassTypes, value => Assert.True((bool)value["IsActive"]!));
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
}
