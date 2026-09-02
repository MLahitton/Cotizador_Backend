using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.CreatePreQuote;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class CreatePreQuoteServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("invalid", CreatePreQuoteFailure.InvalidRequest)]
    [InlineData("unauthorized", CreatePreQuoteFailure.Unauthorized)]
    [InlineData("inactive_user", CreatePreQuoteFailure.InactiveUser)]
    [InlineData("project_not_found", CreatePreQuoteFailure.ProjectNotFound)]
    [InlineData("foreign_project", CreatePreQuoteFailure.ProjectNotFound)]
    [InlineData("inactive_project", CreatePreQuoteFailure.InactiveProject)]
    [InlineData("client_not_found", CreatePreQuoteFailure.ClientNotFound)]
    [InlineData("inactive_client", CreatePreQuoteFailure.InactiveClient)]
    [InlineData("query", CreatePreQuoteFailure.QueryError)]
    [InlineData("persistence", CreatePreQuoteFailure.PersistenceError)]
    public async Task Execute_Failure_ReturnsExpectedFailure(
        string scenario,
        CreatePreQuoteFailure expected)
    {
        var context = CreateContext(scenario);
        var result = await context.Service.ExecuteAsync(
            new CreatePreQuoteCommand(
                scenario == "invalid" ? Guid.Empty : context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Failure);
        Assert.Null(result.PreQuote);
        if (scenario is "project_not_found" or "foreign_project"
            or "inactive_project")
        {
            await context.ClientRepository.DidNotReceive()
                .FindByIdAsync(
                    Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }
        if (scenario != "persistence")
        {
            await context.PreQuoteRepository.DidNotReceive()
                .SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Execute_Success_PersistsOnceAndReturnsCreatedData()
    {
        var context = CreateContext("success");
        PreQuote? captured = null;
        context.PreQuoteRepository.When(repository => repository.Add(
                Arg.Any<PreQuote>()))
            .Do(call => captured = call.Arg<PreQuote>());

        var result = await context.Service.ExecuteAsync(
            new CreatePreQuoteCommand(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.PreQuote);
        Assert.Equal(context.Project.Id, result.PreQuote.ProjectId);
        Assert.Equal("PC-2026-0001", result.PreQuote.Serial);
        Assert.Null(result.PreQuote.Name);
        Assert.NotNull(captured);
        Assert.Equal(result.PreQuote.Id, captured.Id);
        Assert.Equal("PC-2026-0001", captured.Serial);
        await context.PreQuoteRepository.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    private static Context CreateContext(string scenario)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var clientRepository = Substitute.For<IClientRepository>();
        var preQuoteRepository = Substitute.For<IPreQuoteRepository>();
        var user = User.CreateFromGoogle(
            "user@example.com", "User", null, null, At);
        var client = Client.Create(
            ClientType.Company, "Client", null, null, null, null, null,
            null, null, UserId, At);
        var ownerId = scenario == "foreign_project"
            ? Guid.NewGuid()
            : UserId;
        var project = ProjectEntity.Create(
            client.Id, "PR-001", "Project", null, null, ownerId, At);
        currentUser.IsAuthenticated.Returns(scenario != "unauthorized");
        currentUser.UserId.Returns(UserId);
        if (scenario == "inactive_user")
        {
            user.Deactivate(At.AddMinutes(1));
        }
        if (scenario == "inactive_project")
        {
            project.SetActive(false, ownerId, At.AddMinutes(1));
        }
        if (scenario == "inactive_client")
        {
            client.SetActive(false, UserId, At.AddMinutes(1));
        }
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        projectRepository.FindByIdAsync(
                project.Id, Arg.Any<CancellationToken>())
            .Returns(scenario == "project_not_found" ? null : project);
        clientRepository.FindByIdAsync(
                client.Id, Arg.Any<CancellationToken>())
            .Returns(scenario == "client_not_found" ? null : client);
        if (scenario == "query")
        {
            projectRepository.FindByIdAsync(
                    project.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromException<ProjectEntity?>(
                    new ProjectQueryException(
                        new InvalidOperationException("sensitive"))));
        }
        preQuoteRepository.ReserveNextSerialAsync(
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns("PC-2026-0001");
        preQuoteRepository.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(scenario == "persistence"
                ? Task.FromException(new PreQuotePersistenceException(
                    new InvalidOperationException("sensitive")))
                : Task.CompletedTask);
        var service = new CreatePreQuoteService(
            new CreatePreQuoteCommandValidator(), currentUser, identity,
            projectRepository, clientRepository, preQuoteRepository,
            Substitute.For<ILogger<CreatePreQuoteService>>());
        return new Context(
            service, project, clientRepository, preQuoteRepository);
    }

    private sealed record Context(
        CreatePreQuoteService Service,
        ProjectEntity Project,
        IClientRepository ClientRepository,
        IPreQuoteRepository PreQuoteRepository);
}
