using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.GetRequirementDetails;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class GetRequirementDetailsServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithRequirement_ReturnsLifecycleAndDocuments()
    {
        var context = CreateContext("success");

        var result = await context.Service.ExecuteAsync(
            new GetRequirementDetailsCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(context.Requirement.Id, result.Requirement!.RequirementId);
        Assert.True(result.Requirement.IsCurrent);
        Assert.True(result.Requirement.CanEditDocuments);
        Assert.Single(result.Requirement.Documents);
        Assert.Equal(
            context.File.Id,
            result.Requirement.Documents[0].RequirementFileId);
        Assert.Equal("source.pdf", result.Requirement.Documents[0].FileName);
    }

    [Fact]
    public async Task Execute_WithUnknownRequirement_ReturnsNotFound()
    {
        var context = CreateContext("missing_requirement");

        var result = await context.Service.ExecuteAsync(
            new GetRequirementDetailsCommand(Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            GetRequirementDetailsFailure.RequirementNotFound,
            result.Failure);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("superseded")]
    public async Task Execute_WithHistoricalRequirement_ReturnsItAsNotCurrent(
        string scenario)
    {
        var context = CreateContext(scenario);

        var result = await context.Service.ExecuteAsync(
            new GetRequirementDetailsCommand(context.Requirement.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Requirement!.IsCurrent);
        Assert.False(result.Requirement.CanEditDocuments);
        Assert.False(result.Requirement.CanCancel);
        Assert.Equal(context.Requirement.Status, result.Requirement.Status);
        if (scenario == "superseded")
        {
            Assert.NotNull(result.Requirement.SupersededByRequirementId);
        }
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
            "user@example.com",
            "User",
            null,
            null,
            At);
        var client = Client.Create(
            ClientType.Company,
            "Client",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            UserId,
            At);
        var project = ProjectEntity.Create(
            client.Id,
            "P-001",
            "Project",
            null,
            null,
            UserId,
            At);
        var preQuote = PreQuote.Create(project.Id, UserId, At);
        var requirement = Requirement.Create(
            preQuote.Id,
            UserId,
            RequirementCommercialLine.Essential,
            At);
        var file = RequirementFile.Create(
            requirement.Id,
            "source.pdf",
            "application/pdf",
            123,
            "requirements/source/original.pdf",
            At.AddSeconds(1));

        if (scenario == "cancelled")
        {
            requirement.Cancel(At.AddSeconds(2));
        }
        else if (scenario == "superseded")
        {
            requirement.StartProcessing(At.AddSeconds(2));
            requirement.MarkProcessed(At.AddSeconds(3));
            requirement.SupersedeBy(Guid.NewGuid(), At.AddSeconds(4));
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindByIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(scenario == "missing_requirement" ? null : requirement);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDetails(
                preQuote.Id,
                preQuote.ProjectId,
                DocumentCount: 0,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);

        var files = requirement.Files as ICollection<RequirementFile>;
        files?.Add(file);

        var service = new GetRequirementDetailsService(
            currentUser,
            identity,
            preQuotes,
            projects,
            clients,
            requirements);

        return new Context(service, requirement, file);
    }

    private sealed record Context(
        GetRequirementDetailsService Service,
        Requirement Requirement,
        RequirementFile File);
}
