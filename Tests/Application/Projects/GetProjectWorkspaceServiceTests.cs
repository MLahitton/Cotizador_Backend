using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.Projects.GetProjectWorkspace;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.Projects;

public sealed class GetProjectWorkspaceServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_ProjectWithoutPreQuotes_ReturnsEmptyWorkflow()
    {
        var context = CreateContext();
        context.PreQuotes.ListWorkspaceCandidatesByProjectIdAsync(
                context.Project.Id,
                2,
                Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ProjectWorkspacePreQuoteCandidate>());

        var result = await context.Service.ExecuteAsync(
            new GetProjectWorkspaceQuery(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Workspace);
        Assert.Equal(
            ProjectWorkspaceResolutionState.Empty,
            result.Workspace.Workflow.ResolutionState);
        Assert.Null(result.Workspace.Workflow.PreQuoteId);
        Assert.Null(result.Workspace.Workflow.RequirementId);
        Assert.Null(result.Workspace.Workflow.RequirementStatus);
        Assert.Null(result.Workspace.Workflow.TechnicalProposalId);
        Assert.False(result.Workspace.Workflow.HasTechnicalProposal);
    }

    [Fact]
    public async Task ExecuteAsync_OnePreQuoteWithoutRequirement_ReturnsResolved()
    {
        var context = CreateContext();
        var preQuoteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        context.PreQuotes.ListWorkspaceCandidatesByProjectIdAsync(
                context.Project.Id,
                2,
                Arg.Any<CancellationToken>())
            .Returns([new ProjectWorkspacePreQuoteCandidate(preQuoteId)]);
        context.Requirements.GetCurrentByPreQuoteIdAsync(
                preQuoteId,
                Arg.Any<CancellationToken>())
            .Returns((CurrentRequirementReadModel?)null);

        var result = await context.Service.ExecuteAsync(
            new GetProjectWorkspaceQuery(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Workspace);
        Assert.Equal(
            ProjectWorkspaceResolutionState.Resolved,
            result.Workspace.Workflow.ResolutionState);
        Assert.Equal(preQuoteId, result.Workspace.Workflow.PreQuoteId);
        Assert.Null(result.Workspace.Workflow.RequirementId);
        Assert.False(result.Workspace.Workflow.HasTechnicalProposal);
    }

    [Fact]
    public async Task ExecuteAsync_CurrentRequirementWithoutProposal_ReturnsRequirement()
    {
        var context = CreateContext();
        var preQuoteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var requirementId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        context.PreQuotes.ListWorkspaceCandidatesByProjectIdAsync(
                context.Project.Id,
                2,
                Arg.Any<CancellationToken>())
            .Returns([new ProjectWorkspacePreQuoteCandidate(preQuoteId)]);
        context.Requirements.GetCurrentByPreQuoteIdAsync(
                preQuoteId,
                Arg.Any<CancellationToken>())
            .Returns(CreateRequirement(
                requirementId,
                preQuoteId,
                RequirementStatus.Pending,
                technicalProposalId: null));

        var result = await context.Service.ExecuteAsync(
            new GetProjectWorkspaceQuery(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Workspace);
        Assert.Equal(requirementId, result.Workspace.Workflow.RequirementId);
        Assert.Equal("PENDING", result.Workspace.Workflow.RequirementStatus);
        Assert.Null(result.Workspace.Workflow.TechnicalProposalId);
        Assert.False(result.Workspace.Workflow.HasTechnicalProposal);
    }

    [Fact]
    public async Task ExecuteAsync_CurrentRequirementWithProposal_ReturnsProposalId()
    {
        var context = CreateContext();
        var preQuoteId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var requirementId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var technicalProposalId = Guid.Parse(
            "44444444-4444-4444-4444-444444444444");
        context.PreQuotes.ListWorkspaceCandidatesByProjectIdAsync(
                context.Project.Id,
                2,
                Arg.Any<CancellationToken>())
            .Returns([new ProjectWorkspacePreQuoteCandidate(preQuoteId)]);
        context.Requirements.GetCurrentByPreQuoteIdAsync(
                preQuoteId,
                Arg.Any<CancellationToken>())
            .Returns(CreateRequirement(
                requirementId,
                preQuoteId,
                RequirementStatus.Processed,
                technicalProposalId));

        var result = await context.Service.ExecuteAsync(
            new GetProjectWorkspaceQuery(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Workspace);
        Assert.Equal(requirementId, result.Workspace.Workflow.RequirementId);
        Assert.Equal("PROCESSED", result.Workspace.Workflow.RequirementStatus);
        Assert.Equal(
            technicalProposalId,
            result.Workspace.Workflow.TechnicalProposalId);
        Assert.True(result.Workspace.Workflow.HasTechnicalProposal);
    }

    [Fact]
    public async Task ExecuteAsync_TwoPreQuotes_ReturnsAmbiguous()
    {
        var context = CreateContext();
        context.PreQuotes.ListWorkspaceCandidatesByProjectIdAsync(
                context.Project.Id,
                2,
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new ProjectWorkspacePreQuoteCandidate(
                    Guid.Parse("22222222-2222-2222-2222-222222222222")),
                new ProjectWorkspacePreQuoteCandidate(
                    Guid.Parse("33333333-3333-3333-3333-333333333333"))
            ]);

        var result = await context.Service.ExecuteAsync(
            new GetProjectWorkspaceQuery(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Workspace);
        Assert.Equal(
            ProjectWorkspaceResolutionState.Ambiguous,
            result.Workspace.Workflow.ResolutionState);
        Assert.Null(result.Workspace.Workflow.PreQuoteId);
        Assert.Null(result.Workspace.Workflow.RequirementId);
        Assert.False(result.Workspace.Workflow.HasTechnicalProposal);
        await context.Requirements.DidNotReceive()
            .GetCurrentByPreQuoteIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingProject_ReturnsNotFound()
    {
        var context = CreateContext();
        context.Projects.FindByIdAsync(
                context.Project.Id,
                Arg.Any<CancellationToken>())
            .Returns((ProjectEntity?)null);

        var result = await context.Service.ExecuteAsync(
            new GetProjectWorkspaceQuery(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GetProjectWorkspaceFailure.NotFound, result.Failure);
    }

    [Fact]
    public async Task ExecuteAsync_UserWithoutProjectAccess_ReturnsNotFound()
    {
        var context = CreateContext(projectOwnerUserId:
            Guid.Parse("99999999-9999-9999-9999-999999999999"));

        var result = await context.Service.ExecuteAsync(
            new GetProjectWorkspaceQuery(context.Project.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(GetProjectWorkspaceFailure.NotFound, result.Failure);
        await context.PreQuotes.DidNotReceive()
            .ListWorkspaceCandidatesByProjectIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>());
    }

    private static Context CreateContext(
        UserRole role = UserRole.User,
        Guid? projectOwnerUserId = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var user = User.CreateFromGoogle(
            "user@example.com",
            "User",
            null,
            null,
            At);
        if (role != UserRole.User)
        {
            user.ChangeRole(role, At.AddMinutes(1));
        }

        var client = Client.Create(
            ClientType.Company,
            "Steel and Glass",
            "SNG",
            ClientDocumentType.Nit,
            "900123456",
            "ventas@sng.example",
            "6015550000",
            "Calle 1",
            "Bogota",
            UserId,
            At);
        var project = ProjectEntity.Create(
            client.Id,
            "PR-001",
            "Proyecto",
            null,
            "Bogota",
            projectOwnerUserId ?? UserId,
            At);

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);

        var service = new GetProjectWorkspaceService(
            new GetProjectWorkspaceQueryValidator(),
            currentUser,
            identity,
            projects,
            clients,
            preQuotes,
            requirements);

        return new Context(
            service,
            project,
            preQuotes,
            requirements,
            projects);
    }

    private static CurrentRequirementReadModel CreateRequirement(
        Guid requirementId,
        Guid preQuoteId,
        RequirementStatus status,
        Guid? technicalProposalId) =>
        new(
            requirementId,
            preQuoteId,
            status,
            CommercialLine: null,
            At,
            technicalProposalId is not null,
            technicalProposalId,
            LatestAttemptId: null,
            LatestAttemptState: null,
            LatestAttemptOutcome: null,
            LatestAttemptErrorCode: null,
            CanEditDocuments: true,
            CanCancel: true,
            CanReplace: false,
            IsCurrent: true,
            SupersedesRequirementId: null,
            SupersededByRequirementId: null,
            Documents: Array.Empty<RequirementDocumentReadModel>());

    private sealed record Context(
        GetProjectWorkspaceService Service,
        ProjectEntity Project,
        IPreQuoteRepository PreQuotes,
        IRequirementRepository Requirements,
        IProjectRepository Projects);
}
