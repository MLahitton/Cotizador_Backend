using Application.Catalogs.GetGlassTypesCatalog;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Domain.Catalogs;
using Domain.Identity;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.Catalogs;

public sealed class GetGlassTypesCatalogServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_ReturnsOnlyActiveOpenRangesInCodeOrder()
    {
        var context = CreateContext();
        context.Repository.GetActiveWithCurrentPriceRangesAsync(
                Arg.Any<CancellationToken>())
            .Returns(
            [
                Item("LAM_5_5_GRAY", 125000m, 145000m),
                Item("INACTIVE", 1m, 2m, isActive: false),
                Item("LAM_4_4_GRAY", 95000m, 95000m),
                Item("CLOSED", 1m, 2m, validToUtc: At.AddDays(1)),
                Item("LAM_5_5", 120000m, 140000m),
                Item("LAM_4_4", 90000m, 110000m)
            ]);

        var result = await context.Service.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Items.Count);
        Assert.Equal(
            ["LAM_4_4", "LAM_4_4_GRAY", "LAM_5_5", "LAM_5_5_GRAY"],
            result.Items.Select(value => value.Code));
        AssertRange(result, "LAM_4_4", 90000m, 110000m);
        AssertRange(result, "LAM_4_4_GRAY", 95000m, 95000m);
        AssertRange(result, "LAM_5_5", 120000m, 140000m);
        AssertRange(result, "LAM_5_5_GRAY", 125000m, 145000m);
        Assert.All(result.Items, value =>
        {
            Assert.Equal("COP", value.CurrentPriceRange!.Currency);
            Assert.Equal(
                GlassPriceRangeStatus.Preliminary,
                value.CurrentPriceRange.Status);
            Assert.Null(value.CurrentPriceRange.ValidToUtc);
        });
        await context.Repository.Received(1)
            .GetActiveWithCurrentPriceRangesAsync(
                Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("unauthorized", GetGlassTypesCatalogFailure.Unauthorized)]
    [InlineData("inactive", GetGlassTypesCatalogFailure.InactiveUser)]
    [InlineData("query", GetGlassTypesCatalogFailure.QueryError)]
    public async Task Execute_MapsControlledFailures(
        string scenario,
        GetGlassTypesCatalogFailure expected)
    {
        var context = CreateContext();
        if (scenario == "unauthorized")
        {
            context.CurrentUser.IsAuthenticated.Returns(false);
        }
        else if (scenario == "inactive")
        {
            context.User.Deactivate(At.AddMinutes(1));
        }
        else
        {
            context.Repository.GetActiveWithCurrentPriceRangesAsync(
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException<
                    IReadOnlyList<GlassTypeCatalogReadModel>>(
                    new GlassTypeCatalogQueryException(
                        new InvalidOperationException())));
        }

        var result = await context.Service.ExecuteAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Failure);
    }

    private static void AssertRange(
        GetGlassTypesCatalogResult result,
        string code,
        decimal minimum,
        decimal maximum)
    {
        var item = result.Items.Single(value => value.Code == code);
        Assert.Equal(minimum, item.CurrentPriceRange!.MinimumPricePerSquareMeter);
        Assert.Equal(maximum, item.CurrentPriceRange.MaximumPricePerSquareMeter);
    }

    private static Context CreateContext()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var repository = Substitute.For<IGlassTypeCatalogRepository>();
        var user = User.CreateFromGoogle(
            "user@example.com", "Test", "User", null, At);
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        repository.GetActiveWithCurrentPriceRangesAsync(
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GlassTypeCatalogReadModel>());
        return new(
            currentUser,
            repository,
            user,
            new GetGlassTypesCatalogService(
                currentUser,
                identity,
                repository));
    }

    internal static GlassTypeCatalogReadModel Item(
        string code,
        decimal minimum,
        decimal maximum,
        bool isActive = true,
        DateTimeOffset? validToUtc = null) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            null,
            isActive,
            new GlassPriceRangeCatalogReadModel(
                Guid.NewGuid(),
                1,
                minimum,
                maximum,
                "COP",
                GlassPriceRangeStatus.Preliminary,
                At,
                validToUtc));

    private sealed record Context(
        ICurrentUser CurrentUser,
        IGlassTypeCatalogRepository Repository,
        User User,
        GetGlassTypesCatalogService Service);
}
