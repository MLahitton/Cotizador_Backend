using System.Text.Json;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class FinishCandidateResolverTests
{
    private static readonly FinishCandidateResolver Resolver = new();

    [Fact]
    public void Resolve_PaintedBlackMatte_ReturnsBdGnPp13()
    {
        var result = Resolver.Resolve(
            Input(
                raw: "negro pintura al horno",
                normalizedType: "PAINTED",
                color: "BLACK",
                texture: "MATTE"),
            Catalog());

        Assert.False(result.RequiresReview);
        Assert.Equal(0.95m, result.Confidence);
        Assert.Equal("BLACK_MATTE", result.Suggested!.Code);
        Assert.Equal(
            "ALUCOLOR POLIESTER NEGRO MATE PP13",
            result.Suggested.DisplayName);
    }

    [Fact]
    public void Resolve_ExplicitPp003_ReturnsBdGnWhitePolyester()
    {
        var result = Resolver.Resolve(
            Input(
                normalizedType: "PAINTED",
                color: "WHITE",
                explicitCode: "PP003"),
            Catalog());

        Assert.False(result.RequiresReview);
        Assert.Equal(1m, result.Confidence);
        Assert.Equal("FINISH_PP003", result.Suggested!.Code);
        Assert.Equal(
            "ALUCOLOR POLIESTER BLANCO PP003",
            result.Suggested.DisplayName);
    }

    [Fact]
    public void Resolve_AnodizedWhiteMatteExplicitAn001_ReturnsBdGnAnodized()
    {
        var result = Resolver.Resolve(
            Input(
                normalizedType: "ANODIZED",
                color: "WHITE",
                texture: "MATTE",
                explicitCode: "AN001"),
            Catalog());

        Assert.False(result.RequiresReview);
        Assert.Equal("FINISH_AN001", result.Suggested!.Code);
        Assert.Equal("ANODIZADO BLANCO MATE AN001", result.Suggested.DisplayName);
    }

    [Theory]
    [InlineData("GRAY", "FINISH_GRAY_POLYESTER", "ALUCOLOR POLIESTER PINTURA GRIS")]
    [InlineData("CHAMPAGNE", "FINISH_CHAMPAGNE_POLY", "ALUCOLOR POLIESTER PINTURA CHAMPAÑA")]
    public void Resolve_PaintedColorWithUniqueMatch_ReturnsBdGnFinish(
        string color,
        string expectedCode,
        string expectedName)
    {
        var result = Resolver.Resolve(
            Input(normalizedType: "PAINTED", color: color),
            Catalog());

        Assert.False(result.RequiresReview);
        Assert.Equal(expectedCode, result.Suggested!.Code);
        Assert.Equal(expectedName, result.Suggested.DisplayName);
    }

    [Fact]
    public void Resolve_StainlessSteel_ReturnsInox()
    {
        var result = Resolver.Resolve(
            Input(normalizedType: "STAINLESS_STEEL", raw: "inox"),
            Catalog());

        Assert.False(result.RequiresReview);
        Assert.Equal("FINISH_INOX", result.Suggested!.Code);
        Assert.Equal("INOX", result.Suggested.DisplayName);
    }

    [Fact]
    public void Resolve_MissingFinish_ReturnsReview()
    {
        var result = Resolver.Resolve(Input(), Catalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.Contains(
            FinishResolutionReviewReasons.FinishNotSpecified,
            result.ReviewReasons);
    }

    [Fact]
    public void Resolve_PaintedWithoutColor_ReturnsAmbiguous()
    {
        var result = Resolver.Resolve(
            Input(normalizedType: "PAINTED"),
            Catalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.Contains(
            FinishResolutionReviewReasons.FinishAmbiguous,
            result.ReviewReasons);
        Assert.Contains(result.Alternatives, value => value.Code == "BLACK_MATTE");
        Assert.Contains(result.Alternatives, value => value.Code == "FINISH_PP003");
    }

    [Fact]
    public void Resolve_UnknownFinish_ReturnsNoCompatibleCandidate()
    {
        var result = Resolver.Resolve(
            Input(raw: "acabado extraterrestre", color: "PURPLE"),
            Catalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.Contains(
            FinishResolutionReviewReasons.FinishNoCompatibleCandidate,
            result.ReviewReasons);
    }

    [Fact]
    public void Resolve_ConflictingExplicitCodeAndColor_ReturnsConflict()
    {
        var result = Resolver.Resolve(
            Input(explicitCode: "PP13", color: "WHITE"),
            Catalog());

        Assert.True(result.RequiresReview);
        Assert.Null(result.Suggested);
        Assert.Contains(
            FinishResolutionReviewReasons.FinishConflictingSignals,
            result.ReviewReasons);
    }

    [Fact]
    public void Resolve_NotApplicable_IsSeparateFromUnknown()
    {
        var result = Resolver.Resolve(
            Input(raw: "N.A", normalizedType: "NOT_APPLICABLE"),
            Catalog());

        Assert.True(result.RequiresReview);
        Assert.Equal("FINISH_NA", result.Suggested!.Code);
        Assert.NotEqual("UNKNOWN", result.Suggested.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(50)]
    public void ResolveMany_ProcessesAnyItemCountWithoutFixedLimit(int count)
    {
        var inputs = Enumerable.Range(0, count)
            .Select(_ => Input(
                raw: "negro pintura al horno",
                normalizedType: "PAINTED",
                color: "BLACK",
                texture: "MATTE"))
            .ToArray();

        var results = Resolver.ResolveMany(inputs, Catalog());

        Assert.Equal(count, results.Count);
        Assert.All(results, result =>
        {
            Assert.False(result.RequiresReview);
            Assert.Equal("BLACK_MATTE", result.Suggested!.Code);
        });
    }

    [Fact]
    public void Resolve_RealAi2FixtureV01Finish_ReturnsBdGnPp13()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            RepositoryFile(
                "Tests/Fixtures/DocumentProcessing/ai2-real-ventaneria-puertas.json")));
        var v01 = document.RootElement.GetProperty("elements")
            .EnumerateArray()
            .Single(value => Text(value, "reference") == "V-01");
        var finish = v01.GetProperty("finish");

        var result = Resolver.Resolve(
            Input(
                raw: Text(finish, "raw_description"),
                normalizedType: Text(finish, "normalized_type"),
                color: Text(finish, "color"),
                texture: Text(finish, "texture"),
                explicitCode: Text(finish, "code")),
            Catalog());

        Assert.False(result.RequiresReview);
        Assert.Equal("BLACK_MATTE", result.Suggested!.Code);
        Assert.Equal(
            "ALUCOLOR POLIESTER NEGRO MATE PP13",
            result.Suggested.DisplayName);
    }

    internal static IReadOnlyList<FinishTypeCatalogReadModel> Catalog() =>
    [
        Finish("BLACK_MATTE", "ALUCOLOR POLIESTER NEGRO MATE PP13",
            normalizedType: "PAINTED", color: "BLACK", texture: "MATTE",
            process: "POLYESTER", commercialCode: "PP13",
            material: "ALUMINUM"),
        Finish("FINISH_PP003", "ALUCOLOR POLIESTER BLANCO PP003",
            normalizedType: "PAINTED", color: "WHITE",
            process: "POLYESTER", commercialCode: "PP003",
            material: "ALUMINUM"),
        Finish("FINISH_GRAY_POLYESTER", "ALUCOLOR POLIESTER PINTURA GRIS",
            normalizedType: "PAINTED", color: "GRAY",
            process: "POLYESTER", material: "ALUMINUM"),
        Finish("FINISH_CHAMPAGNE_POLY", "ALUCOLOR POLIESTER PINTURA CHAMPAÑA",
            normalizedType: "PAINTED", color: "CHAMPAGNE",
            process: "POLYESTER", material: "ALUMINUM"),
        Finish("FINISH_AN001", "ANODIZADO BLANCO MATE AN001",
            normalizedType: "ANODIZED", color: "WHITE", texture: "MATTE",
            commercialCode: "AN001", material: "ALUMINUM"),
        Finish("FINISH_INOX", "INOX",
            normalizedType: "STAINLESS_STEEL",
            material: "STAINLESS_STEEL"),
        Finish("FINISH_NA", "N.A",
            normalizedType: "NOT_APPLICABLE", requiresReview: true),
        Finish("UNKNOWN", "Acabado por confirmar", isSelectable: false,
            requiresReview: true)
    ];

    private static FinishCandidateResolutionInput Input(
        string? raw = null,
        string? normalizedType = null,
        string? color = null,
        string? texture = null,
        string? explicitCode = null) =>
        new(raw, normalizedType, color, color, texture, texture, explicitCode, null);

    private static FinishTypeCatalogReadModel Finish(
        string code,
        string name,
        string? normalizedType = null,
        string? color = null,
        string? texture = null,
        string? process = null,
        string? commercialCode = null,
        string? material = null,
        bool isSelectable = true,
        bool requiresReview = false) =>
        new(
            Guid.NewGuid(),
            code,
            name,
            normalizedType,
            color,
            texture,
            process,
            commercialCode,
            material,
            isSelectable,
            requiresReview,
            true);

    private static string? Text(JsonElement value, string property) =>
        value.TryGetProperty(property, out var propertyValue)
            && propertyValue.ValueKind != JsonValueKind.Null
            ? Text(propertyValue)
            : null;

    private static string? Text(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in new[] { "normalized", "raw", "value" })
        {
            if (value.TryGetProperty(property, out var propertyValue)
                && propertyValue.ValueKind == JsonValueKind.String)
            {
                return propertyValue.GetString();
            }
        }

        return null;
    }

    private static string RepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(relativePath);
    }
}
