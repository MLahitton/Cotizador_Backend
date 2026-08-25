using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemSelection;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using FluentValidation;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class UpdateRequirementTechnicalProposalItemSelectionServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithConfirmSuggested_PersistsSuggestedAsSelected()
    {
        var context = CreateContext();

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemSelectionCommand(
                context.Proposal.Id,
                context.Item.Id,
                true,
                null,
                null,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            UpdateRequirementTechnicalProposalItemSelectionFailure.None,
            result.Failure);
        Assert.Equal("CONFIRMED_AS_SUGGESTED", result.Selection!.SelectionState);
        Assert.Equal(context.SuggestedSystem.Id, result.Selection.System!.Id);
        Assert.Equal(
            context.SuggestedGlass.GlassTypeId,
            result.Selection.Glass!.Id);
        Assert.Equal(context.SuggestedFinish.Id, result.Selection.Finish!.Id);
        Assert.Equal(context.SuggestedSystem.Id, context.Item.SelectedSystemId);
        Assert.Equal(
            context.SuggestedGlass.GlassTypeId,
            context.Item.SelectedGlassTypeId);
        Assert.Equal(context.SuggestedFinish.Id, context.Item.SelectedFinishTypeId);
        Assert.Equal(At, context.Item.SelectedAtUtc);
        Assert.Equal(UserId, context.Item.SelectedByUserId);
        await context.Requirements.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithPartialUpdate_UsesExistingSelectedForOmittedFields()
    {
        var context = CreateContext();
        context.Item.Select(
            context.AlternativeSystem.Id,
            context.SuggestedGlass.GlassTypeId,
            context.SuggestedFinish.Id,
            UserId,
            At.AddMinutes(-5));

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemSelectionCommand(
                context.Proposal.Id,
                context.Item.Id,
                false,
                null,
                context.AlternativeGlass.GlassTypeId,
                null),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("MODIFIED", result.Selection!.SelectionState);
        Assert.Equal(context.AlternativeSystem.Id, context.Item.SelectedSystemId);
        Assert.Equal(
            context.AlternativeGlass.GlassTypeId,
            context.Item.SelectedGlassTypeId);
        Assert.Equal(context.SuggestedFinish.Id, context.Item.SelectedFinishTypeId);
    }

    [Fact]
    public async Task Execute_WithUnselectableGlass_ReturnsInvalidGlassSelection()
    {
        var context = CreateContext();

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemSelectionCommand(
                context.Proposal.Id,
                context.Item.Id,
                false,
                null,
                context.UnselectableGlass.GlassTypeId,
                null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .InvalidGlassSelection,
            result.Failure);
        await context.Requirements.DidNotReceive()
            .SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithItemFromAnotherProposal_ReturnsItemNotFound()
    {
        var context = CreateContext();

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemSelectionCommand(
                context.Proposal.Id,
                Guid.NewGuid(),
                true,
                null,
                null,
                null),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .TechnicalProposalItemNotFound,
            result.Failure);
    }

    private static Context CreateContext()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        var glasses = Substitute.For<IGlassTypeCatalogRepository>();
        var finishes = Substitute.For<IFinishTypeCatalogRepository>();

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
        var extraction = RequirementExtractionResult.Create(
            Guid.NewGuid(),
            "AI2-1.0",
            "Ai2",
            "{}",
            1,
            0,
            0,
            0,
            "ai2_requirement_extraction",
            100,
            At);
        var extractedItem = RequirementExtractedItem.Create(
            extraction.Id,
            "element-1",
            1,
            "PV-06",
            "Puerta vidriera",
            StructuredElementType.Door,
            1,
            3740,
            2500,
            9.35m,
            0.91m,
            RequirementExtractionValueStatus.Explicit,
            false,
            [],
            "SLIDING_DOOR",
            "SLIDING",
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            null,
            "3831",
            "3831",
            "templado 6 mm",
            "templado",
            "templado",
            6m,
            null,
            null,
            null,
            null,
            "monolitico",
            null,
            null,
            false,
            "negro pintura al horno",
            "PAINTED",
            "negro",
            "BLACK",
            null,
            "MATTE",
            null,
            false,
            At);
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            extraction.Id,
            Guid.NewGuid(),
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var suggestedSystem = ProductSystem(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "K70");
        var alternativeSystem = ProductSystem(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "K72");
        var suggestedGlass = Glass(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "TEMP_6",
            true);
        var alternativeGlass = Glass(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "TEMP_8",
            true);
        var unselectableGlass = Glass(
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "TEMP_4",
            false);
        var suggestedFinish = Finish(
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            "BLACK_MATTE");
        var alternativeFinish = Finish(
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            "WHITE_MATTE");
        var item = RequirementTechnicalProposalItem.Create(
            proposal.Id,
            extractedItem.Id,
            suggestedSystem.Id,
            suggestedGlass.GlassTypeId,
            suggestedFinish.Id,
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
        SetPrivateProperty(item, "ExtractedItem", extractedItem);
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
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>())
            .Returns([suggestedSystem, alternativeSystem]);
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns([suggestedGlass, alternativeGlass, unselectableGlass]);
        finishes.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns([suggestedFinish, alternativeFinish]);

        var service =
            new UpdateRequirementTechnicalProposalItemSelectionService(
                new UpdateRequirementTechnicalProposalItemSelectionCommandValidator(),
                currentUser,
                identity,
                requirements,
                preQuotes,
                projects,
                clients,
                systems,
                glasses,
                finishes,
                new FixedTimeProvider(At));

        return new Context(
            service,
            requirements,
            proposal,
            item,
            suggestedSystem,
            alternativeSystem,
            suggestedGlass,
            alternativeGlass,
            unselectableGlass,
            suggestedFinish);
    }

    private static ProductSystemCatalogReadModel ProductSystem(Guid id, string code) =>
        new(
            id,
            code,
            $"Sistema {code}",
            $"Sistema tecnico {code}",
            code,
            "SLIDING_DOOR",
            code,
            "SERIE",
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private static GlassTypeCatalogReadModel Glass(
        Guid id,
        string code,
        bool isSelectable) =>
        new(
            id,
            code,
            $"Cristal {code}",
            null,
            true,
            null,
            IsSelectable: isSelectable);

    private static FinishTypeCatalogReadModel Finish(Guid id, string code) =>
        new(
            id,
            code,
            $"Acabado {code}",
            "PAINTED",
            code,
            "MATTE",
            "PAINTED",
            null,
            "ALUMINUM",
            true,
            false,
            true);

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
        UpdateRequirementTechnicalProposalItemSelectionService Service,
        IRequirementRepository Requirements,
        RequirementTechnicalProposal Proposal,
        RequirementTechnicalProposalItem Item,
        ProductSystemCatalogReadModel SuggestedSystem,
        ProductSystemCatalogReadModel AlternativeSystem,
        GlassTypeCatalogReadModel SuggestedGlass,
        GlassTypeCatalogReadModel AlternativeGlass,
        GlassTypeCatalogReadModel UnselectableGlass,
        FinishTypeCatalogReadModel SuggestedFinish);
}
