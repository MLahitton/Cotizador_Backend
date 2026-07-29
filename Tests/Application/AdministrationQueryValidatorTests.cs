using Application.Clients.GetClients;
using Application.Projects.GetProjects;
using Application.Projects.SetProjectActivation;
using Xunit;

namespace CotizadorBackend.Tests.Application;

public sealed class AdministrationQueryValidatorTests
{
    [Fact]
    public async Task GetClients_AcceptsAllAdministrativeFilters()
    {
        var query = new GetClientsQuery(
            "Bogota",
            "inactive",
            "Company",
            "Nit",
            " 900.123-456/7 ",
            2,
            50);

        var result = await new GetClientsQueryValidator()
            .ValidateAsync(query, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("invalid", null)]
    [InlineData(null, "invalid")]
    public async Task GetClients_RejectsUnknownEnumFilters(
        string? clientType,
        string? documentType)
    {
        var query = new GetClientsQuery(
            null,
            "all",
            clientType,
            documentType,
            null,
            1,
            20);

        var result = await new GetClientsQueryValidator()
            .ValidateAsync(query, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task GetProjects_AcceptsAllAdministrativeFilters()
    {
        var query = new GetProjectsQuery(
            "cliente",
            "all",
            Guid.NewGuid(),
            "Person",
            "CitizenshipCard",
            1,
            100);

        var result = await new GetProjectsQueryValidator()
            .ValidateAsync(query, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("unknown", null, null)]
    [InlineData("all", "unknown", null)]
    [InlineData("all", null, "unknown")]
    public async Task GetProjects_RejectsInvalidFilters(
        string? status,
        string? clientType,
        string? documentType)
    {
        var query = new GetProjectsQuery(
            null,
            status,
            null,
            clientType,
            documentType,
            1,
            20);

        var result = await new GetProjectsQueryValidator()
            .ValidateAsync(query, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000", true)]
    [InlineData("11111111-1111-1111-1111-111111111111", null)]
    public async Task SetProjectActivation_RejectsInvalidCommand(
        string projectId,
        bool? isActive)
    {
        var command = new SetProjectActivationCommand(
            Guid.Parse(projectId),
            isActive);

        var result = await new SetProjectActivationCommandValidator()
            .ValidateAsync(command, TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
    }
}
