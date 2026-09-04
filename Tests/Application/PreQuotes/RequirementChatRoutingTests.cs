using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.GetRequirementDetails;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Application.PreQuotes.RequirementChat;
using Application.PreQuotes.RequirementChatActions;
using Domain.Catalogs;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;
using ProjectEntity = Domain.Projects.Project;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class RequirementChatRoutingTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset At = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SystemAId = Guid.Parse("22222222-2222-2222-2222-222222222221");
    private static readonly Guid SystemBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GlassAId = Guid.Parse("33333333-3333-3333-3333-333333333331");
    private static readonly Guid GlassBId = Guid.Parse("33333333-3333-3333-3333-333333333332");
    private static readonly Guid FinishAId = Guid.Parse("44444444-4444-4444-4444-444444444441");
    private static readonly Guid FinishBId = Guid.Parse("44444444-4444-4444-4444-444444444442");

    [Fact]
    public async Task RequirementChat_InformationalGeneralChat_UsesRespondAndDoesNotCreateActionPlan()
    {
        var context = CreateContext();
        context.Ai.InterpretActionAsync(Arg.Any<RequirementChatActionInterpretationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatActionIntent(false, null, null, null, null, null, null, null, 0.91m, false, null, "que sistema tiene V-9"));
        context.Ai.RespondAsync(Arg.Any<RequirementChatAiRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatAiResponse("V-9 usa el sistema sugerido."));

        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "que sistema tiene V-9"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var lastInteraction = Assert.IsType<RequirementChatInteractionReadModel>(result.LastInteraction);
        Assert.Equal("INFORMATIONAL", lastInteraction.MessageType);
        Assert.Empty(lastInteraction.Reasons);
        Assert.Empty(lastInteraction.AvailableOptions);
        Assert.Null(lastInteraction.PlanId);
        Assert.Equal("V-9 usa el sistema sugerido.", result.Thread!.Messages.Last().Content);
        await context.Ai.Received(1).RespondAsync(
            Arg.Is<RequirementChatAiRequest>(request => request.Scope == "REQUIREMENT"),
            Arg.Any<CancellationToken>());
        Assert.Empty(context.Store.Plans);
    }

    [Fact]
    public async Task RequirementChat_InformationalItemChat_SendsItemScopeAndContextItem()
    {
        var context = CreateContext();
        var itemId = context.Proposal.Items.First().Id;
        context.Ai.InterpretActionAsync(Arg.Any<RequirementChatActionInterpretationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatActionIntent(false, null, null, null, null, null, null, null, 0.90m, false, null, "explicame este item"));
        context.Ai.RespondAsync(Arg.Any<RequirementChatAiRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatAiResponse("Este item esta listo."));

        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, itemId, "explicame este item"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        await context.Ai.Received(1).InterpretActionAsync(
            Arg.Is<RequirementChatActionInterpretationRequest>(request =>
                request.Scope == "ITEM" && request.TechnicalProposalItemId == itemId),
            Arg.Any<CancellationToken>());
        Assert.Equal("ITEM", result.Thread!.Scope);
        var lastInteraction = Assert.IsType<RequirementChatInteractionReadModel>(result.LastInteraction);
        Assert.Equal("INFORMATIONAL", lastInteraction.MessageType);
        Assert.Empty(lastInteraction.Reasons);
        Assert.Empty(lastInteraction.AvailableOptions);
    }

    [Fact]
    public async Task RequirementChat_ActionGeneral_ReturnsActionPlanWithoutWritingBeforeConfirm()
    {
        var context = CreateContext();
        var item = context.Proposal.Items.First();
        context.Ai.InterpretActionAsync(Arg.Any<RequirementChatActionInterpretationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatActionIntent(true, "CHANGE_SYSTEM", "REQUIREMENT", "V-9", "K72", null, null, null, 0.87m, false, null, "cambia V-9 a K72"));

        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "cambia V-9 a K72"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("ACTION_PLAN", result.LastInteraction!.MessageType);
        Assert.NotNull(result.LastInteraction.PlanId);
        Assert.True(result.LastInteraction.RequiresConfirmation);
        Assert.Equal("CHANGE_SYSTEM", result.LastInteraction.ActionType);
        Assert.Equal(item.Id, result.LastInteraction.TargetTechnicalProposalItemId);
        Assert.Equal("K70", result.LastInteraction.CurrentValue);
        Assert.Equal("K72", result.LastInteraction.RequestedValue);
        Assert.Null(item.SelectedSystemId);
        await context.Ai.DidNotReceive().RespondAsync(Arg.Any<RequirementChatAiRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequirementChat_ActionItemSpecific_UsesContextualItemForPlan()
    {
        var context = CreateContext();
        var item = context.Proposal.Items.First();
        context.Ai.InterpretActionAsync(Arg.Any<RequirementChatActionInterpretationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatActionIntent(true, "CHANGE_FINISH", "ITEM", null, "WHITE_MATTE", null, null, null, 0.89m, false, null, "ponlo en blanco"));

        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, item.Id, "ponlo en blanco"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("ACTION_PLAN", result.LastInteraction!.MessageType);
        Assert.Equal(item.Id, result.LastInteraction.TargetTechnicalProposalItemId);
        Assert.Equal("CHANGE_FINISH", result.LastInteraction.ActionType);
        Assert.Equal("BLACK_MATTE", result.LastInteraction.CurrentValue);
        Assert.Equal("WHITE_MATTE", result.LastInteraction.RequestedValue);
    }

    [Fact]
    public async Task RequirementChat_ClarificationIntent_ReturnsClarificationAndNoActionPlan()
    {
        var context = CreateContext();
        context.Ai.InterpretActionAsync(Arg.Any<RequirementChatActionInterpretationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatActionIntent(true, "CHANGE_FINISH", "REQUIREMENT", null, null, null, null, null, 0.70m, true, "Que acabado quieres usar?", "quiero otro acabado"));

        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "quiero otro acabado"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("CLARIFICATION", result.LastInteraction!.MessageType);
        Assert.Null(result.LastInteraction.PlanId);
        Assert.Equal("Que acabado quieres usar?", result.Thread!.Messages.Last().Content);
        Assert.Empty(context.Store.Plans);
        await context.Ai.DidNotReceive().RespondAsync(Arg.Any<RequirementChatAiRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequirementChat_AmbiguousReference_UsesBackendPlanClarificationAndDoesNotWrite()
    {
        var context = CreateContext(duplicateReference: true);
        context.Ai.InterpretActionAsync(Arg.Any<RequirementChatActionInterpretationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatActionIntent(true, "CHANGE_SYSTEM", "REQUIREMENT", "V-9", "K72", null, null, null, 0.88m, false, null, "cambia V-9 a K72"));

        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "cambia V-9 a K72"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("CLARIFICATION", result.LastInteraction!.MessageType);
        Assert.Contains("TARGET_REFERENCE_AMBIGUOUS", result.LastInteraction.Reasons);
        Assert.Null(context.Proposal.Items.First().SelectedSystemId);
        await context.Ai.DidNotReceive().RespondAsync(Arg.Any<RequirementChatAiRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequirementChat_InvalidCatalog_BackendValidationWins()
    {
        var context = CreateContext();
        context.Ai.InterpretActionAsync(Arg.Any<RequirementChatActionInterpretationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RequirementChatActionIntent(true, "CHANGE_SYSTEM", "REQUIREMENT", "V-9", "NO_EXISTE", null, null, null, 0.88m, false, null, "cambia V-9 a NO_EXISTE"));

        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "cambia V-9 a NO_EXISTE"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("CLARIFICATION", result.LastInteraction!.MessageType);
        Assert.Contains("SYSTEM_NOT_FOUND", result.LastInteraction.Reasons);
        await context.Ai.DidNotReceive().RespondAsync(Arg.Any<RequirementChatAiRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequirementChat_Ai2InterpreterFailure_ReturnsControlledFailureAndDoesNotWriteActionPlan()
    {
        var context = CreateContext();
        context.Ai.InterpretActionAsync(Arg.Any<RequirementChatActionInterpretationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<RequirementChatActionIntent>>(_ => throw new RequirementChatAiUnavailableException());

        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "cambia V-9 a K72"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementChatFailure.Ai2Unavailable, result.Failure);
        Assert.Empty(context.Store.Plans);
        await context.Ai.DidNotReceive().RespondAsync(Arg.Any<RequirementChatAiRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequirementChat_PendingClarification_SendsPendingActionContextAndReusesPlanId()
    {
        var context = CreateContext();
        RequirementChatActionInterpretationRequest? secondRequest = null;
        context.Ai.InterpretActionAsync(
                Arg.Any<RequirementChatActionInterpretationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(new RequirementChatActionIntent(true, "CHANGE_SYSTEM", "REQUIREMENT", "V-9", "venecia fermo", null, null, null, 0.88m, false, null, "cambia V-9 a venecia fermo")),
                call =>
                {
                    secondRequest = call.Arg<RequirementChatActionInterpretationRequest>();
                    return Task.FromResult(new RequirementChatActionIntent(true, "CHANGE_SYSTEM", "REQUIREMENT", null, "K72", null, null, null, 0.92m, false, null, "Que sea a Sistema K72"));
                });

        var first = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "cambia V-9 a venecia fermo"),
            TestContext.Current.CancellationToken);
        var firstPlanId = first.LastInteraction!.PlanId;
        var second = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "Que sea a Sistema K72"),
            TestContext.Current.CancellationToken);

        Assert.Equal("CLARIFICATION", first.LastInteraction.MessageType);
        Assert.NotNull(firstPlanId);
        Assert.NotEmpty(first.LastInteraction.AvailableOptions);
        Assert.True(second.IsSuccess);
        Assert.Equal("ACTION_PLAN", second.LastInteraction!.MessageType);
        Assert.Equal(firstPlanId, second.LastInteraction.PlanId);
        Assert.True(second.LastInteraction.RequiresConfirmation);
        var json = Serialize(secondRequest!.Context);
        Assert.Contains("\"pendingAction\"", json);
        Assert.Contains("\"planId\"", json);
        Assert.Contains("\"actionType\":\"CHANGE_SYSTEM\"", json);
        Assert.Contains("\"targetReference\":\"V-9\"", json);
        Assert.Contains("\"requestedValue\":\"venecia fermo\"", json);
        Assert.Contains("\"availableOptions\"", json);
    }

    [Fact]
    public async Task RequirementChat_ItemPendingClarification_SendsPendingActionForSameItem()
    {
        var context = CreateContext();
        var itemId = context.Proposal.Items.First().Id;
        RequirementChatActionInterpretationRequest? secondRequest = null;
        context.Ai.InterpretActionAsync(
                Arg.Any<RequirementChatActionInterpretationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(new RequirementChatActionIntent(true, "CHANGE_FINISH", "ITEM", null, "inox", null, null, null, 0.88m, false, null, "ponlo en inox")),
                call =>
                {
                    secondRequest = call.Arg<RequirementChatActionInterpretationRequest>();
                    return Task.FromResult(new RequirementChatActionIntent(true, "CHANGE_FINISH", "ITEM", null, "WHITE_MATTE", null, null, null, 0.90m, false, null, "que sea blanco"));
                });

        await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, itemId, "ponlo en inox"),
            TestContext.Current.CancellationToken);
        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, itemId, "que sea blanco"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("ACTION_PLAN", result.LastInteraction!.MessageType);
        var json = Serialize(secondRequest!.Context);
        Assert.Contains("\"scope\":\"ITEM\"", json);
        Assert.Contains($"\"targetTechnicalProposalItemId\":\"{itemId}\"", json);
    }

    [Fact]
    public async Task RequirementChat_PendingClarificationFromAnotherItem_IsNotSent()
    {
        var context = CreateContext();
        var itemA = context.Proposal.Items.First().Id;
        var itemB = context.Proposal.Items.Last().Id;
        RequirementChatActionInterpretationRequest? secondRequest = null;
        context.Ai.InterpretActionAsync(
                Arg.Any<RequirementChatActionInterpretationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(new RequirementChatActionIntent(true, "CHANGE_FINISH", "ITEM", null, "inox", null, null, null, 0.88m, false, null, "ponlo en inox")),
                call =>
                {
                    secondRequest = call.Arg<RequirementChatActionInterpretationRequest>();
                    return Task.FromResult(new RequirementChatActionIntent(false, null, null, null, null, null, null, null, 0.84m, false, null, "que sea blanco"));
                });
        context.Ai.RespondAsync(
                Arg.Any<RequirementChatAiRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new RequirementChatAiResponse("Respuesta informativa."));

        await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, itemA, "ponlo en inox"),
            TestContext.Current.CancellationToken);
        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, itemB, "que sea blanco"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("INFORMATIONAL", result.LastInteraction!.MessageType);
        Assert.DoesNotContain("\"pendingAction\"", Serialize(secondRequest!.Context));
    }

    [Fact]
    public async Task RequirementChat_ExpiredPendingClarification_IsNotReused()
    {
        var context = CreateContext();
        RequirementChatActionInterpretationRequest? secondRequest = null;
        context.Ai.InterpretActionAsync(
                Arg.Any<RequirementChatActionInterpretationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(new RequirementChatActionIntent(true, "CHANGE_SYSTEM", "REQUIREMENT", "V-9", "venecia fermo", null, null, null, 0.88m, false, null, "cambia V-9 a venecia fermo")),
                call =>
                {
                    secondRequest = call.Arg<RequirementChatActionInterpretationRequest>();
                    return Task.FromResult(new RequirementChatActionIntent(false, null, null, null, null, null, null, null, 0.80m, false, null, "Que sea a Sistema K72"));
                });
        context.Ai.RespondAsync(
                Arg.Any<RequirementChatAiRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new RequirementChatAiResponse("Respuesta informativa."));

        await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "cambia V-9 a venecia fermo"),
            TestContext.Current.CancellationToken);
        context.Clock.Advance(TimeSpan.FromMinutes(16));
        var result = await context.Service.ExecuteAsync(
            new SendRequirementChatMessageCommand(context.Requirement.Id, null, "Que sea a Sistema K72"),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("INFORMATIONAL", result.LastInteraction!.MessageType);
        Assert.DoesNotContain("\"pendingAction\"", Serialize(secondRequest!.Context));
    }

    private static Context CreateContext(bool duplicateReference = false)
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
        var chat = new FakeRequirementChatRepository();
        var ai = Substitute.For<IRequirementChatAiClient>();
        var clock = new FixedTimeProvider(At);
        var store = new ObservablePlanStore(clock);

        var user = User.CreateFromGoogle("user@example.com", "User", null, null, At);
        var client = Client.Create(ClientType.Company, "Client", null, null, null, null, null, null, null, UserId, At);
        var project = ProjectEntity.Create(client.Id, "P-001", "Project", null, null, UserId, At);
        var preQuote = PreQuote.Create(project.Id, UserId, "PC-2026-0001", null, At);
        var requirement = Requirement.Create(preQuote.Id, UserId, RequirementCommercialLine.Essential, At);
        var proposal = CreateProposal(requirement, duplicateReference);

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(user);
        requirements.FindByIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(requirement);
        requirements.GetCurrentTechnicalProposalAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns(proposal);
        requirements.GetCurrentPricingSnapshotAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns((RequirementPricingSnapshot?)null);
        requirements.ListFilesByRequirementIdAsync(requirement.Id, Arg.Any<CancellationToken>()).Returns([]);
        preQuotes.FindByIdAsync(preQuote.Id, Arg.Any<CancellationToken>()).Returns(new PreQuoteDetails(preQuote.Id, preQuote.ProjectId, 0, preQuote.CreatedAtUtc, preQuote.UpdatedAtUtc));
        projects.FindByIdAsync(project.Id, Arg.Any<CancellationToken>()).Returns(project);
        clients.FindByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);
        systems.ListActiveAsync(Arg.Any<CancellationToken>()).Returns([System("K70", SystemAId), System("K72", SystemBId)]);
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>()).Returns([System("K70", SystemAId), System("K72", SystemBId)]);
        glasses.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>()).Returns([Glass("TEMP_6", GlassAId), Glass("TEMP_8", GlassBId)]);
        finishes.ListActiveAsync(Arg.Any<CancellationToken>()).Returns([Finish("BLACK_MATTE", FinishAId), Finish("WHITE_MATTE", FinishBId)]);

        var details = new GetRequirementDetailsService(currentUser, identity, preQuotes, projects, clients, requirements);
        var technical = new GetRequirementTechnicalProposalService(currentUser, identity, requirements, preQuotes, projects, clients, systems, glasses, finishes);
        var plan = new PlanRequirementChatActionService(
            new RequirementChatTechnicalProposalReader(technical),
            systems,
            glasses,
            finishes,
            store,
            clock);
        var service = new SendRequirementChatMessageService(
            currentUser,
            chat,
            ai,
            details,
            technical,
            requirements,
            plan,
            store,
            clock);

        return new Context(service, ai, store, requirement, proposal, clock);
    }

    private static RequirementTechnicalProposal CreateProposal(Requirement requirement, bool duplicateReference)
    {
        var extraction = RequirementExtractionResult.Create(Guid.NewGuid(), "AI2-1.0", "Ai2", "{}", 1, 0, 0, 0, "ai2", 100, At);
        var proposal = RequirementTechnicalProposal.Create(requirement.Id, extraction.Id, Guid.NewGuid(), false, At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        AddItem(proposal, extraction.Id, 1, "V-9");
        AddItem(proposal, extraction.Id, 2, duplicateReference ? "V-9" : "V-10");
        return proposal;
    }

    private static void AddItem(RequirementTechnicalProposal proposal, Guid extractionId, int sequence, string reference)
    {
        var extracted = RequirementExtractedItem.Create(
            extractionId,
            $"element-{sequence}",
            sequence,
            reference,
            "Puerta vidriera",
            StructuredElementType.Door,
            1,
            1000,
            2000,
            2m,
            0.9m,
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
            "RECTANGULAR",
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
        var item = RequirementTechnicalProposalItem.Create(
            proposal.Id,
            extracted.Id,
            SystemAId,
            GlassAId,
            FinishAId,
            sequence,
            reference,
            extracted.Description,
            extracted.ElementType,
            extracted.Quantity,
            extracted.WidthMillimeters,
            extracted.HeightMillimeters,
            0.9m,
            0.9m,
            0.9m,
            0.9m,
            false,
            true,
            true,
            [],
            [],
            [],
            [],
            1,
            0.9m,
            0.9m,
            "AVAILABLE",
            At);
        SetPrivateProperty(item, "ExtractedItem", extracted);
        proposal.AddItem(item);
    }

    private static ProductSystemCatalogReadModel System(string code, Guid id) =>
        new(id, code, $"Sistema {code}", $"Sistema tecnico {code}", code, "SLIDING_DOOR", code, "SERIE", "ESSENTIAL", "STANDARD", true, true, true, true, false, true);

    private static GlassTypeCatalogReadModel Glass(string code, Guid id) =>
        new(id, code, $"Cristal {code}", null, true, null, IsSelectable: true);

    private static FinishTypeCatalogReadModel Finish(string code, Guid id) =>
        new(id, code, $"Acabado {code}", "PAINTED", code, "MATTE", "PAINTED", null, "ALUMINUM", true, false, true);

    private static void SetPrivateProperty<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(target, value);
    }

    private sealed record Context(
        SendRequirementChatMessageService Service,
        IRequirementChatAiClient Ai,
        ObservablePlanStore Store,
        Requirement Requirement,
        RequirementTechnicalProposal Proposal,
        FixedTimeProvider Clock);

    private sealed class ObservablePlanStore(TimeProvider timeProvider)
        : IRequirementChatActionPlanStore
    {
        private readonly InMemoryRequirementChatActionPlanStore _inner = new(timeProvider);
        public List<ChatActionPlanReadModel> Plans { get; } = [];

        public void Save(ChatActionPlanReadModel plan)
        {
            _inner.Save(plan);
            Plans.RemoveAll(value => value.PlanId == plan.PlanId);
            Plans.Add(plan);
        }

        public ChatActionPlanReadModel? Find(Guid requirementId, Guid planId) =>
            _inner.Find(requirementId, planId);

        public ChatActionPlanReadModel? FindPendingClarification(
            Guid requirementId,
            string scope,
            Guid? technicalProposalItemId,
            Guid chatThreadId) =>
            _inner.FindPendingClarification(
                requirementId,
                scope,
                technicalProposalItemId,
                chatThreadId);

        public ChatActionPlanReadModel? StartExecution(Guid requirementId, Guid planId) =>
            _inner.StartExecution(requirementId, planId);
    }

    private sealed class FakeRequirementChatRepository : IRequirementChatRepository
    {
        private readonly List<RequirementChatThread> _threads = [];
        private readonly List<RequirementChatMessage> _messages = [];

        public Task<RequirementChatThread?> FindThreadAsync(Guid requirementId, RequirementChatScope scope, Guid? technicalProposalItemId, CancellationToken cancellationToken) =>
            Task.FromResult(_threads.SingleOrDefault(thread => thread.RequirementId == requirementId && thread.Scope == scope && thread.TechnicalProposalItemId == technicalProposalItemId));

        public Task<IReadOnlyList<RequirementChatMessage>> ListMessagesAsync(Guid chatThreadId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RequirementChatMessage>>(_messages.Where(message => message.ChatThreadId == chatThreadId).OrderBy(message => message.Sequence).ToArray());

        public Task<IReadOnlyList<RequirementChatMessage>> ListRecentMessagesAsync(Guid chatThreadId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RequirementChatMessage>>(_messages.Where(message => message.ChatThreadId == chatThreadId).OrderByDescending(message => message.Sequence).Take(limit).OrderBy(message => message.Sequence).ToArray());

        public Task<int> GetNextSequenceAsync(Guid chatThreadId, CancellationToken cancellationToken) =>
            Task.FromResult(_messages.Where(message => message.ChatThreadId == chatThreadId).Select(message => message.Sequence).DefaultIfEmpty(0).Max() + 1);

        public void AddThread(RequirementChatThread thread) => _threads.Add(thread);

        public void AddMessage(RequirementChatMessage message) => _messages.Add(message);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static string Serialize(object value) =>
        global::System.Text.Json.JsonSerializer.Serialize(
            value,
            global::System.Text.Json.JsonSerializerOptions.Web);

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;

        public override DateTimeOffset GetUtcNow() => _value;

        public void Advance(TimeSpan value) => _value += value;
    }
}
