using Application.Common.Abstractions.Projects;
using Application.Projects.GetProjects;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Clients;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.Projects;

public sealed class GetProjectsServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WithCombinedFilters_MapsCriteriaAndClientSummary()
    {
        var context = new AdministrationTestContext();
        ProjectSearchCriteria? captured = null;
        var item = CreateItem(context);
        context.ProjectRepository.SearchAsync(
                Arg.Do<ProjectSearchCriteria>(value => captured = value),
                Arg.Any<CancellationToken>())
            .Returns(new AdministrativeProjectSearchPage([item], 25));

        var result = await context.GetProjectsService.ExecuteAsync(
            new GetProjectsQuery(
                "  cliente  ",
                " active ",
                context.Client.Id,
                " company ",
                " nit ",
                2,
                10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("cliente", captured.Search);
        Assert.True(captured.IsActive);
        Assert.Equal(context.Client.Id, captured.ClientId);
        Assert.Equal(ClientType.Company, captured.ClientType);
        Assert.Equal(ClientDocumentType.Nit, captured.DocumentType);
        Assert.Equal(2, captured.Page);
        Assert.Equal(10, captured.PageSize);
        var project = Assert.Single(result.Page!.Items);
        Assert.Equal(context.Project.Id, project.Id);
        Assert.Equal("Servicios Nacionales", project.Client.LegalName);
        Assert.Equal("SnG", project.Client.TradeName);
        Assert.Equal(ClientDocumentType.Nit, project.Client.DocumentType);
        Assert.Equal("900.123.456-7", project.Client.DocumentNumber);
        Assert.Equal(25, result.Page.TotalCount);
        Assert.Equal(3, result.Page.TotalPages);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyPage_PreservesTotals()
    {
        var context = new AdministrationTestContext();
        context.ProjectRepository.SearchAsync(
                Arg.Any<ProjectSearchCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdministrativeProjectSearchPage([], 22));

        var result = await context.GetProjectsService.ExecuteAsync(
            new GetProjectsQuery(null, "all", null, null, null, 4, 10),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Page!.Items);
        Assert.Equal(22, result.Page.TotalCount);
        Assert.Equal(3, result.Page.TotalPages);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnauthenticatedUser_ReturnsUnauthorized()
    {
        var context = new AdministrationTestContext();
        context.CurrentUser.IsAuthenticated.Returns(false);

        var result = await ExecuteDefault(context);

        Assert.Equal(GetProjectsFailure.Unauthorized, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithInactiveUser_ReturnsInactiveUser()
    {
        var context = new AdministrationTestContext();
        context.SetInactiveUser();

        var result = await ExecuteDefault(context);

        Assert.Equal(GetProjectsFailure.InactiveUser, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithQueryException_ReturnsQueryError()
    {
        var context = new AdministrationTestContext();
        context.ProjectRepository.SearchAsync(
                Arg.Any<ProjectSearchCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AdministrativeProjectSearchPage>(
                new ProjectQueryException(new InvalidOperationException())));

        var result = await ExecuteDefault(context);

        Assert.Equal(GetProjectsFailure.QueryError, result.Failure);
    }

    private static Task<GetProjectsResult> ExecuteDefault(
        AdministrationTestContext context)
    {
        return context.GetProjectsService.ExecuteAsync(
            new GetProjectsQuery(null, null, null, null, null, 1, 20),
            TestContext.Current.CancellationToken);
    }

    private static AdministrativeProjectSearchItem CreateItem(
        AdministrationTestContext context)
    {
        return new AdministrativeProjectSearchItem(
            context.Project.Id,
            context.Client.Id,
            context.Project.Code,
            context.Project.Name,
            context.Project.Description,
            context.Project.Location,
            context.Project.IsActive,
            context.Project.CreatedAtUtc,
            context.Project.UpdatedAtUtc,
            context.Client.ClientType,
            context.Client.LegalName,
            context.Client.TradeName,
            context.Client.DocumentType,
            context.Client.DocumentNumber);
    }
}
