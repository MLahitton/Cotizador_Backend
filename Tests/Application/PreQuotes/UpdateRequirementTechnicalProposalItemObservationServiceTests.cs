using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemObservation;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class UpdateRequirementTechnicalProposalItemObservationServiceTests
{
    private static readonly Guid UserId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At =
        new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_WithManualObservation_TrimsAndUpdatesOnlyObservationMetadata()
    {
        var context = CreateContext();
        var initialSelectionState = context.Item.SelectedAtUtc;
        var initialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemObservationCommand(
                context.Proposal.Id,
                context.Item.Id,
                "  Coordinar apertura en obra  "),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Coordinar apertura en obra", result.Observation!.ManualObservation);
        Assert.Equal("Coordinar apertura en obra", context.Item.ManualObservation);
        Assert.Equal(initialSelectionState, context.Item.SelectedAtUtc);
        Assert.Equal(initialRevision, context.Proposal.CommercialRevision);
        await context.Requirements.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithWhitespaceObservation_ClearsObservation()
    {
        var context = CreateContext();
        context.Item.UpdateManualObservation("Nota previa");

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemObservationCommand(
                context.Proposal.Id,
                context.Item.Id,
                "   "),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Observation!.ManualObservation);
        Assert.Null(context.Item.ManualObservation);
    }

    [Fact]
    public async Task Execute_WithObservationLongerThanMaxLength_ReturnsInvalidRequest()
    {
        var context = CreateContext();

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemObservationCommand(
                context.Proposal.Id,
                context.Item.Id,
                new string('x', 501)),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            UpdateRequirementTechnicalProposalItemObservationFailure.InvalidRequest,
            result.Failure);
        await context.Requirements.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_WithItemFromAnotherProposal_ReturnsItemNotFound()
    {
        var context = CreateContext();

        var result = await context.Service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemObservationCommand(
                context.Proposal.Id,
                Guid.NewGuid(),
                "Coordinar con residente"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            UpdateRequirementTechnicalProposalItemObservationFailure.TechnicalProposalItemNotFound,
            result.Failure);
        await context.Requirements.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Context CreateContext()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();

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
        var project = ProjectEntity.Create(client.Id, "P-001", "Project", null, null, UserId, At);
        var preQuote = PreQuote.Create(project.Id, UserId, "PC-2026-0001", null, At);
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
            At,
            occurrenceContext: "Planta de ubicacion");
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            extraction.Id,
            Guid.NewGuid(),
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var item = RequirementTechnicalProposalItem.Create(
            proposal.Id,
            extractedItem.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
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
        SetPrivateProperty(item, "ExtractedItem", extractedItem);
        proposal.AddItem(item);

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        requirements.FindTechnicalProposalForUpdateAsync(proposal.Id, Arg.Any<CancellationToken>())
            .Returns(proposal);
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>())
            .Returns(new PreQuoteDetails(
                preQuote.Id,
                preQuote.ProjectId,
                0,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);

        var service = new UpdateRequirementTechnicalProposalItemObservationService(
            new UpdateRequirementTechnicalProposalItemObservationCommandValidator(),
            currentUser,
            identity,
            requirements,
            preQuotes,
            projects,
            clients);

        return new Context(service, requirements, proposal, item);
    }

    private static void SetPrivateProperty<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private sealed record Context(
        UpdateRequirementTechnicalProposalItemObservationService Service,
        IRequirementRepository Requirements,
        RequirementTechnicalProposal Proposal,
        RequirementTechnicalProposalItem Item);
}