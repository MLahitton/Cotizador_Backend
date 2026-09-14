using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetPreQuoteById;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Identity;
using Domain.Projects;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class GetPreQuoteByIdServiceTests
{
    [Fact]
    public async Task ExecuteAsync_UserReadingOwnPreQuote_ReturnsSuccess()
    {
        var context = new AdministrationTestContext();
        var preQuoteRepository = Substitute.For<IPreQuoteRepository>();

        var preQuoteId = Guid.NewGuid();

        var preQuote = new PreQuoteDetails(
            preQuoteId,
            context.Project.Id,
            "PC-2026-0001",
            "Precotizacion propia",
            2,
            AdministrationTestContext.CreatedAt,
            AdministrationTestContext.CreatedAt);

        preQuoteRepository.FindByIdAsync(
                preQuoteId,
                Arg.Any<CancellationToken>())
            .Returns(preQuote);

        context.ProjectRepository.FindByIdAsync(
                context.Project.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Project);

        var service = CreateService(
            context,
            preQuoteRepository);

        var result = await service.ExecuteAsync(
            new GetPreQuoteByIdQuery(preQuoteId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            GetPreQuoteByIdFailure.None,
            result.Failure);

        Assert.NotNull(result.PreQuote);
        Assert.Equal(
            preQuoteId,
            result.PreQuote.Id);

        Assert.Equal(
            context.Project.Id,
            result.PreQuote.ProjectId);

        Assert.Equal(
            "PC-2026-0001",
            result.PreQuote.Serial);

        Assert.Equal(
            "Precotizacion propia",
            result.PreQuote.Name);
    }

    [Fact]
    public async Task ExecuteAsync_UserReadingAnotherUsersPreQuote_ReturnsNotFound()
    {
        var context = new AdministrationTestContext();
        var preQuoteRepository = Substitute.For<IPreQuoteRepository>();

        var otherUser = User.CreateFromGoogle(
            "other-user@example.com",
            "Other",
            "User",
            null,
            AdministrationTestContext.CreatedAt);

        var otherProject = Project.Create(
            context.Client.Id,
            "PR-OTHER",
            "Proyecto Ajeno",
            "Proyecto perteneciente a otro usuario",
            "Bogota",
            otherUser.Id,
            AdministrationTestContext.CreatedAt);

        var preQuoteId = Guid.NewGuid();

        var preQuote = new PreQuoteDetails(
            preQuoteId,
            otherProject.Id,
            "PC-2026-0002",
            "Precotizacion ajena",
            1,
            AdministrationTestContext.CreatedAt,
            AdministrationTestContext.CreatedAt);

        preQuoteRepository.FindByIdAsync(
                preQuoteId,
                Arg.Any<CancellationToken>())
            .Returns(preQuote);

        context.ProjectRepository.FindByIdAsync(
                otherProject.Id,
                Arg.Any<CancellationToken>())
            .Returns(otherProject);

        var service = CreateService(
            context,
            preQuoteRepository);

        var result = await service.ExecuteAsync(
            new GetPreQuoteByIdQuery(preQuoteId),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            GetPreQuoteByIdFailure.NotFound,
            result.Failure);

        Assert.Null(result.PreQuote);
    }

    [Fact]
    public async Task ExecuteAsync_AdminReadingAnotherUsersPreQuote_ReturnsSuccess()
    {
        var context = new AdministrationTestContext();
        var preQuoteRepository = Substitute.For<IPreQuoteRepository>();

        context.User.ChangeRole(
            UserRole.Admin,
            AdministrationTestContext.CreatedAt.AddMinutes(1));

        var otherUser = User.CreateFromGoogle(
            "other-user@example.com",
            "Other",
            "User",
            null,
            AdministrationTestContext.CreatedAt);

        var otherProject = Project.Create(
            context.Client.Id,
            "PR-OTHER",
            "Proyecto Ajeno",
            "Proyecto perteneciente a otro usuario",
            "Bogota",
            otherUser.Id,
            AdministrationTestContext.CreatedAt);

        var preQuoteId = Guid.NewGuid();

        var preQuote = new PreQuoteDetails(
            preQuoteId,
            otherProject.Id,
            "PC-2026-0003",
            "Precotizacion visible para admin",
            3,
            AdministrationTestContext.CreatedAt,
            AdministrationTestContext.CreatedAt);

        preQuoteRepository.FindByIdAsync(
                preQuoteId,
                Arg.Any<CancellationToken>())
            .Returns(preQuote);

        context.ProjectRepository.FindByIdAsync(
                otherProject.Id,
                Arg.Any<CancellationToken>())
            .Returns(otherProject);

        var service = CreateService(
            context,
            preQuoteRepository);

        var result = await service.ExecuteAsync(
            new GetPreQuoteByIdQuery(preQuoteId),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            GetPreQuoteByIdFailure.None,
            result.Failure);

        Assert.NotNull(result.PreQuote);
        Assert.Equal(
            preQuoteId,
            result.PreQuote.Id);

        Assert.Equal(
            otherProject.Id,
            result.PreQuote.ProjectId);

        Assert.Equal(
            "PC-2026-0003",
            result.PreQuote.Serial);
    }

    private static GetPreQuoteByIdService CreateService(
        AdministrationTestContext context,
        IPreQuoteRepository preQuoteRepository)
    {
        return new GetPreQuoteByIdService(
            new GetPreQuoteByIdQueryValidator(),
            context.CurrentUser,
            context.IdentityRepository,
            context.ProjectRepository,
            preQuoteRepository);
    }
}