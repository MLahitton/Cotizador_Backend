using System.Reflection;
using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.RequirementExperience;
using Domain.Clients;
using Domain.Catalogs;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using NSubstitute;
using CotizadorBackend.Tests.TestDoubles;
using Xunit;

namespace Tests.Application.PreQuotes;

public sealed class UpdateRequirementExperienceLevel2SelectionServiceTests
{
    [Theory]
    [InlineData("VENTANA CORREDIZA LINEA CLASSIC SISTEMA SG", null, "SIGNATURE", "CLASSIC")]
    [InlineData("PUERTA CORREDIZA LINEA PREMIUM SISTEMA VENECIA", null, "SIGNATURE", "PREMIUM")]
    [InlineData("VENTANA CORREDIZA TRADICIONAL SISTEMA SG", null, null, "CLASSIC")]
    [InlineData("CUERPO FIJO TRADICIONAL SG", null, null, "CLASSIC")]
    [InlineData("Sin evidencia", null, "TRADITIONAL", "UNKNOWN")]
    [InlineData("FACHADA STICK INOX BARANDA SPECIAL", null, "SPECIAL", "UNKNOWN")]
    [InlineData("Sin evidencia", "sistema tradicional SG", null, "CLASSIC")]
    [InlineData("TRADICIONAL", "LINEA PREMIUM", null, "CLASSIC")]
    [InlineData("LINEA PREMIUM TRADICIONAL", null, null, "PREMIUM")]
    [InlineData("TRADICIONALES", null, null, "UNKNOWN")]
    [InlineData("Sin evidencia", null, "PREMIUM", "UNKNOWN")]
    [InlineData("K70", null, "CLASSIC", "UNKNOWN")]
    [InlineData("Sin evidencia", "LINEA PREMIUM", null, "PREMIUM")]
    [InlineData("l\u00ednea  classic", "LINEA PREMIUM", null, "CLASSIC")]
    [InlineData("LINEA PREMIUMPLUS", null, null, "UNKNOWN")]
    [InlineData("LINEA PREMIUM LINEA CLASSIC", null, null, "PREMIUM")]
    public void ExperienceTier_UsesOnlyExplicitCanonicalNames(string name, string? technicalName,
        string? commercialLine, string expected)
    {
        var system = System("ANY", "FIXED", commercialLine: commercialLine)
            with { Name = name, TechnicalName = technicalName };
        Assert.Equal(expected, ProductSystemExperienceTierResolver.Resolve(system));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task GetLevel2_IncompletePreferences_GatesBothResults(int count)
    {
        var context = CreateContext();
        context.StoredDraft = count == 0 ? null : PreferencesDraft(context, false, count);
        var result = await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("PENDING", result.Value!.System.State);
        Assert.Equal("PENDING", result.Value.Glass.State);
        Assert.Null(result.Value.System.Suggested);
        Assert.Null(result.Value.System.Effective);
        Assert.Null(result.Value.Glass.Suggested);
        Assert.Null(result.Value.Glass.Effective);
        Assert.Empty(result.Value.System.Candidates);
        Assert.Empty(result.Value.Glass.Candidates);
        Assert.Contains(RequirementExperienceLevel2Resolver.PreferencesIncomplete, result.Value.System.BlockingReasons);
        Assert.Equal(0, context.SaveCount);
    }

    [Fact]
    public async Task GetLevel2_ChangedPreferences_ReevaluatesTierFamilyAndOverrides()
    {
        var classic = System("CLASSIC_FIXED", "FIXED", commercialLine: "SIGNATURE");
        var premium = System("PREMIUM_FIXED", "FIXED", commercialLine: "CLASSIC")
            with { Name = "LINEA PREMIUM FIJO" };
        var unknown = System("UNKNOWN_FIXED", "FIXED", commercialLine: "PREMIUM")
            with { Name = "Sin evidencia", TechnicalName = null };
        var temp = Glass("TEMP_5", "MONOLITHIC", "TEMPERED");
        var lam = Glass("LAM_4_4", "LAMINATED", "LAMINATED");
        var context = CreateContext(systems: [classic, premium, unknown], glasses: [temp, lam]);
        context.StoreSelection(systemId: classic.Id, glassTypeId: temp.GlassTypeId);

        var before = (await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None)).Value!;
        Assert.Equal(classic.Id, Assert.Single(before.System.Candidates).Id);
        Assert.Equal(temp.GlassTypeId, before.Glass.Suggested!.Id);
        Assert.Equal("VALID", before.System.OverrideState);

        context.StoredDraft = PreferencesDraft(context, true);
        var after = (await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None)).Value!;
        Assert.Equal(premium.Id, Assert.Single(after.System.Candidates).Id);
        Assert.Equal(premium.Id, after.System.Suggested!.Id);
        Assert.Equal(premium.Id, after.System.Effective!.Id);
        Assert.Equal(lam.GlassTypeId, after.Glass.Suggested!.Id);
        Assert.Equal("STALE", after.System.OverrideState);
        Assert.Equal("STALE", after.Glass.OverrideState);
        Assert.Equal(classic.Id, context.StoredSelection!.SystemId);
        Assert.Equal(temp.GlassTypeId, context.StoredSelection.GlassTypeId);
        Assert.Equal(0, context.SaveCount);
        await context.ProductSystems.Received(2).ListActiveSelectableAsync(Arg.Any<CancellationToken>());

        var repeated = (await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None)).Value!;
        Assert.Equal(after.System.Suggested.Id, repeated.System.Suggested!.Id);
        Assert.NotSame(after.System, repeated.System);
    }

    [Fact]
    public async Task GetLevel2_PremiumDoesNotBypassHardConstraints()
    {
        var premium = System("PREMIUM_FIXED", "FIXED") with
        {
            Name = "LINEA PREMIUM FIJO",
            Constraints = [new(Guid.NewGuid(), Guid.NewGuid(), "MAX_WIDTH",
                ProductSystemConstraintType.MaxWidth,
                ProductSystemConstraintScope.Opening,
                ConstraintEvaluationStage.PreSelection,
                ProductSystemConstraintSeverity.Hard,
                ProductSystemConstraintKnowledgeClass.VerifiedTechnical,
                null, 100m, null, [], null, false, true, null, null,
                ProductSystemConstraintSourceType.SgRule, null, null)]
        };
        var context = CreateContext(systems: [premium]);
        context.StoredDraft = PreferencesDraft(context, true);
        var result = (await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None)).Value!;
        Assert.Null(result.System.Suggested);
        Assert.Equal("INCOMPATIBLE", Assert.Single(result.System.Candidates).CompatibilityState);
    }

    private static RequirementItemExperienceDraft PreferencesDraft(TestContext context, bool premium, int count = 5)
    {
        var level = premium ? "HIGH" : "LOW";
        return RequirementItemExperienceDraft.Create(context.Proposal.Id, context.Item.Id,
            "sng-experience-v4-001", null,
            new[] { "THERMAL", "ACOUSTIC", "SECURITY", "AESTHETICS", "UV" }
                .Take(count).Select(code => new RequirementItemExperienceAnswerValue(code, code == "UV" ? "UV_NO" : $"{code}_{level}")).ToArray(),
            context.User.Id, At);
    }

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

    [Fact]
    public async Task UpdateSystem_TraditionalHardFailureIsRejected()
    {
        var system = System("TRAD_FIXED", "FIXED", commercialLine: "TRADITIONAL") with
        {
            Name = "CUERPO FIJO TRADICIONAL SG",
            Constraints = [new(Guid.NewGuid(), Guid.NewGuid(), "MAX_WIDTH",
                ProductSystemConstraintType.MaxWidth, ProductSystemConstraintScope.Opening,
                ConstraintEvaluationStage.PreSelection, ProductSystemConstraintSeverity.Hard,
                ProductSystemConstraintKnowledgeClass.VerifiedTechnical,
                null, 100m, null, [], null, false, true, null, null,
                ProductSystemConstraintSourceType.SgRule, null, null)]
        };
        var context = CreateContext(systems: [system]);
        var result = await context.UpdateService.UpdateSystemAsync(context.Proposal.Id, context.Item.Id,
            new(system.Id, 0), CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.CandidateNotAllowed, result.Error);
        Assert.Null(context.StoredSelection);
        Assert.Equal(0, context.SaveCount);
    }

    [Theory]
    [InlineData("STANDARD", false)]
    [InlineData("POCKET", true)]
    public async Task UpdateSystem_PocketUsesTechnicalEligibility(string variant, bool allowed)
    {
        var system = System("TRAD_DOOR", "SLIDING_DOOR", commercialLine: "TRADITIONAL") with
        {
            Name = "PUERTA CORREDIZA TRADICIONAL", Variant = variant
        };
        var context = CreateContext(systems: [system], functionalType: "SLIDING_DOOR", features: ["POCKET"]);
        var before = (await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None)).Value!;
        var candidate = Assert.Single(before.System.Candidates);
        Assert.Equal(allowed ? "COMPATIBLE" : "INCOMPATIBLE", candidate.CompatibilityState);
        var result = await context.UpdateService.UpdateSystemAsync(context.Proposal.Id, context.Item.Id,
            new(system.Id, 0), CancellationToken.None);
        Assert.Equal(allowed, result.IsSuccess);
        if (allowed)
        {
            Assert.Equal("VALID", result.Value!.System.OverrideState);
            Assert.Equal(system.Id, result.Value.System.Effective!.Id);
        }
        else
        {
            Assert.Equal(RequirementExperienceFailure.CandidateNotAllowed, result.Error);
            Assert.Null(context.StoredSelection);
            Assert.Equal(0, context.SaveCount);
        }
    }

    [Fact]
    public async Task GetLevel2_PersistedStandardUnderPocketIsStaleWithoutDeletingSelection()
    {
        var system = System("TRAD_DOOR", "SLIDING_DOOR", commercialLine: "TRADITIONAL") with
        { Name = "PUERTA TRADICIONAL" };
        var context = CreateContext(systems: [system], functionalType: "SLIDING_DOOR", features: ["POCKET"]);
        context.StoreSelection(systemId: system.Id);
        var result = (await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None)).Value!;
        Assert.Equal("STALE", result.System.OverrideState);
        Assert.Null(result.System.Effective);
        Assert.Null(result.System.Suggested);
        Assert.Equal(system.Id, result.System.Override!.Id);
        Assert.Equal(system.Id, context.StoredSelection!.SystemId);
        Assert.Equal(0, context.SaveCount);
    }

    [Fact]
    public async Task UpdateSystem_CompatibleNonSuggestedTraditionalAlternativeIsAllowed()
    {
        var preferred = System("PREFERRED", "FIXED") with { Family = "VENECIA FERMO" };
        var alternative = System("ALTERNATIVE", "FIXED", commercialLine: "TRADITIONAL") with
        { Name = "CUERPO FIJO TRADICIONAL SG" };
        var context = CreateContext(systems: [preferred, alternative]);
        var before = (await context.GetService.ExecuteAsync(context.Proposal.Id, context.Item.Id, CancellationToken.None)).Value!;
        Assert.Equal(preferred.Id, before.System.Suggested!.Id);
        var result = await context.UpdateService.UpdateSystemAsync(context.Proposal.Id, context.Item.Id,
            new(alternative.Id, 0), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("VALID", result.Value!.System.OverrideState);
        Assert.Equal(preferred.Id, result.Value.System.Suggested!.Id);
        Assert.Equal(alternative.Id, result.Value.System.Effective!.Id);
    }

    private static TestContext CreateContext(
        IReadOnlyList<ProductSystemCatalogReadModel>? systems = null,
        IReadOnlyList<GlassTypeCatalogReadModel>? glasses = null,
        Func<TestContext, RequirementItemExperienceDraft>? draft = null,
        string functionalType = "FIXED",
        IReadOnlyList<string>? features = null)
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
        var extracted = CreateExtractedItem(extractionId, functionalType, features);
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
        context.StoredDraft = (draft ?? TempladoDraft).Invoke(context);

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
            "LINEA CLASSIC " + code,
            "LINEA CLASSIC " + code,
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

    private static RequirementExtractedItem CreateExtractedItem(Guid extractionId,
        string functionalType = "FIXED", IReadOnlyList<string>? features = null)
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
            functionalType: functionalType,
            operation: null,
            panelCount: 1,
            movablePanelCount: null,
            fixedPanelCount: null,
            arrangement: null,
            modulation: null,
            openingDirection: null,
            specialFeatures: features ?? [],
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
