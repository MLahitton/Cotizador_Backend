using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.RequirementExperience;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using NSubstitute;
using CotizadorBackend.Tests.TestDoubles;
using Xunit;

namespace Tests.Application.PreQuotes;

public sealed class RequirementExperienceServicesTests
{
    private static readonly DateTimeOffset At =
        new(2026, 09, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Catalog_ContainsExpectedQuestionsOptionsSpacesAndPriorities()
    {
        var service = new GetRequirementExperienceCatalogService(
            new RequirementExperienceCatalogProvider());

        var catalog = service.Execute();

        Assert.Equal("sng-experience-v2-draft-001", catalog.Version);
        Assert.Equal(11, catalog.Questions.Count);
        Assert.Equal(36, catalog.Questions.Sum(question => question.Options.Count));
        Assert.Equal(36, catalog.Questions
            .SelectMany(question => question.Options)
            .Select(option => option.OptionCode)
            .Distinct(StringComparer.Ordinal)
            .Count());
        Assert.Equal(12, catalog.Spaces.Count);
        Assert.DoesNotContain(catalog.Questions, question =>
            question.BenefitCode == "B10"
            && question.Options.Any(option => option.OptionCode.StartsWith("INS_", StringComparison.Ordinal)));
        Assert.Contains(catalog.Questions, question =>
            question.BenefitCode == "B10"
            && question.Options.Any(option => option.OptionCode == "MOS_0")
            && question.Options.Any(option => option.OptionCode == "MOS_1"));
        Assert.Contains(catalog.Questions, question =>
            question.BenefitCode == "B11"
            && question.Options.Any(option =>
                option.OptionCode == "ACA_3"
                && option.OptionLabel == "Acabado protagonista"
                && option.ShortLabel == "Protagonista"));
        Assert.All(catalog.Spaces, space =>
        {
            Assert.Equal(11, space.Priorities.Count);
            Assert.All(space.Priorities.Values, priority => Assert.InRange(priority, 0, 3));
        });
    }

    [Fact]
    public async Task UpdateAsync_WithValidPartialDraft_SavesAndReturnsDraft()
    {
        var context = CreateContext();
        var request = new UpdateRequirementExperienceDraftRequest(
            "sng-experience-v2-draft-001",
            "ESP_ALC_PPAL",
            0,
            [
                new("B01", "VIS_2"),
                new("B11", "ACA_3")
            ]);

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            request,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.StoredDraft);
        Assert.Equal(1, result.Value!.Revision);
        Assert.Equal("ESP_ALC_PPAL", result.Value.SpaceTypeCode);
        Assert.Equal(["B01", "B11"], result.Value.Answers.Select(answer => answer.BenefitCode).ToArray());
        Assert.Equal(["VIS_2", "ACA_3"], result.Value.Answers.Select(answer => answer.OptionCode).ToArray());
        Assert.Equal(1, context.SaveCount);
    }

    [Fact]
    public async Task GetAsync_WithUnansweredItems_ReturnsEmptyDraftsWithoutDefaults()
    {
        var context = CreateContext();

        var result = await context.GetService.ExecuteAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, item =>
        {
            Assert.Null(item.CatalogVersion);
            Assert.Null(item.SpaceTypeCode);
            Assert.Equal(0, item.Revision);
            Assert.Empty(item.Answers);
        });
    }

    [Fact]
    public async Task UpdateAsync_ReplacingDraftDeletesRemovedAnswers()
    {
        var context = CreateContext();
        context.StoredDraft = RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.FirstItem.Id,
            "sng-experience-v2-draft-001",
            "ESP_ALC_PPAL",
            [
                new("B01", "VIS_2"),
                new("B11", "ACA_3")
            ],
            context.User.Id,
            At);

        var request = new UpdateRequirementExperienceDraftRequest(
            "sng-experience-v2-draft-001",
            "ESP_ALC_PPAL",
            1,
            [
                new("B01", "VIS_3")
            ]);

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            request,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Revision);
        var answer = Assert.Single(result.Value.Answers);
        Assert.Equal("B01", answer.BenefitCode);
        Assert.Equal("VIS_3", answer.OptionCode);
    }

    [Fact]
    public async Task UpdateAsync_TwoItemsRemainIndependent()
    {
        var context = CreateContext();

        var first = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_ALC_PPAL",
                0,
                [new("B01", "VIS_2")]),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        var firstDraft = context.StoredDraft;

        context.StoredDraft = null;
        var second = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.SecondItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_SALA",
                0,
                [new("B02", "ACU_3")]),
            CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.NotEqual(firstDraft!.TechnicalProposalItemId, context.StoredDraft!.TechnicalProposalItemId);
        Assert.Equal(context.SecondItem.Id, context.StoredDraft.TechnicalProposalItemId);
    }

    [Fact]
    public async Task UpdateAsync_WithWrongOptionBenefit_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_ALC_PPAL",
                0,
                [new("B01", "ACU_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.OptionDoesNotBelongToBenefit, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task UpdateAsync_WithUnknownVersion_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "unknown",
                "ESP_ALC_PPAL",
                0,
                [new("B01", "VIS_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.UnknownCatalogVersion, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_WithDuplicateBenefit_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_ALC_PPAL",
                0,
                [
                    new("B01", "VIS_1"),
                    new("B01", "VIS_2")
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.DuplicateBenefit, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_WithUnknownSpace_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_UNKNOWN",
                0,
                [new("B01", "VIS_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.UnknownSpaceType, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_WithItemFromOtherProposal_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            Guid.NewGuid(),
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_ALC_PPAL",
                0,
                [new("B01", "VIS_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.ItemNotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_WithProposalFromAnotherUser_IsRejected()
    {
        var context = CreateContext();
        var otherUser = User.CreateFromGoogle(
            "other@example.com",
            "Other",
            null,
            null,
            At);
        context.CurrentUser.UserId.Returns(otherUser.Id);
        context.Identity.FindUserByIdAsync(otherUser.Id, Arg.Any<CancellationToken>())
            .Returns(otherUser);

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_ALC_PPAL",
                0,
                [new("B01", "VIS_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_WithStaleRevision_ReturnsConflictAndPreservesLatest()
    {
        var context = CreateContext();
        context.StoredDraft = RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.FirstItem.Id,
            "sng-experience-v2-draft-001",
            "ESP_ALC_PPAL",
            [new("B01", "VIS_1")],
            context.User.Id,
            At);

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_SALA",
                0,
                [new("B01", "VIS_3")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.Conflict, result.Error);
        Assert.Equal("ESP_ALC_PPAL", context.StoredDraft.SpaceTypeCode);
        Assert.Equal("VIS_1", Assert.Single(context.StoredDraft.Answers).OptionCode);
    }

    [Fact]
    public async Task UpdateAsync_WithSameDraftAndCurrentRevision_DoesNotIncrementRevisionOrTimestamp()
    {
        var context = CreateContext();
        context.StoredDraft = RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.FirstItem.Id,
            "sng-experience-v2-draft-001",
            "ESP_ALC_PPAL",
            [new("B01", "VIS_1")],
            context.User.Id,
            At);
        context.UpdateService = new UpdateRequirementExperienceDraftService(
            context.Requirements,
            new RequirementExperienceCatalogProvider(),
            context.Identity,
            context.CurrentUser,
            new FixedTimeProvider(At.AddHours(1)));

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_ALC_PPAL",
                1,
                [new("B01", "VIS_1")]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Revision);
        Assert.Equal(At, result.Value.UpdatedAtUtc);
        Assert.Equal(At, context.StoredDraft.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotModifyTechnicalSelectionOrMeasurements()
    {
        var context = CreateContext();
        var quantity = context.FirstItem.EffectiveQuantity;
        var width = context.FirstItem.EffectiveWidthMillimeters;
        var height = context.FirstItem.EffectiveHeightMillimeters;
        var selectedSystem = context.FirstItem.SelectedSystemId;
        var suggestedSystem = context.FirstItem.SuggestedSystemId;
        var commercialRevision = context.Proposal.CommercialRevision;
        var commercialConfirmation = context.Proposal.CommercialConfirmationState;
        var commercialConfirmedAt = context.Proposal.CommercialConfirmedAtUtc;

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v2-draft-001",
                "ESP_ALC_PPAL",
                0,
                [new("B01", "VIS_2")]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(quantity, context.FirstItem.EffectiveQuantity);
        Assert.Equal(width, context.FirstItem.EffectiveWidthMillimeters);
        Assert.Equal(height, context.FirstItem.EffectiveHeightMillimeters);
        Assert.Equal(selectedSystem, context.FirstItem.SelectedSystemId);
        Assert.Equal(suggestedSystem, context.FirstItem.SuggestedSystemId);
        Assert.Equal(commercialRevision, context.Proposal.CommercialRevision);
        Assert.Equal(commercialConfirmation, context.Proposal.CommercialConfirmationState);
        Assert.Equal(commercialConfirmedAt, context.Proposal.CommercialConfirmedAtUtc);
    }

    private static TestContext CreateContext()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var requirements = Substitute.For<IRequirementRepository>();
        var user = User.CreateFromGoogle(
            "owner@example.com",
            "Owner",
            null,
            null,
            At);
        var client = Client.Create(
            ClientType.Company,
            "Cliente",
            null,
            null,
            null,
            null,
            null,
            null,
            "Bogota",
            user.Id,
            At);
        var project = Project.Create(
            client.Id,
            "P-001",
            "Proyecto",
            null,
            "Bogota",
            user.Id,
            At);
        var preQuote = PreQuote.Create(
            project.Id,
            user.Id,
            "SG-001",
            "Prequote",
            At);
        SetPrivateProperty(preQuote, "Project", project);
        var requirement = Requirement.Create(
            preQuote.Id,
            user.Id,
            RequirementCommercialLine.Essential,
            At);
        SetPrivateProperty(requirement, "PreQuote", preQuote);
        var extractionId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            extractionId,
            attemptId,
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var firstExtracted = CreateExtractedItem(extractionId, 1, "V-01");
        var secondExtracted = CreateExtractedItem(extractionId, 2, "V-02");
        var firstItem = CreateProposalItem(proposal.Id, firstExtracted);
        var secondItem = CreateProposalItem(proposal.Id, secondExtracted);
        SetPrivateProperty(firstItem, "ExtractedItem", firstExtracted);
        SetPrivateProperty(secondItem, "ExtractedItem", secondExtracted);
        proposal.AddItem(firstItem);
        proposal.AddItem(secondItem);

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(user.Id);
        identity.FindUserByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindTechnicalProposalForUpdateAsync(
                proposal.Id,
                Arg.Any<CancellationToken>())
            .Returns(proposal);

        var context = new TestContext(
            requirements,
            currentUser,
            identity,
            proposal,
            firstItem,
            secondItem,
            user);
        requirements.ListExperienceDraftsByTechnicalProposalIdAsync(
                proposal.Id,
                Arg.Any<CancellationToken>())
            .Returns(_ => context.StoredDraft is null ? [] : [context.StoredDraft]);
        requirements.FindExperienceDraftForUpdateAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var itemId = call.ArgAt<Guid>(0);
                return context.StoredDraft?.TechnicalProposalItemId == itemId
                    ? context.StoredDraft
                    : null;
            });
        requirements.When(repository => repository.AddExperienceDraft(
                Arg.Any<RequirementItemExperienceDraft>()))
            .Do(call => context.StoredDraft = call.Arg<RequirementItemExperienceDraft>());
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                context.SaveCount++;
                return Task.CompletedTask;
            });

        context.GetService = new GetRequirementExperienceDraftsService(
            requirements,
            identity,
            currentUser);
        context.UpdateService = new UpdateRequirementExperienceDraftService(
            requirements,
            new RequirementExperienceCatalogProvider(),
            identity,
            currentUser,
            new FixedTimeProvider(At));

        return context;
    }

    private static RequirementExtractedItem CreateExtractedItem(
        Guid extractionId,
        int sequence,
        string reference)
    {
        return RequirementExtractedItem.Create(
            requirementExtractionResultId: extractionId,
            ai2ElementId: $"el-{sequence}",
            sequence: sequence,
            reference: reference,
            description: $"Item {reference}",
            elementType: StructuredElementType.Window,
            quantity: 1,
            widthMillimeters: 1000,
            heightMillimeters: 1200,
            areaSquareMeters: 1.2m,
            confidence: 0.9m,
            extractionStatus: RequirementExtractionValueStatus.Explicit,
            requiresReview: false,
            reviewReasons: [],
            functionalType: "FIXED",
            operation: null,
            panelCount: null,
            movablePanelCount: null,
            fixedPanelCount: null,
            arrangement: null,
            modulation: null,
            openingDirection: null,
            specialFeatures: [],
            geometryType: null,
            requestedSystemRaw: null,
            requestedProfileRaw: null,
            glassRawSpecification: null,
            glassTypeRaw: null,
            glassTypeNormalized: null,
            glassThicknessMm: null,
            glassColorRaw: null,
            glassColorNormalized: null,
            glassTreatmentRaw: null,
            glassTreatmentNormalized: null,
            glassComposition: null,
            glassCoating: null,
            glassTransparency: null,
            glassRequiresReview: false,
            finishRawDescription: null,
            finishNormalizedType: null,
            finishColorRaw: null,
            finishColorNormalized: null,
            finishTextureRaw: null,
            finishTextureNormalized: null,
            finishExplicitCode: null,
            finishRequiresReview: false,
            createdAtUtc: At);
    }

    private static RequirementTechnicalProposalItem CreateProposalItem(
        Guid proposalId,
        RequirementExtractedItem extracted)
    {
        return RequirementTechnicalProposalItem.Create(
            proposalId,
            extracted.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            extracted.Sequence,
            extracted.Reference,
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
            0,
            null,
            null,
            "NotEvaluated",
            At);
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

    private sealed class TestContext(
        IRequirementRepository requirements,
        ICurrentUser currentUser,
        IIdentityRepository identity,
        RequirementTechnicalProposal proposal,
        RequirementTechnicalProposalItem firstItem,
        RequirementTechnicalProposalItem secondItem,
        User user)
    {
        public IRequirementRepository Requirements { get; } = requirements;

        public ICurrentUser CurrentUser { get; } = currentUser;

        public IIdentityRepository Identity { get; } = identity;

        public RequirementTechnicalProposal Proposal { get; } = proposal;

        public RequirementTechnicalProposalItem FirstItem { get; } = firstItem;

        public RequirementTechnicalProposalItem SecondItem { get; } = secondItem;

        public User User { get; } = user;

        public RequirementItemExperienceDraft? StoredDraft { get; set; }

        public int SaveCount { get; set; }

        public GetRequirementExperienceDraftsService GetService { get; set; } = null!;

        public UpdateRequirementExperienceDraftService UpdateService { get; set; } = null!;
    }
}
