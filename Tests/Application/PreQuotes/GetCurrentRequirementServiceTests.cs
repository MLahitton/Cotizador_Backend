using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.GetCurrentRequirement;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class GetCurrentRequirementServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithCurrentRequirement_ReturnsIt()
    {
        var context = CreateContext("success");
        var current = new CurrentRequirementReadModel(
            Guid.NewGuid(),
            context.PreQuote.Id,
            RequirementStatus.Processed,
            RequirementCommercialLine.Essential,
            At,
            true,
            Guid.NewGuid(),
            DocumentProcessingState.Finished,
            DocumentProcessingOutcome.Completed,
            null,
            CanEditDocuments: false,
            CanCancel: false,
            CanReplace: true,
            IsCurrent: true,
            SupersedesRequirementId: null,
            SupersededByRequirementId: null,
            Documents: []);
        context.Requirements.GetCurrentByPreQuoteIdAsync(
                context.PreQuote.Id,
                Arg.Any<CancellationToken>())
            .Returns(current);

        var result = await context.Service.ExecuteAsync(
            new GetCurrentRequirementCommand(context.PreQuote.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(current, result.Requirement);
        await context.Requirements.Received(1).GetCurrentByPreQuoteIdAsync(
            context.PreQuote.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithoutRequirement_ReturnsNotFound()
    {
        var context = CreateContext("success");

        var result = await context.Service.ExecuteAsync(
            new GetCurrentRequirementCommand(context.PreQuote.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            GetCurrentRequirementFailure.CurrentRequirementNotFound,
            result.Failure);
    }

    [Theory]
    [InlineData("empty_prequote", GetCurrentRequirementFailure.InvalidRequest)]
    [InlineData("unauthorized", GetCurrentRequirementFailure.Unauthorized)]
    [InlineData("inactive_user", GetCurrentRequirementFailure.InactiveUser)]
    [InlineData("prequote_not_found", GetCurrentRequirementFailure.PreQuoteNotFound)]
    [InlineData("foreign", GetCurrentRequirementFailure.PreQuoteNotFound)]
    [InlineData("inactive_project", GetCurrentRequirementFailure.InactiveProject)]
    [InlineData("inactive_client", GetCurrentRequirementFailure.InactiveClient)]
    public async Task Execute_WithInvalidInput_ReturnsExpectedFailure(
        string scenario,
        GetCurrentRequirementFailure expected)
    {
        var context = CreateContext(scenario);

        var result = await context.Service.ExecuteAsync(
            new GetCurrentRequirementCommand(
                scenario == "empty_prequote"
                    ? Guid.Empty
                    : context.PreQuote.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Failure);
        await context.Requirements.DidNotReceive().GetCurrentByPreQuoteIdAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    private static Context CreateContext(string scenario)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var user = User.CreateFromGoogle(
            "user@example.com", "User", null, null, At);
        var client = Client.Create(
            ClientType.Company, "Client", null, null, null, null, null,
            null, null, UserId, At);
        var owner = scenario == "foreign" ? Guid.NewGuid() : UserId;
        var project = ProjectEntity.Create(
            client.Id, "P-001", "Project", null, null, owner, At);
        var preQuote = PreQuote.Create(project.Id, UserId, At);

        currentUser.IsAuthenticated.Returns(scenario != "unauthorized");
        currentUser.UserId.Returns(UserId);
        if (scenario == "inactive_user")
        {
            user.Deactivate(At.AddMinutes(1));
        }
        if (scenario == "inactive_project")
        {
            project.SetActive(false, owner, At.AddMinutes(1));
        }
        if (scenario == "inactive_client")
        {
            client.SetActive(false, UserId, At.AddMinutes(1));
        }

        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(scenario == "prequote_not_found"
                ? null
                : new PreQuoteDetails(preQuote.Id, project.Id, 0, At, At));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);

        var service = new GetCurrentRequirementService(
            currentUser,
            identity,
            preQuotes,
            projects,
            clients,
            requirements);

        return new Context(service, preQuote, requirements);
    }

    private sealed record Context(
        GetCurrentRequirementService Service,
        PreQuote PreQuote,
        IRequirementRepository Requirements);
}
