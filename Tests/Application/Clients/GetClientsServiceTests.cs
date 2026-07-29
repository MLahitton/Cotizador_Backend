using Application.Clients.GetClients;
using Application.Common.Abstractions.Clients;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Clients;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.Clients;

public sealed class GetClientsServiceTests
{
    [Fact]
    public async Task ExecuteAsync_WithCombinedFilters_NormalizesCriteriaAndMapsPage()
    {
        var context = new AdministrationTestContext();
        ClientSearchCriteria? captured = null;
        context.ClientRepository.SearchAsync(
                Arg.Do<ClientSearchCriteria>(value => captured = value),
                Arg.Any<CancellationToken>())
            .Returns(new ClientSearchPage([context.Client], 21));

        var result = await context.GetClientsService.ExecuteAsync(
            new GetClientsQuery(
                "  Bogota  ",
                " inactive ",
                " company ",
                " nit ",
                " 900.123-456/7 ",
                2,
                10),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("Bogota", captured.Search);
        Assert.False(captured.IsActive);
        Assert.Equal(ClientType.Company, captured.ClientType);
        Assert.Equal(ClientDocumentType.Nit, captured.DocumentType);
        Assert.Equal("9001234567", captured.NormalizedDocumentNumber);
        Assert.Equal(2, captured.Page);
        Assert.Equal(10, captured.PageSize);
        Assert.Single(result.Page!.Items);
        Assert.Equal(21, result.Page.TotalCount);
        Assert.Equal(3, result.Page.TotalPages);
        Assert.Equal(context.Client.Id, result.Page.Items[0].Id);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyPage_PreservesTotals()
    {
        var context = new AdministrationTestContext();
        context.ClientRepository.SearchAsync(
                Arg.Any<ClientSearchCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(new ClientSearchPage([], 41));

        var result = await context.GetClientsService.ExecuteAsync(
            new GetClientsQuery(null, "all", null, null, null, 5, 10),
            TestContext.Current.CancellationToken);

        Assert.Empty(result.Page!.Items);
        Assert.Equal(41, result.Page.TotalCount);
        Assert.Equal(5, result.Page.TotalPages);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnauthenticatedUser_ReturnsUnauthorized()
    {
        var context = new AdministrationTestContext();
        context.CurrentUser.IsAuthenticated.Returns(false);

        var result = await ExecuteDefault(context);

        Assert.Equal(GetClientsFailure.Unauthorized, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithInactiveUser_ReturnsInactiveUser()
    {
        var context = new AdministrationTestContext();
        context.SetInactiveUser();

        var result = await ExecuteDefault(context);

        Assert.Equal(GetClientsFailure.InactiveUser, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_WithQueryException_ReturnsQueryError()
    {
        var context = new AdministrationTestContext();
        context.ClientRepository.SearchAsync(
                Arg.Any<ClientSearchCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ClientSearchPage>(
                new ClientQueryException(new InvalidOperationException())));

        var result = await ExecuteDefault(context);

        Assert.Equal(GetClientsFailure.QueryError, result.Failure);
    }

    private static Task<GetClientsResult> ExecuteDefault(
        AdministrationTestContext context)
    {
        return context.GetClientsService.ExecuteAsync(
            new GetClientsQuery(null, null, null, null, null, 1, 20),
            TestContext.Current.CancellationToken);
    }
}
