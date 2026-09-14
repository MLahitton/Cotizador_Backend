using Api.Controllers;
using Application.Administration.GetAdminDashboard;
using Application.Administration.GetAdminPreQuotes;
using Application.Administration.GetAdminUsers;
using Application.Common.Abstractions.Administration;
using Application.Common.Abstractions.Authentication;
using Domain.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class AdminControllerTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAdminIdentity_WithCurrentAdmin_ReturnsDatabaseRole()
    {
        var context = new Context();
        var user = CreateUser(UserRole.Admin);
        context.IdentityRepository.FindUserByIdAsync(
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(user);

        var action = await context.Controller.GetAdminIdentity(
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(action);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal(UserId, GetValue<Guid>(ok.Value!, "userId"));
        Assert.Equal("ADMIN", GetValue<string>(ok.Value!, "role"));
        Assert.True(GetValue<bool>(ok.Value!, "isAdmin"));
    }

    [Fact]
    public async Task GetAdminIdentity_WithInactiveAdmin_ReturnsForbidden()
    {
        var context = new Context();
        var user = CreateUser(UserRole.Admin);
        user.Deactivate(Now.AddMinutes(1));
        context.IdentityRepository.FindUserByIdAsync(
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(user);

        var action = await context.Controller.GetAdminIdentity(
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("Usuario inactivo", problem.Title);
    }

    [Fact]
    public async Task GetAdminIdentity_WithUserRole_ReturnsForbidden()
    {
        var context = new Context();
        context.IdentityRepository.FindUserByIdAsync(
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(CreateUser(UserRole.User));

        var action = await context.Controller.GetAdminIdentity(
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("Acceso denegado", problem.Title);
    }

    [Fact]
    public async Task GetAdminIdentity_WithMissingUser_ReturnsUnauthorized()
    {
        var context = new Context();
        context.IdentityRepository.FindUserByIdAsync(
                UserId,
                Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var action = await context.Controller.GetAdminIdentity(
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("No autorizado", problem.Title);
    }

    [Fact]
    public async Task GetAdminIdentity_WithoutAuthenticatedUser_ReturnsUnauthorized()
    {
        var context = new Context(isAuthenticated: false);

        var action = await context.Controller.GetAdminIdentity(
            TestContext.Current.CancellationToken);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("No autorizado", problem.Title);
    }

    private static User CreateUser(UserRole role)
    {
        var user = User.CreateFromGoogle(
            "admin@example.com",
            "Admin",
            "User",
            null,
            Now);
        user.ChangeRole(role, Now.AddMinutes(1));
        typeof(User).GetProperty(nameof(User.Id))!
            .SetValue(user, UserId);
        return user;
    }

    private static T GetValue<T>(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<T>(property.GetValue(value));
    }

    private sealed class Context
    {
        public Context(bool isAuthenticated = true)
        {
            CurrentUser = Substitute.For<ICurrentUser>();
            CurrentUser.IsAuthenticated.Returns(isAuthenticated);
            CurrentUser.UserId.Returns(isAuthenticated ? UserId : null);

            IdentityRepository = Substitute.For<IIdentityRepository>();

            Controller = new AdminController(
                CurrentUser,
                IdentityRepository,
                new GetAdminDashboardService(
                    CurrentUser,
                    IdentityRepository,
                    Substitute.For<IAdministrationDashboardReader>(),
                    TimeProvider.System),
                new GetAdminUsersService(
                    new GetAdminUsersQueryValidator(),
                    CurrentUser,
                    IdentityRepository,
                    Substitute.For<IAdministrationUserReader>()),
                new GetAdminPreQuotesService(
                    new GetAdminPreQuotesQueryValidator(),
                    CurrentUser,
                    IdentityRepository,
                    Substitute.For<IAdministrationPreQuoteReader>()));
        }

        public ICurrentUser CurrentUser { get; }
        public IIdentityRepository IdentityRepository { get; }
        public AdminController Controller { get; }
    }
}