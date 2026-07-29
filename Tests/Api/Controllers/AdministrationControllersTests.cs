using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Contracts.Clients;
using Contracts.Projects;
using CotizadorBackend.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Api.Controllers;

public sealed class AdministrationControllersTests
{
    [Fact]
    public async Task ClientsGet_ReturnsPagedContractAndForwardsFilters()
    {
        var context = new AdministrationTestContext();
        ClientSearchCriteria? captured = null;
        context.ClientRepository.SearchAsync(
                Arg.Do<ClientSearchCriteria>(value => captured = value),
                Arg.Any<CancellationToken>())
            .Returns(new ClientSearchPage([context.Client], 1));

        var action = await context.ClientsController.Get(
            " Bogota ",
            "all",
            "Company",
            "Nit",
            "900.123-456/7",
            1,
            20,
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<GetClientsResponse>(ok.Value);
        var item = Assert.Single(response.Items);
        Assert.Equal("Company", item.ClientType);
        Assert.Equal("Nit", item.DocumentType);
        Assert.Equal("9001234567", captured!.NormalizedDocumentNumber);
        Assert.Equal(1, response.TotalCount);
    }

    [Theory]
    [InlineData("invalid", StatusCodes.Status400BadRequest)]
    [InlineData("unauthorized", StatusCodes.Status401Unauthorized)]
    [InlineData("inactive", StatusCodes.Status403Forbidden)]
    [InlineData("query", StatusCodes.Status500InternalServerError)]
    public async Task ClientsGet_WithFailure_ReturnsSafeProblem(
        string scenario,
        int expectedStatus)
    {
        var context = new AdministrationTestContext();
        var page = 1;
        ConfigureClientFailure(context, scenario, ref page);

        var action = await context.ClientsController.Get(
            null, null, null, null, null, page, 20,
            TestContext.Current.CancellationToken);

        AssertSafeProblem(action.Result, expectedStatus);
    }

    [Fact]
    public async Task ProjectsGet_ReturnsClientSummaryAndPagination()
    {
        var context = new AdministrationTestContext();
        context.ProjectRepository.SearchAsync(
                Arg.Any<ProjectSearchCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(new AdministrativeProjectSearchPage(
                [CreateProjectItem(context)],
                1));

        var action = await context.ProjectsController.Get(
            "cliente", "all", context.Client.Id, "Company", "Nit",
            1, 20, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<GetProjectsResponse>(ok.Value);
        var item = Assert.Single(response.Items);
        Assert.Equal(context.Client.Id, item.Client.Id);
        Assert.Equal("Servicios Nacionales", item.Client.LegalName);
        Assert.Equal("Nit", item.Client.DocumentType);
        Assert.Equal(1, response.TotalCount);
    }

    [Theory]
    [InlineData("invalid", StatusCodes.Status400BadRequest)]
    [InlineData("unauthorized", StatusCodes.Status401Unauthorized)]
    [InlineData("inactive", StatusCodes.Status403Forbidden)]
    [InlineData("query", StatusCodes.Status500InternalServerError)]
    public async Task ProjectsGet_WithFailure_ReturnsSafeProblem(
        string scenario,
        int expectedStatus)
    {
        var context = new AdministrationTestContext();
        var page = 1;
        ConfigureProjectFailure(context, scenario, ref page);

        var action = await context.ProjectsController.Get(
            null, null, null, null, null, page, 20,
            TestContext.Current.CancellationToken);

        AssertSafeProblem(action.Result, expectedStatus);
    }

    [Fact]
    public async Task ProjectActivation_ReturnsUpdatedDetails()
    {
        var context = new AdministrationTestContext();

        var action = await context.ProjectsController.SetActivation(
            context.Project.Id,
            new SetProjectActivationRequest(false),
            TestContext.Current.CancellationToken);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ProjectDetailsResponse>(ok.Value);
        Assert.Equal(context.Project.Id, response.Id);
        Assert.False(response.IsActive);
    }

    [Theory]
    [InlineData("invalid", StatusCodes.Status400BadRequest)]
    [InlineData("unauthorized", StatusCodes.Status401Unauthorized)]
    [InlineData("inactive", StatusCodes.Status403Forbidden)]
    [InlineData("not_found", StatusCodes.Status404NotFound)]
    [InlineData("query", StatusCodes.Status500InternalServerError)]
    [InlineData("persistence", StatusCodes.Status500InternalServerError)]
    public async Task ProjectActivation_WithFailure_ReturnsSafeProblem(
        string scenario,
        int expectedStatus)
    {
        var context = new AdministrationTestContext();
        var projectId = context.Project.Id;
        ConfigureActivationFailure(context, scenario, ref projectId);

        var action = await context.ProjectsController.SetActivation(
            projectId,
            new SetProjectActivationRequest(false),
            TestContext.Current.CancellationToken);

        AssertSafeProblem(action.Result, expectedStatus);
    }

    private static void ConfigureClientFailure(
        AdministrationTestContext context,
        string scenario,
        ref int page)
    {
        if (scenario == "invalid") page = 0;
        else if (scenario == "unauthorized")
            context.CurrentUser.IsAuthenticated.Returns(false);
        else if (scenario == "inactive") context.SetInactiveUser();
        else
            context.ClientRepository.SearchAsync(
                    Arg.Any<ClientSearchCriteria>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException<ClientSearchPage>(
                    new ClientQueryException(
                        new InvalidOperationException())));
    }

    private static void ConfigureProjectFailure(
        AdministrationTestContext context,
        string scenario,
        ref int page)
    {
        if (scenario == "invalid") page = 0;
        else if (scenario == "unauthorized")
            context.CurrentUser.IsAuthenticated.Returns(false);
        else if (scenario == "inactive") context.SetInactiveUser();
        else
            context.ProjectRepository.SearchAsync(
                    Arg.Any<ProjectSearchCriteria>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException<AdministrativeProjectSearchPage>(
                    new ProjectQueryException(
                        new InvalidOperationException())));
    }

    private static void ConfigureActivationFailure(
        AdministrationTestContext context,
        string scenario,
        ref Guid projectId)
    {
        if (scenario == "invalid") projectId = Guid.Empty;
        else if (scenario == "unauthorized")
            context.CurrentUser.IsAuthenticated.Returns(false);
        else if (scenario == "inactive") context.SetInactiveUser();
        else if (scenario == "not_found")
            context.ProjectRepository.FindForUpdateByIdAsync(
                    projectId,
                    Arg.Any<CancellationToken>())
                .Returns((global::Domain.Projects.Project?)null);
        else if (scenario == "query")
            context.ProjectRepository.FindForUpdateByIdAsync(
                    projectId,
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException<global::Domain.Projects.Project?>(
                    new ProjectQueryException(
                        new InvalidOperationException())));
        else
            context.ProjectRepository.SaveChangesAsync(
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromException(
                    new ProjectPersistenceException(
                        new InvalidOperationException())));
    }

    private static AdministrativeProjectSearchItem CreateProjectItem(
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

    private static void AssertSafeProblem(
        ActionResult? action,
        int expectedStatus)
    {
        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal(expectedStatus, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        var json = System.Text.Json.JsonSerializer.Serialize(problem);
        Assert.DoesNotContain("sql", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "constraint",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "stackTrace",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "exception",
            json,
            StringComparison.OrdinalIgnoreCase);
    }
}
