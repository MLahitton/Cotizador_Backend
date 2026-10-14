using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemPhysicalData;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class UpdateRequirementTechnicalProposalItemPhysicalDataServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Controller_MapsPhysicalDataAndOwnerOnlyAccess(bool owner)
    {
        var context = CreateContext(UserRole.Admin, owner);
        var controller = new global::Api.Controllers.TechnicalProposalItemPhysicalDataController(context.Service)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };
        var result = await controller.Put(context.Proposal.Id, context.Item.Id,
            new global::Contracts.PreQuotes.UpdateRequirementTechnicalProposalItemPhysicalDataRequest(2, null, null),
            TestContext.Current.CancellationToken);
        var response = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
        Assert.Equal(owner ? 200 : 403, response.StatusCode);
        if (owner)
        {
            var value = Assert.IsType<global::Contracts.PreQuotes.UpdateRequirementTechnicalProposalItemPhysicalDataResponse>(response.Value);
            Assert.Equal(2, value.Quantity);
            Assert.Equal(3740, value.WidthMm);
            Assert.Equal(2500, value.HeightMm);
            Assert.True(value.DataCompleteness.IsComplete);
        }
    }

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2, null, null, false)]
    [InlineData(null, 2000, null, false)]
    [InlineData(null, null, 2800, false)]
    [InlineData(2, 2000, 2800, false)]
    [InlineData(2, 2000, 2800, true)]
    public async Task Execute_ChangesPhysicalDataWithoutSelecting(
        int? quantity, int? width, int? height, bool selected)
    {
        var context = CreateContext();
        if (selected) context.Item.Select(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), UserId, At);
        var selection = (context.Item.SelectedSystemId, context.Item.SelectedGlassTypeId,
            context.Item.SelectedFinishTypeId, context.Item.SelectedAtUtc, context.Item.SelectedByUserId);
        var suggested = (context.Item.SuggestedSystemId, context.Item.SuggestedGlassTypeId, context.Item.SuggestedFinishTypeId);
        var revision = context.Proposal.CommercialRevision;
        SetPrivateProperty(context.Proposal, "CommercialConfirmedAtUtc", (DateTimeOffset?)At);
        SetPrivateProperty(context.Proposal, "CommercialConfirmedByUserId", (Guid?)UserId);

        var result = await context.Service.ExecuteAsync(new(context.Proposal.Id, context.Item.Id,
            quantity, width, height), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(quantity ?? 1, context.Item.EffectiveQuantity);
        Assert.Equal(width ?? 3740, context.Item.EffectiveWidthMillimeters);
        Assert.Equal(height ?? 2500, context.Item.EffectiveHeightMillimeters);
        Assert.Equal(selection, (context.Item.SelectedSystemId, context.Item.SelectedGlassTypeId,
            context.Item.SelectedFinishTypeId, context.Item.SelectedAtUtc, context.Item.SelectedByUserId));
        Assert.Equal(suggested, (context.Item.SuggestedSystemId, context.Item.SuggestedGlassTypeId, context.Item.SuggestedFinishTypeId));
        Assert.Equal(revision + 1, context.Proposal.CommercialRevision);
        Assert.Null(context.Proposal.CommercialConfirmedAtUtc);
        Assert.Null(context.Proposal.CommercialConfirmedByUserId);
        Assert.True(result.PhysicalData!.DataCompleteness.IsComplete);
        await context.Requirements.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(1, 3740, 2500)]
    public async Task Execute_NoOp_PreservesRevisionConfirmationAndOverrides(int? quantity, int? width, int? height)
    {
        var context = CreateContext();
        var revision = context.Proposal.CommercialRevision;
        SetPrivateProperty(context.Proposal, "CommercialConfirmedAtUtc", (DateTimeOffset?)At);
        var result = await context.Service.ExecuteAsync(new(context.Proposal.Id, context.Item.Id,
            quantity, width, height), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(revision, context.Proposal.CommercialRevision);
        Assert.Equal(At, context.Proposal.CommercialConfirmedAtUtc);
        Assert.Null(context.Item.ManualQuantityOverride);
        await context.Requirements.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_NullFields_PreserveExistingOverrides()
    {
        var context = CreateContext();
        context.Item.ApplyManualDataOverride(3, 2100, 2800);
        var result = await context.Service.ExecuteAsync(new(context.Proposal.Id, context.Item.Id, 4, null, null),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, context.Item.EffectiveQuantity);
        Assert.Equal(2100, context.Item.EffectiveWidthMillimeters);
        Assert.Equal(2800, context.Item.EffectiveHeightMillimeters);
    }

    [Fact]
    public async Task Execute_ExcludedItem_DoesNotInvalidateCommercialConfirmation()
    {
        var context = CreateContext();
        context.Item.Exclude(UserId, At, null);
        var revision = context.Proposal.CommercialRevision;
        SetPrivateProperty(context.Proposal, "CommercialConfirmedAtUtc", (DateTimeOffset?)At);
        var result = await context.Service.ExecuteAsync(new(context.Proposal.Id, context.Item.Id, 2, null, null),
            TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(revision, context.Proposal.CommercialRevision);
        Assert.Equal(At, context.Proposal.CommercialConfirmedAtUtc);
    }

    [Theory]
    [InlineData(UserRole.User, true, true)]
    [InlineData(UserRole.Admin, true, true)]
    [InlineData(UserRole.User, false, false)]
    [InlineData(UserRole.Admin, false, false)]
    public async Task Execute_OnlyOwnerMayMutate(UserRole role, bool owner, bool allowed)
    {
        var context = CreateContext(role, owner);
        var result = await context.Service.ExecuteAsync(new(context.Proposal.Id, context.Item.Id, 2, null, null),
            TestContext.Current.CancellationToken);
        Assert.Equal(allowed, result.IsSuccess);
        if (!allowed)
        {
            Assert.Equal(UpdateRequirementTechnicalProposalItemPhysicalDataFailure.Forbidden, result.Failure);
            await context.Requirements.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task Execute_InactiveOwner_IsRejected()
    {
        var context = CreateContext(active: false);
        var result = await context.Service.ExecuteAsync(new(context.Proposal.Id, context.Item.Id, 2, null, null),
            TestContext.Current.CancellationToken);
        Assert.Equal(UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InactiveUser, result.Failure);
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(null, -1, null)]
    [InlineData(null, null, 0)]
    public async Task Execute_InvalidDimensions_AreRejected(int? quantity, int? width, int? height)
    {
        var context = CreateContext();
        var result = await context.Service.ExecuteAsync(new(context.Proposal.Id, context.Item.Id, quantity, width, height),
            TestContext.Current.CancellationToken);
        Assert.Equal(UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InvalidRequest, result.Failure);
        await context.Requirements.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Context CreateContext(UserRole role = UserRole.User, bool owner = true, bool active = true)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var preQuotes = Substitute.For<IPreQuoteRepository>();
        var projects = Substitute.For<IProjectRepository>();
        var clients = Substitute.For<IClientRepository>();

        var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
        SetPrivateProperty(user, "Id", UserId);
        SetPrivateProperty(user, "Role", role);
        SetPrivateProperty(user, "IsActive", active);
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
        if (!owner) SetPrivateProperty(project, "CreatedByUserId", Guid.NewGuid());
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

        var service = new UpdateRequirementTechnicalProposalItemPhysicalDataService(
            new UpdateRequirementTechnicalProposalItemPhysicalDataCommandValidator(),
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
        UpdateRequirementTechnicalProposalItemPhysicalDataService Service,
        IRequirementRepository Requirements,
        RequirementTechnicalProposal Proposal,
        RequirementTechnicalProposalItem Item);
}
