using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.ConfirmRequirementTechnicalProposalSelection;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class ConfirmRequirementTechnicalProposalSelectionServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SuggestedSystemId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SuggestedGlassId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SuggestedFinishId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SelectedSystemId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid SelectedGlassId =
        Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid SelectedFinishId =
        Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset At =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithSuggestedComplete_ConfirmsAndCopiesSuggestedSelection()
    {
        var context = CreateContext();

        var result = await context.Service.ExecuteAsync(
            new ConfirmRequirementTechnicalProposalSelectionCommand(
                context.Proposal.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("CONFIRMED", result.Confirmation!.State);
        Assert.Equal(At, result.Confirmation.ConfirmedAtUtc);
        Assert.Equal(UserId, result.Confirmation.ConfirmedByUserId);
        Assert.True(context.Proposal.IsCommerciallyConfirmed);
        Assert.Equal(SuggestedSystemId, context.Item.SelectedSystemId);
        Assert.Equal(SuggestedGlassId, context.Item.SelectedGlassTypeId);
        Assert.Equal(SuggestedFinishId, context.Item.SelectedFinishTypeId);
        await context.Requirements.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithExistingSelectedConfiguration_DoesNotOverwriteSelection()
    {
        var context = CreateContext();
        context.Item.Select(
            SelectedSystemId,
            SelectedGlassId,
            SelectedFinishId,
            UserId,
            At.AddMinutes(-5));

        var result = await context.Service.ExecuteAsync(
            new ConfirmRequirementTechnicalProposalSelectionCommand(
                context.Proposal.Id),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(SelectedSystemId, context.Item.SelectedSystemId);
        Assert.Equal(SelectedGlassId, context.Item.SelectedGlassTypeId);
        Assert.Equal(SelectedFinishId, context.Item.SelectedFinishTypeId);
        Assert.Equal(At, context.Proposal.CommercialConfirmedAtUtc);
    }

    [Fact]
    public async Task Execute_WithIncompleteProposal_ReturnsIncompleteTechnicalProposal()
    {
        var context = CreateContext(withoutSuggestedGlass: true);

        var result = await context.Service.ExecuteAsync(
            new ConfirmRequirementTechnicalProposalSelectionCommand(
                context.Proposal.Id),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            ConfirmRequirementTechnicalProposalSelectionFailure
                .IncompleteTechnicalProposal,
            result.Failure);
        Assert.False(context.Proposal.IsCommerciallyConfirmed);
        await context.Requirements.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Context CreateContext(bool withoutSuggestedGlass = false)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();

        var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
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
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var item = RequirementTechnicalProposalItem.Create(
            proposal.Id,
            Guid.NewGuid(),
            SuggestedSystemId,
            SuggestedGlassId,
            SuggestedFinishId,
            0.90m,
            0.90m,
            0.90m,
            0.90m,
            false,
            true,
            true,
            [],
            [],
            [],
            [],
            0,
            null,
            null,
            "NotEvaluated",
            At);
        if (withoutSuggestedGlass)
        {
            SetPrivateProperty<Guid?>(item, "SuggestedGlassTypeId", null);
        }

        proposal.AddItem(item);

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindTechnicalProposalForUpdateAsync(
                proposal.Id,
                Arg.Any<CancellationToken>())
            .Returns(proposal);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDetails(
                preQuote.Id,
                preQuote.ProjectId,
                0,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>())
            .Returns(client);

        var service = new ConfirmRequirementTechnicalProposalSelectionService(
            currentUser,
            identity,
            requirements,
            preQuotes,
            projects,
            clients,
            new FixedTimeProvider(At));

        return new Context(service, requirements, proposal, item);
    }

    private static void SetPrivateProperty<T>(
        object target,
        string propertyName,
        T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private sealed record Context(
        ConfirmRequirementTechnicalProposalSelectionService Service,
        IRequirementRepository Requirements,
        RequirementTechnicalProposal Proposal,
        RequirementTechnicalProposalItem Item);
}
