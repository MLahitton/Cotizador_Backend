using Application.Common.Abstractions.Projects;
using Application.Projects.SetProjectActivation;
using CotizadorBackend.Tests.TestDoubles;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.Projects;

public sealed class SetProjectActivationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_DeactivatesProjectAndPersistsOnce()
    {
        var context = new AdministrationTestContext();
        var before = DateTimeOffset.UtcNow;

        var result = await Execute(context, false);

        Assert.True(result.IsSuccess);
        Assert.False(context.Project.IsActive);
        Assert.Equal(context.User.Id, context.Project.UpdatedByUserId);
        Assert.NotNull(context.Project.StatusChangedAtUtc);
        Assert.InRange(context.Project.UpdatedAtUtc, before, DateTimeOffset.UtcNow);
        await context.ProjectRepository.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ActivatesInactiveProjectAndPersistsOnce()
    {
        var context = CreateInactiveContext();

        var result = await Execute(context, true);

        Assert.True(result.Project!.IsActive);
        await context.ProjectRepository.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ActiveToActive_IsIdempotent()
    {
        var context = new AdministrationTestContext();
        var updatedAt = context.Project.UpdatedAtUtc;
        var statusChangedAt = context.Project.StatusChangedAtUtc;

        var result = await Execute(context, true);

        Assert.True(result.IsSuccess);
        Assert.Equal(updatedAt, context.Project.UpdatedAtUtc);
        Assert.Equal(statusChangedAt, context.Project.StatusChangedAtUtc);
        await context.ProjectRepository.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_InactiveToInactive_IsIdempotent()
    {
        var context = CreateInactiveContext();
        var updatedAt = context.Project.UpdatedAtUtc;
        var statusChangedAt = context.Project.StatusChangedAtUtc;

        var result = await Execute(context, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(updatedAt, context.Project.UpdatedAtUtc);
        Assert.Equal(statusChangedAt, context.Project.StatusChangedAtUtc);
        await context.ProjectRepository.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingProject_ReturnsNotFound()
    {
        var context = new AdministrationTestContext();
        context.ProjectRepository.FindForUpdateByIdAsync(
                context.Project.Id,
                Arg.Any<CancellationToken>())
            .Returns((global::Domain.Projects.Project?)null);

        var result = await Execute(context, false);

        Assert.Equal(SetProjectActivationFailure.NotFound, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnauthenticatedUser_ReturnsUnauthorized()
    {
        var context = new AdministrationTestContext();
        context.CurrentUser.IsAuthenticated.Returns(false);

        var result = await Execute(context, false);

        Assert.Equal(SetProjectActivationFailure.Unauthorized, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithInactiveUser_ReturnsInactiveUser()
    {
        var context = new AdministrationTestContext();
        context.SetInactiveUser();

        var result = await Execute(context, false);

        Assert.Equal(SetProjectActivationFailure.InactiveUser, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithQueryException_ReturnsQueryError()
    {
        var context = new AdministrationTestContext();
        context.ProjectRepository.FindForUpdateByIdAsync(
                context.Project.Id,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<global::Domain.Projects.Project?>(
                new ProjectQueryException(new InvalidOperationException())));

        var result = await Execute(context, false);

        Assert.Equal(SetProjectActivationFailure.QueryError, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithPersistenceException_ReturnsPersistenceError()
    {
        var context = new AdministrationTestContext();
        context.ProjectRepository.SaveChangesAsync(
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(
                new ProjectPersistenceException(
                    new InvalidOperationException())));

        var result = await Execute(context, false);

        Assert.Equal(
            SetProjectActivationFailure.PersistenceError,
            result.Failure);
    }

    private static AdministrationTestContext CreateInactiveContext()
    {
        var context = new AdministrationTestContext();
        context.Project.SetActive(
            false,
            context.User.Id,
            AdministrationTestContext.CreatedAt.AddMinutes(1));
        return context;
    }

    private static Task<SetProjectActivationResult> Execute(
        AdministrationTestContext context,
        bool isActive)
    {
        return context.SetProjectActivationService.ExecuteAsync(
            new SetProjectActivationCommand(
                context.Project.Id,
                isActive),
            TestContext.Current.CancellationToken);
    }
}
