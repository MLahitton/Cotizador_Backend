using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.CreateManualRequirementTechnicalProposalItem;
using CotizadorBackend.Tests.TestDoubles;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class CreateManualRequirementTechnicalProposalItemServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SystemId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GlassId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FinishId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset At =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithValidRequest_AddsManualIncludedItemAndInvalidatesConfirmation()
    {
        var context = CreateContext(confirmProposal: true);
        var initialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.ExecuteAsync(
            new CreateManualRequirementTechnicalProposalItemCommand(
                context.Requirement.Id,
                "M-02",
                "Ventana manual",
                "WINDOW",
                2,
                1500,
                2400,
                SystemId,
                GlassId,
                FinishId,
                "Agregada por plano"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Item);
        Assert.Equal("Manual", result.Item!.Source);
        Assert.Equal(2, result.Item.Sequence);
        Assert.Equal(initialRevision + 1, result.Item.CommercialRevision);
        Assert.False(context.Proposal.IsCommerciallyConfirmed);
        Assert.Equal(["begin", "find", "save", "commit"], context.Calls);

        var manual = Assert.Single(
            context.Proposal.Items,
            item => item.Source == TechnicalProposalItemSource.Manual);
        Assert.Null(manual.RequirementExtractedItemId);
        Assert.True(manual.IsIncluded);
        Assert.Equal("M-02", manual.Reference);
        Assert.Equal("Ventana manual", manual.Description);
        Assert.Equal(2, manual.EffectiveQuantity);
        Assert.Equal(1500, manual.EffectiveWidthMillimeters);
        Assert.Equal(2400, manual.EffectiveHeightMillimeters);
        Assert.Equal(SystemId, manual.SelectedSystemId);
        Assert.Equal(GlassId, manual.SelectedGlassTypeId);
        Assert.Equal(FinishId, manual.SelectedFinishTypeId);
        Assert.Equal("Agregada por plano", manual.ManualNote);
    }

    private static Context CreateContext(bool confirmProposal = false)
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
        var transaction = Substitute.For<IRequirementPersistenceTransaction>();
        var calls = new List<string>();

        var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
        SetPrivateProperty(user, "Id", UserId);
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
        var preQuote = PreQuote.Create(
            project.Id,
            UserId,
            "PC-2026-0001",
            null,
            At);
        var requirement = Requirement.Create(
            preQuote.Id,
            UserId,
            RequirementCommercialLine.Essential,
            At);
        var extraction = RequirementExtractionResult.Create(
            Guid.NewGuid(),
            "1",
            "AI2",
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
            At);        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            extraction.Id,
            Guid.NewGuid(),
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var aiItem = RequirementTechnicalProposalItem.Create(
            proposal.Id,
            extractedItem.Id,
            SystemId,
            GlassId,
            FinishId,
            extractedItem.Sequence,
            extractedItem.Reference,
            extractedItem.Description,
            extractedItem.ElementType,
            extractedItem.Quantity,
            extractedItem.WidthMillimeters,
            extractedItem.HeightMillimeters,
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
        SetPrivateProperty(aiItem, "ExtractedItem", extractedItem);
        proposal.AddItem(aiItem);

        if (confirmProposal)
        {
            proposal.ConfirmCommercialSelection(UserId, At.AddMinutes(-1));
        }

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.BeginPricingUpdateTransactionAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("begin");
                return transaction;
            });
        requirements.FindCurrentTechnicalProposalForUpdateAsync(
                requirement.Id,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("find");
                return proposal;
            });
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("save");
                return Task.CompletedTask;
            });
        transaction.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("commit");
                return Task.CompletedTask;
            });
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
            .Returns([ProductSystem()]);
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns([Glass()]);
        finishes.ListActiveAsync(Arg.Any<CancellationToken>())
            .Returns([Finish()]);

        var service = new CreateManualRequirementTechnicalProposalItemService(
            new CreateManualRequirementTechnicalProposalItemCommandValidator(),
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

        return new Context(service, requirement, proposal, calls);
    }

    private static ProductSystemCatalogReadModel ProductSystem() =>
        new(
            SystemId,
            "K70",
            "Sistema K70",
            "Sistema tecnico K70",
            "K70",
            "SLIDING_DOOR",
            "K70",
            "SERIE 70",
            "ESSENTIAL",
            "STANDARD",
            true,
            true,
            true,
            true,
            false,
            true);

    private static GlassTypeCatalogReadModel Glass() =>
        new(
            GlassId,
            "TEMP_6",
            "Templado 6 mm",
            null,
            true,
            null,
            IsSelectable: true);

    private static FinishTypeCatalogReadModel Finish() =>
        new(
            FinishId,
            "BLACK",
            "Negro",
            null,
            null,
            null,
            null,
            null,
            null,
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
        CreateManualRequirementTechnicalProposalItemService Service,
        Requirement Requirement,
        RequirementTechnicalProposal Proposal,
        List<string> Calls);
}
