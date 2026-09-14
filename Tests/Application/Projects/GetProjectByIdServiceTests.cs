using Application.Projects.GetProjectById;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Identity;
using Domain.Projects;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.Projects;

public sealed class GetProjectByIdServiceTests
{
    [Fact]
    public async Task ExecuteAsync_UserReadingOwnProject_ReturnsSuccess()
    {
        var context = new AdministrationTestContext();

        context.ProjectRepository.FindByIdAsync(
                context.Project.Id,
                Arg.Any<CancellationToken>())
            .Returns(context.Project);

        var service = CreateService(context);

        var result = await service.ExecuteAsync(
            new GetProjectByIdQuery(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(GetProjectByIdFailure.None, result.Failure);
        Assert.NotNull(result.Project);
        Assert.Equal(context.Project.Id, result.Project.Id);
        Assert.Equal(context.Project.Name, result.Project.Name);
    }

    [Fact]
    public async Task ExecuteAsync_UserReadingAnotherUsersProject_ReturnsNotFound()
    {
        var context = new AdministrationTestContext();

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

        context.ProjectRepository.FindByIdAsync(
                otherProject.Id,
                Arg.Any<CancellationToken>())
            .Returns(otherProject);

        var service = CreateService(context);

        var result = await service.ExecuteAsync(
            new GetProjectByIdQuery(otherProject.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            GetProjectByIdFailure.NotFound,
            result.Failure);
        Assert.Null(result.Project);
    }

    [Fact]
    public async Task ExecuteAsync_AdminReadingAnotherUsersProject_ReturnsSuccess()
    {
        var context = new AdministrationTestContext();

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

        context.ProjectRepository.FindByIdAsync(
                otherProject.Id,
                Arg.Any<CancellationToken>())
            .Returns(otherProject);

        var service = CreateService(context);

        var result = await service.ExecuteAsync(
            new GetProjectByIdQuery(otherProject.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(GetProjectByIdFailure.None, result.Failure);
        Assert.NotNull(result.Project);
        Assert.Equal(otherProject.Id, result.Project.Id);
        Assert.Equal(otherProject.Name, result.Project.Name);
    }

    private static GetProjectByIdService CreateService(
        AdministrationTestContext context)
    {
        return new GetProjectByIdService(
            new GetProjectByIdQueryValidator(),
            context.CurrentUser,
            context.IdentityRepository,
            context.ProjectRepository);
    }
}