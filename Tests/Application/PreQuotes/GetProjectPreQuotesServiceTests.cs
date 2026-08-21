using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.GetProjectPreQuotes;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using NSubstitute;
using Xunit;
using ProjectEntity = global::Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class GetProjectPreQuotesServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithNoPreQuotes_ReturnsEmptyPage()
    {
        var context = CreateContext();
        context.PreQuoteRepository.SearchByProjectAsync(
                context.Project.Id,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(new PreQuoteSearchPage(
                Array.Empty<PreQuoteSearchItem>(),
                0));

        var result = await context.Service.ExecuteAsync(
            new GetProjectPreQuotesQuery(context.Project.Id, 1, 20),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Page);
        Assert.Empty(result.Page.Items);
        Assert.Equal(0, result.Page.TotalCount);
        Assert.Equal(0, result.Page.TotalPages);
    }

    [Fact]
    public async Task Execute_WithManyPreQuotes_PreservesHistoryMetadata()
    {
        var context = CreateContext();
        var firstPreQuoteId = Guid.Parse(
            "22222222-2222-2222-2222-222222222222");
        var secondPreQuoteId = Guid.Parse(
            "33333333-3333-3333-3333-333333333333");
        var requirementId = Guid.Parse(
            "44444444-4444-4444-4444-444444444444");
        var proposalId = Guid.Parse(
            "55555555-5555-5555-5555-555555555555");

        context.PreQuoteRepository.SearchByProjectAsync(
                context.Project.Id,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(new PreQuoteSearchPage(
                [
                    new PreQuoteSearchItem(
                        firstPreQuoteId,
                        context.Project.Id,
                        2,
                        At.AddHours(-2),
                        At.AddHours(-1),
                        true,
                        requirementId,
                        RequirementStatus.Processed,
                        true,
                        proposalId,
                        15,
                        DocumentProcessingState.Finished,
                        DocumentProcessingOutcome.Completed,
                        null),
                    new PreQuoteSearchItem(
                        secondPreQuoteId,
                        context.Project.Id,
                        0,
                        At.AddHours(-4),
                        At.AddHours(-3),
                        false,
                        null,
                        null,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null)
                ],
                2));

        var result = await context.Service.ExecuteAsync(
            new GetProjectPreQuotesQuery(context.Project.Id, 1, 20),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Page);
        Assert.Equal(2, result.Page.Items.Count);
        Assert.Equal(firstPreQuoteId, result.Page.Items[0].Id);
        Assert.True(result.Page.Items[0].HasRequirement);
        Assert.Equal(requirementId, result.Page.Items[0].LatestRequirementId);
        Assert.Equal("Processed", result.Page.Items[0].LatestRequirementStatus);
        Assert.True(result.Page.Items[0].HasTechnicalProposal);
        Assert.Equal(proposalId, result.Page.Items[0].TechnicalProposalId);
        Assert.Equal(15, result.Page.Items[0].TechnicalProposalItemCount);
        Assert.Equal("Finished", result.Page.Items[0].LatestAttemptState);
        Assert.Equal("Completed", result.Page.Items[0].LatestAttemptOutcome);
        Assert.False(result.Page.Items[1].HasRequirement);
        Assert.False(result.Page.Items[1].HasTechnicalProposal);
    }

    [Fact]
    public async Task Execute_CreatingNewPreQuoteIsNotPartOfHistoryQuery()
    {
        var context = CreateContext();

        context.PreQuoteRepository.SearchByProjectAsync(
                context.Project.Id,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(new PreQuoteSearchPage(
                Array.Empty<PreQuoteSearchItem>(),
                0));

        await context.Service.ExecuteAsync(
            new GetProjectPreQuotesQuery(context.Project.Id, 1, 20),
            TestContext.Current.CancellationToken);

        context.PreQuoteRepository.DidNotReceive()
            .Add(Arg.Any<PreQuote>());
        context.PreQuoteRepository.DidNotReceive()
            .AddDocument(Arg.Any<PreQuoteDocument>());
        await context.PreQuoteRepository.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Context CreateContext()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identityRepository = Substitute.For<IIdentityRepository>();
        var projectRepository = Substitute.For<IProjectRepository>();
        var preQuoteRepository = Substitute.For<IPreQuoteRepository>();
        var user = User.CreateFromGoogle(
            "user@example.com",
            "User",
            null,
            null,
            At);
        var project = ProjectEntity.Create(
            Guid.NewGuid(),
            "PR-001",
            "Project",
            null,
            null,
            UserId,
            At);

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identityRepository.FindUserByIdAsync(
                UserId,
                Arg.Any<CancellationToken>())
            .Returns(user);
        projectRepository.FindByIdAsync(
                project.Id,
                Arg.Any<CancellationToken>())
            .Returns(project);

        var service = new GetProjectPreQuotesService(
            new GetProjectPreQuotesQueryValidator(),
            currentUser,
            identityRepository,
            projectRepository,
            preQuoteRepository);

        return new Context(service, project, preQuoteRepository);
    }

    private sealed record Context(
        GetProjectPreQuotesService Service,
        ProjectEntity Project,
        IPreQuoteRepository PreQuoteRepository);
}
