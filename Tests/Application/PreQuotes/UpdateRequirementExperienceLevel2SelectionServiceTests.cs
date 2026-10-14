using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
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

public sealed class UpdateRequirementExperienceLevel2SelectionServiceTests
{
    [Fact]
    public async Task UpdateExisting_NullRevision_CannotBypassConflict()
    {
        var system = System("FIXED", "FIXED");
        var context = CreateContext(systems: [system]);
        context.StoreSelection(systemId: system.Id);
        var result = await context.UpdateService.UpdateSystemAsync(context.Proposal.Id, context.Item.Id,
            new(null, null), CancellationToken.None);
        Assert.Equal(RequirementExperienceFailure.Conflict, result.Error);
        Assert.Equal(0, context.SaveCount);
        Assert.Equal(1, context.StoredSelection!.Revision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_MapsOnlyConcurrencyFailuresToConflict(bool conflict)
    {
        var system = System("FIXED", "FIXED");
        var context = CreateContext(systems: [system]);
        Exception exception = conflict
            ? new RequirementExperienceConflictException(new Exception("race"))
            : new RequirementPersistenceException(new Exception("storage"));
        context.Requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(exception));
        var result = await context.UpdateService.UpdateSystemAsync(context.Proposal.Id, context.Item.Id,
            new(system.Id, null), CancellationToken.None);
        Assert.Equal(conflict ? RequirementExperienceFailure.Conflict
            : RequirementExperienceFailure.PersistenceError, result.Error);
    }

    [Theory]
    [InlineData(null, null, "LAM_4_4")]
    [InlineData(2000, null, "LAM_4_4")]
    [InlineData(null, 3000, "LAM_4_4")]
    [InlineData(2000, 3000, "LAM_5_5")]
    public async Task GetLevel2_LaminatedGlassUsesEffectiveDimensions(
        int? width, int? height, string expectedCode)
    {
        var context = CreateContext(
            glasses: [
                Glass("LAM_4_4", "LAMINATED", "LAMINATED"),
                Glass("LAM_5_5", "LAMINATED", "LAMINATED")],
            draft: LaminadoDraft);
        var original = (context.Item.ExtractedItem!.WidthMillimeters, context.Item.ExtractedItem.HeightMillimeters);
        context.Item.ApplyManualDataOverride(null, width, height);
        var result = await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedCode, result.Value!.Glass.Suggested!.Code);
        Assert.Equal(original, (context.Item.ExtractedItem.WidthMillimeters, context.Item.ExtractedItem.HeightMillimeters));
    }

    [Theory]
    [InlineData(false, UserRole.User, true)]
    [InlineData(false, UserRole.Admin, true)]
    [InlineData(false, UserRole.User, false)]
    [InlineData(false, UserRole.Admin, false)]
    [InlineData(true, UserRole.User, true)]
    [InlineData(true, UserRole.Admin, true)]
    [InlineData(true, UserRole.User, false)]
    [InlineData(true, UserRole.Admin, false)]
    public async Task Access_AdminMayReadButOnlyOwnerMaySelect(bool glass, UserRole role, bool owner)
    {
        var system = System("A_FIXED", "FIXED");
        var candidate = Glass("TEMP_5", "MONOLITHIC", "TEMPERED");
        var context = CreateContext(systems: [system], glasses: [candidate], draft: TempladoDraft);
        SetPrivateProperty(context.User, "Role", role);
        if (!owner)
            SetPrivateProperty(context.Proposal.Requirement.PreQuote.Project, "CreatedByUserId", Guid.NewGuid());
        var read = await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None);
        Assert.Equal(owner || role == UserRole.Admin, read.IsSuccess);
        var result = glass
            ? await context.UpdateService.UpdateGlassAsync(context.Proposal.Id, context.Item.Id,
                new(candidate.GlassTypeId, 0), CancellationToken.None)
            : await context.UpdateService.UpdateSystemAsync(context.Proposal.Id, context.Item.Id,
                new(system.Id, 0), CancellationToken.None);
        Assert.Equal(owner, result.IsSuccess);
        if (!owner)
        {
            Assert.Equal(RequirementExperienceFailure.Forbidden, result.Error);
            Assert.Equal(0, context.SaveCount);
        }
    }

    [Theory]
    [InlineData(null, null, "TEMP_5")]
    [InlineData(2000, null, "TEMP_10")]
    [InlineData(null, 2800, "TEMP_8")]
    [InlineData(400, 2800, "TEMP_6")]
    public async Task GetLevel2_GlassUsesEffectiveDimensionsWithoutChangingExtraction(
        int? width, int? height, string expectedCode)
    {
        var context = CreateContext(
            glasses: [
                Glass("TEMP_5", "MONOLITHIC", "TEMPERED"),
                Glass("TEMP_6", "MONOLITHIC", "TEMPERED"),
                Glass("TEMP_8", "MONOLITHIC", "TEMPERED"),
                Glass("TEMP_10", "MONOLITHIC", "TEMPERED")],
            draft: TempladoDraft);
        var extracted = context.Item.ExtractedItem!;
        var original = (extracted.WidthMillimeters, extracted.HeightMillimeters, extracted.Quantity);
        context.Item.ApplyManualDataOverride(null, width, height);
        var result = await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedCode, result.Value!.Glass.Suggested!.Code);
        Assert.Equal(original, (extracted.WidthMillimeters, extracted.HeightMillimeters, extracted.Quantity));
    }

    private static readonly DateTimeOffset At =
        new(2026, 10, 13, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateSystemAsync_WithCompatibleSystem_CreatesValidOverrideWithoutLegacySideEffects()
    {
        var compatibleSystem = System("A_FIXED", "FIXED");
        var context = CreateContext(systems: [compatibleSystem]);
        var initialCommercialRevision = context.Proposal.CommercialRevision;
        var initialSuggestedSystemId = context.Item.SuggestedSystemId;

        var result = await context.UpdateService.UpdateSystemAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2SystemRequest(compatibleSystem.Id, 0),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.StoredSelection);
        Assert.Equal(1, result.Value!.TechnicalSelectionRevision);
        Assert.Equal(compatibleSystem.Id, result.Value.System.Override!.Id);
        Assert.Equal(compatibleSystem.Id, result.Value.System.Effective!.Id);
        Assert.Equal(RequirementExperienceLevel2OverrideStates.Valid, result.Value.System.OverrideState);
        Assert.Equal(initialCommercialRevision, context.Proposal.CommercialRevision);
        Assert.Equal(initialSuggestedSystemId, context.Item.SuggestedSystemId);
        Assert.Null(context.Item.SelectedSystemId);
        Assert.Null(context.Item.SelectedGlassTypeId);
        Assert.Equal(1, context.SaveCount);
    }

    [Fact]
    public async Task UpdateSystemAsync_WithRequiresReviewSystem_AcceptsOverrideReview()
    {
        var reviewSystem = System("REVIEW_FIXED", "FIXED", requiresReview: true);
        var context = CreateContext(systems: [reviewSystem]);

        var result = await context.UpdateService.UpdateSystemAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2SystemRequest(reviewSystem.Id, 0),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(reviewSystem.Id, result.Value!.System.Override!.Id);
        Assert.Equal(reviewSystem.Id, result.Value.System.Effective!.Id);
        Assert.Equal(
            RequirementExperienceLevel2OverrideStates.RequiresReview,
            result.Value.System.OverrideState);
    }

    [Fact]
    public async Task UpdateSystemAsync_WithNonCandidateSystem_IsRejected()
    {
        var slidingDoorSystem = System("SLIDING", "SLIDING_DOOR");
        var context = CreateContext(systems: [slidingDoorSystem]);

        var result = await context.UpdateService.UpdateSystemAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2SystemRequest(slidingDoorSystem.Id, 0),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.CandidateNotAllowed, result.Error);
        Assert.Null(context.StoredSelection);
        Assert.Equal(0, context.SaveCount);
    }

    [Fact]
    public async Task UpdateSystemAsync_DoesNotRejectByRequirementCommercialLine()
    {
        var premiumSystem = System("PREMIUM_FIXED", "FIXED", commercialLine: "PREMIUM");
        var context = CreateContext(systems: [premiumSystem]);

        var result = await context.UpdateService.UpdateSystemAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2SystemRequest(premiumSystem.Id, 0),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(premiumSystem.Id, result.Value!.System.Override!.Id);
        Assert.Equal(RequirementExperienceLevel2OverrideStates.Valid, result.Value.System.OverrideState);
    }

    [Fact]
    public async Task UpdateSystemAsync_ClearSystem_RemovesOverrideAndIncrementsRevision()
    {
        var system = System("A_FIXED", "FIXED");
        var context = CreateContext(systems: [system]);
        context.StoreSelection(systemId: system.Id);

        var result = await context.UpdateService.UpdateSystemAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2SystemRequest(null, 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TechnicalSelectionRevision);
        Assert.Null(result.Value.System.Override);
        Assert.Equal(RequirementExperienceLevel2OverrideStates.None, result.Value.System.OverrideState);
        Assert.Equal(result.Value.System.Suggested?.Id, result.Value.System.Effective?.Id);
        Assert.Equal(1, context.SaveCount);
    }

    [Fact]
    public async Task UpdateSystemAsync_WithStaleExpectedRevision_ReturnsConflictWithoutSaving()
    {
        var system = System("A_FIXED", "FIXED");
        var context = CreateContext(systems: [system]);
        context.StoreSelection(systemId: system.Id);

        var result = await context.UpdateService.UpdateSystemAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2SystemRequest(null, 0),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.Conflict, result.Error);
        Assert.Equal(1, context.StoredSelection!.Revision);
        Assert.Equal(0, context.SaveCount);
    }

    [Fact]
    public async Task UpdateSystemAsync_NoOp_DoesNotIncrementRevisionOrSave()
    {
        var system = System("A_FIXED", "FIXED");
        var context = CreateContext(systems: [system]);
        context.StoreSelection(systemId: system.Id);

        var result = await context.UpdateService.UpdateSystemAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2SystemRequest(system.Id, 1),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.TechnicalSelectionRevision);
        Assert.Equal(0, context.SaveCount);
    }

    [Fact]
    public async Task UpdateGlassAsync_TempladoAcceptsTempAndRejectsLaminated()
    {
        var temp = Glass("TEMP_5", family: "MONOLITHIC", composition: "TEMPERED");
        var laminated = Glass("LAM_4_4", family: "LAMINATED", composition: "LAMINATED");
        var context = CreateContext(
            glasses: [temp, laminated],
            draft: TempladoDraft);

        var accepted = await context.UpdateService.UpdateGlassAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2GlassRequest(temp.GlassTypeId, 0),
            CancellationToken.None);
        var rejected = await context.UpdateService.UpdateGlassAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2GlassRequest(laminated.GlassTypeId, 1),
            CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Equal(temp.GlassTypeId, accepted.Value!.Glass.Override!.Id);
        Assert.Equal(RequirementExperienceLevel2OverrideStates.Valid, accepted.Value.Glass.OverrideState);
        Assert.False(rejected.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.CandidateNotAllowed, rejected.Error);
    }

    [Fact]
    public async Task UpdateGlassAsync_LaminadoAcceptsLaminatedAndRejectsTemp()
    {
        var temp = Glass("TEMP_5", family: "MONOLITHIC", composition: "TEMPERED");
        var laminated = Glass("LAM_4_4", family: "LAMINATED", composition: "LAMINATED");
        var context = CreateContext(
            glasses: [temp, laminated],
            draft: LaminadoDraft);

        var accepted = await context.UpdateService.UpdateGlassAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2GlassRequest(laminated.GlassTypeId, 0),
            CancellationToken.None);
        var rejected = await context.UpdateService.UpdateGlassAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2GlassRequest(temp.GlassTypeId, 1),
            CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Equal(laminated.GlassTypeId, accepted.Value!.Glass.Override!.Id);
        Assert.False(rejected.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.CandidateNotAllowed, rejected.Error);
    }

    [Fact]
    public async Task UpdateGlassAsync_WithRequiresReviewGlass_AcceptsOverrideReview()
    {
        var reviewGlass = Glass(
            "TEMP_REVIEW",
            family: "MONOLITHIC",
            composition: "TEMPERED",
            requiresReview: true);
        var context = CreateContext(
            glasses: [reviewGlass],
            draft: TempladoDraft);

        var result = await context.UpdateService.UpdateGlassAsync(
            context.Proposal.Id,
            context.Item.Id,
            new UpdateRequirementExperienceLevel2GlassRequest(reviewGlass.GlassTypeId, 0),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(reviewGlass.GlassTypeId, result.Value!.Glass.Override!.Id);
        Assert.Equal(
            RequirementExperienceLevel2OverrideStates.RequiresReview,
            result.Value.Glass.OverrideState);
    }

    [Fact]
    public async Task GetLevel2_WithPersistedTempladoOverrideAndLaminadoLevel1_ReturnsStaleOverride()
    {
        var temp = Glass("TEMP_5", family: "MONOLITHIC", composition: "TEMPERED");
        var laminated = Glass("LAM_4_4", family: "LAMINATED", composition: "LAMINATED");
        var context = CreateContext(
            glasses: [temp, laminated],
            draft: LaminadoDraft);
        context.StoreSelection(glassTypeId: temp.GlassTypeId);

        var result = await context.GetService.ExecuteAsync(
            context.Proposal.Id,
            context.Item.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequirementExperienceLevel2OverrideStates.Stale, result.Value!.Glass.OverrideState);
        Assert.Equal(temp.GlassTypeId, result.Value.Glass.Override!.Id);
        Assert.Equal(laminated.GlassTypeId, result.Value.Glass.Effective!.Id);
    }

    private static TestContext CreateContext(
        IReadOnlyList<ProductSystemCatalogReadModel>? systems = null,
        IReadOnlyList<GlassTypeCatalogReadModel>? glasses = null,
        Func<TestContext, RequirementItemExperienceDraft>? draft = null)
    {
        var requirements = Substitute.For<IRequirementRepository>();
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var productSystems = Substitute.For<IProductSystemCatalogRepository>();
        var glassTypes = Substitute.For<IGlassTypeCatalogRepository>();
        var user = User.CreateFromGoogle(
            "user@example.com",
            "User",
            "Test",
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
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            extractionId,
            Guid.NewGuid(),
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var extracted = CreateExtractedItem(extractionId);
        var item = CreateProposalItem(proposal.Id, extracted);
        SetPrivateProperty(item, "ExtractedItem", extracted);
        proposal.AddItem(item);

        var context = new TestContext(
            requirements,
            currentUser,
            identity,
            productSystems,
            glassTypes,
            proposal,
            item,
            user);
        context.StoredDraft = draft?.Invoke(context);

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(user.Id);
        identity.FindUserByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindTechnicalProposalForUpdateAsync(
                proposal.Id,
                Arg.Any<CancellationToken>())
            .Returns(proposal);
        requirements.ListExperienceDraftsByTechnicalProposalIdAsync(
                proposal.Id,
                Arg.Any<CancellationToken>())
            .Returns(_ => context.StoredDraft is null ? [] : [context.StoredDraft]);
        requirements.FindExperienceTechnicalSelectionForUpdateAsync(
                item.Id,
                Arg.Any<CancellationToken>())
            .Returns(_ => context.StoredSelection);
        requirements.When(repository => repository.AddExperienceTechnicalSelection(
                Arg.Any<RequirementItemExperienceTechnicalSelection>()))
            .Do(call => context.StoredSelection = call.Arg<RequirementItemExperienceTechnicalSelection>());
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                context.SaveCount++;
                return Task.CompletedTask;
            });
        productSystems.ListActiveSelectableAsync(Arg.Any<CancellationToken>())
            .Returns(systems ?? [System("A_FIXED", "FIXED")]);
        glassTypes.GetActiveWithCurrentPriceRangesAsync(Arg.Any<CancellationToken>())
            .Returns(glasses ?? [Glass("TEMP_5", "MONOLITHIC", "TEMPERED")]);

        var resolver = new RequirementExperienceLevel2Resolver(
            new SgProductSystemConstraintEvaluator(new FixedTimeProvider(At)),
            new GlassCandidateResolver(),
            new DeterministicSgTechnicalSelector(productSystems));
        context.UpdateService = new UpdateRequirementExperienceLevel2SelectionService(
            requirements,
            identity,
            currentUser,
            productSystems,
            glassTypes,
            resolver,
            new FixedTimeProvider(At));
        context.GetService = new GetRequirementExperienceLevel2Service(
            requirements,
            identity,
            currentUser,
            productSystems,
            glassTypes,
            resolver);

        return context;
    }

    private static RequirementItemExperienceDraft TempladoDraft(TestContext context) =>
        RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.Item.Id,
            "sng-experience-v3-001",
            null,
            [
                new("THERMAL", "THERMAL_3"),
                new("ACOUSTIC", "ACOUSTIC_3"),
                new("SECURITY", "SECURITY_2"),
                new("UV", "UV_NO"),
                new("AESTHETICS", "AESTHETICS_3")
            ],
            context.User.Id,
            At);

    private static RequirementItemExperienceDraft LaminadoDraft(TestContext context) =>
        RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.Item.Id,
            "sng-experience-v4-001",
            null,
            [
                new("THERMAL", "THERMAL_LOW"),
                new("ACOUSTIC", "ACOUSTIC_LOW"),
                new("SECURITY", "SECURITY_LOW"),
                new("UV", "UV_YES"),
                new("AESTHETICS", "AESTHETICS_LOW")
            ],
            context.User.Id,
            At);

    private static ProductSystemCatalogReadModel System(
        string code,
        string? functionalType,
        bool requiresReview = false,
        string? commercialLine = null) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            code,
            code,
            functionalType,
            "FAMILY",
            "SERIES",
            commercialLine,
            "STANDARD",
            IsSelectable: true,
            ActiveForRecognition: true,
            Priceable: true,
            FuturePriceable: false,
            requiresReview,
            IsActive: true,
            Constraints: []);

    private static GlassTypeCatalogReadModel Glass(
        string code,
        string family,
        string composition,
        bool requiresReview = false) =>
        new(
            Guid.NewGuid(),
            code,
            code,
            null,
            IsActive: true,
            CurrentPriceRange: null,
            Family: family,
            Composition: composition,
            Treatment: null,
            OuterThicknessMm: 5m,
            InnerThicknessMm: null,
            PvbThicknessMm: null,
            PvbType: null,
            PvbColor: null,
            ChamberThicknessMm: null,
            ProductLine: null,
            ProductToken: null,
            Pattern: null,
            Color: null,
            IsSelectable: true,
            RequiresReview: requiresReview);

    private static RequirementExtractedItem CreateExtractedItem(Guid extractionId)
    {
        return RequirementExtractedItem.Create(
            requirementExtractionResultId: extractionId,
            ai2ElementId: "el-1",
            sequence: 1,
            reference: "V-01",
            description: "Item V-01",
            elementType: StructuredElementType.Window,
            quantity: 1,
            widthMillimeters: 1000,
            heightMillimeters: 2400,
            areaSquareMeters: 2.4m,
            confidence: 0.9m,
            extractionStatus: RequirementExtractionValueStatus.Explicit,
            requiresReview: false,
            reviewReasons: [],
            functionalType: "FIXED",
            operation: null,
            panelCount: 1,
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
        IProductSystemCatalogRepository productSystems,
        IGlassTypeCatalogRepository glassTypes,
        RequirementTechnicalProposal proposal,
        RequirementTechnicalProposalItem item,
        User user)
    {
        public IRequirementRepository Requirements { get; } = requirements;

        public ICurrentUser CurrentUser { get; } = currentUser;

        public IIdentityRepository Identity { get; } = identity;

        public IProductSystemCatalogRepository ProductSystems { get; } = productSystems;

        public IGlassTypeCatalogRepository GlassTypes { get; } = glassTypes;

        public RequirementTechnicalProposal Proposal { get; } = proposal;

        public RequirementTechnicalProposalItem Item { get; } = item;

        public User User { get; } = user;

        public RequirementItemExperienceDraft? StoredDraft { get; set; }

        public RequirementItemExperienceTechnicalSelection? StoredSelection { get; set; }

        public int SaveCount { get; set; }

        public UpdateRequirementExperienceLevel2SelectionService UpdateService { get; set; } = null!;

        public GetRequirementExperienceLevel2Service GetService { get; set; } = null!;

        public void StoreSelection(Guid? systemId = null, Guid? glassTypeId = null)
        {
            StoredSelection = RequirementItemExperienceTechnicalSelection.Create(
                Proposal.Id,
                Item.Id,
                systemId,
                glassTypeId,
                User.Id,
                At);
        }
    }
}
