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
    public void Catalog_CurrentVersionIsV4WithSemanticOptionsAndNoSpaces()
    {
        var service = new GetRequirementExperienceCatalogService(
            new RequirementExperienceCatalogProvider());

        var catalog = service.Execute();

        Assert.Equal("sng-experience-v4-001", catalog.Version);
        Assert.Equal(5, catalog.Questions.Count);
        Assert.Equal(14, catalog.Questions.Sum(question => question.Options.Count));
        Assert.Empty(catalog.Spaces);
        Assert.Equal(
            ["THERMAL", "ACOUSTIC", "SECURITY", "UV", "AESTHETICS"],
            catalog.Questions.Select(question => question.BenefitCode).ToArray());
        AssertV4Options(catalog, "THERMAL", "THERMAL_LOW", "THERMAL_MEDIUM", "THERMAL_HIGH");
        AssertV4Options(catalog, "ACOUSTIC", "ACOUSTIC_LOW", "ACOUSTIC_MEDIUM", "ACOUSTIC_HIGH");
        AssertV4Options(catalog, "SECURITY", "SECURITY_LOW", "SECURITY_MEDIUM", "SECURITY_HIGH");
        AssertV4Options(catalog, "UV", "UV_NO", "UV_YES");
        AssertV4Options(catalog, "AESTHETICS", "AESTHETICS_LOW", "AESTHETICS_MEDIUM", "AESTHETICS_HIGH");
    }

    [Fact]
    public void CatalogProvider_FindsCurrentV4V3AndLegacyV2ByVersion()
    {
        var provider = new RequirementExperienceCatalogProvider();

        var v4 = provider.FindByVersion("sng-experience-v4-001");
        var v3 = provider.FindByVersion("sng-experience-v3-001");
        var v2 = provider.FindByVersion("sng-experience-v2-draft-001");

        Assert.NotNull(v4);
        Assert.NotNull(v3);
        Assert.NotNull(v2);
        Assert.Equal(5, v4!.Questions.Count);
        Assert.Empty(v4.Spaces);
        Assert.Equal(5, v3!.Questions.Count);
        Assert.Empty(v3.Spaces);
        Assert.Equal(11, v2!.Questions.Count);
        Assert.Equal(12, v2.Spaces.Count);
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
        Assert.Null(result.Value.Level1Resolution);
        Assert.Equal(1, context.SaveCount);
    }

    [Fact]
    public async Task UpdateAsync_WithV4DraftAndNullSpace_SavesDraft()
    {
        var context = CreateContext();
        var request = new UpdateRequirementExperienceDraftRequest(
            "sng-experience-v4-001",
            null,
            0,
            [
                new("THERMAL", "THERMAL_LOW"),
                new("ACOUSTIC", "ACOUSTIC_LOW"),
                new("SECURITY", "SECURITY_LOW"),
                new("UV", "UV_YES"),
                new("AESTHETICS", "AESTHETICS_LOW")
            ]);

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            request,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.StoredDraft);
        Assert.Equal("sng-experience-v4-001", result.Value!.CatalogVersion);
        Assert.Null(result.Value.SpaceTypeCode);
        Assert.Equal(5, result.Value.Answers.Count);
        AssertLevel1Resolution(result.Value.Level1Resolution, "CLASSIC", "LAMINADO", true, 5, 5);
        Assert.Equal(1, context.SaveCount);
    }

    [Fact]
    public async Task UpdateAsync_WithV3DraftAndNullSpace_SavesDraft()
    {
        var context = CreateContext();
        var request = new UpdateRequirementExperienceDraftRequest(
            "sng-experience-v3-001",
            null,
            0,
            [
                new("THERMAL", "THERMAL_3"),
                new("ACOUSTIC", "ACOUSTIC_3"),
                new("SECURITY", "SECURITY_2"),
                new("UV", "UV_NO"),
                new("AESTHETICS", "AESTHETICS_3")
            ]);

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            request,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(context.StoredDraft);
        Assert.Equal("sng-experience-v3-001", result.Value!.CatalogVersion);
        Assert.Null(result.Value.SpaceTypeCode);
        Assert.Equal(5, result.Value.Answers.Count);
        AssertLevel1Resolution(result.Value.Level1Resolution, "CLASSIC", "TEMPLADO", true, 5, 5);
        Assert.Equal(1, context.SaveCount);
    }

    [Fact]
    public async Task UpdateAsync_WithV4UnknownBenefit_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v4-001",
                null,
                0,
                [new("B01", "VIS_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.UnknownBenefit, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task UpdateAsync_WithV4OptionFromOtherBenefit_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v4-001",
                null,
                0,
                [new("THERMAL", "ACOUSTIC_LOW")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.OptionDoesNotBelongToBenefit, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task UpdateAsync_WithV4DuplicateBenefit_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v4-001",
                null,
                0,
                [
                    new("THERMAL", "THERMAL_LOW"),
                    new("THERMAL", "THERMAL_MEDIUM")
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.DuplicateBenefit, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task UpdateAsync_WithV4UnknownSpace_IsRejectedBecauseV4HasNoSpaces()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v4-001",
                "ESP_ALC_PPAL",
                0,
                [new("THERMAL", "THERMAL_LOW")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.UnknownSpaceType, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task UpdateAsync_WithV3UnknownBenefit_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v3-001",
                null,
                0,
                [new("B01", "VIS_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.UnknownBenefit, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task UpdateAsync_WithV3OptionFromOtherBenefit_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v3-001",
                null,
                0,
                [new("THERMAL", "ACOUSTIC_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.OptionDoesNotBelongToBenefit, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task UpdateAsync_WithV3DuplicateBenefit_IsRejected()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v3-001",
                null,
                0,
                [
                    new("THERMAL", "THERMAL_1"),
                    new("THERMAL", "THERMAL_2")
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.DuplicateBenefit, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task UpdateAsync_WithV3UnknownSpace_IsRejectedBecauseV3HasNoSpaces()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v3-001",
                "ESP_ALC_PPAL",
                0,
                [new("THERMAL", "THERMAL_1")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.UnknownSpaceType, result.Error);
        Assert.Null(context.StoredDraft);
    }

    [Fact]
    public async Task GetAsync_WithCompleteV4Draft_ReturnsLevel1Resolution()
    {
        var context = CreateContext();
        context.StoredDraft = RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.FirstItem.Id,
            "sng-experience-v4-001",
            null,
            [
                new("THERMAL", "THERMAL_MEDIUM"),
                new("ACOUSTIC", "ACOUSTIC_LOW"),
                new("SECURITY", "SECURITY_LOW"),
                new("UV", "UV_NO"),
                new("AESTHETICS", "AESTHETICS_LOW")
            ],
            context.User.Id,
            At);

        var result = await context.GetService.ExecuteAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items, value => value.TechnicalProposalItemId == context.FirstItem.Id);
        AssertLevel1Resolution(item.Level1Resolution, "PREMIUM", "LAMINADO", true, 5, 5);
    }

    [Fact]
    public async Task GetAsync_WithCompleteV3Draft_ReturnsLevel1Resolution()
    {
        var context = CreateContext();
        context.StoredDraft = RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.FirstItem.Id,
            "sng-experience-v3-001",
            null,
            [
                new("THERMAL", "THERMAL_5"),
                new("ACOUSTIC", "ACOUSTIC_3"),
                new("SECURITY", "SECURITY_2"),
                new("UV", "UV_YES"),
                new("AESTHETICS", "AESTHETICS_3")
            ],
            context.User.Id,
            At);

        var result = await context.GetService.ExecuteAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items, value => value.TechnicalProposalItemId == context.FirstItem.Id);
        AssertLevel1Resolution(item.Level1Resolution, "PREMIUM", "LAMINADO", true, 5, 5);
    }

    [Fact]
    public async Task GetAsync_WithPartialV3Draft_ReturnsProvisionalLevel1Resolution()
    {
        var context = CreateContext();
        context.StoredDraft = RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.FirstItem.Id,
            "sng-experience-v3-001",
            null,
            [new("THERMAL", "THERMAL_2")],
            context.User.Id,
            At);

        var result = await context.GetService.ExecuteAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items, value => value.TechnicalProposalItemId == context.FirstItem.Id);
        AssertLevel1Resolution(item.Level1Resolution, "CLASSIC", "TEMPLADO", false, 1, 5);
    }

    [Fact]
    public async Task UpdateAsync_WithV3ChangedAnswers_ReturnsRecalculatedLevel1Resolution()
    {
        var context = CreateContext();
        context.StoredDraft = RequirementItemExperienceDraft.Create(
            context.Proposal.Id,
            context.FirstItem.Id,
            "sng-experience-v3-001",
            null,
            [new("THERMAL", "THERMAL_2")],
            context.User.Id,
            At);

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v3-001",
                null,
                1,
                [
                    new("THERMAL", "THERMAL_2"),
                    new("AESTHETICS", "AESTHETICS_5")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertLevel1Resolution(result.Value!.Level1Resolution, "PREMIUM", "TEMPLADO", false, 2, 5);
    }

    [Fact]
    public async Task UpdateAsync_WithV3UvYes_ReturnsLaminadoWithoutSystemTier()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v3-001",
                null,
                0,
                [new("UV", "UV_YES")]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertLevel1Resolution(result.Value!.Level1Resolution, null, "LAMINADO", false, 1, 5);
    }

    [Fact]
    public async Task UpdateAsync_WithV3UvNoOnly_ReturnsIncompleteResolutionWithoutFamilies()
    {
        var context = CreateContext();

        var result = await context.UpdateService.ExecuteAsync(
            context.Proposal.Id,
            context.FirstItem.Id,
            new UpdateRequirementExperienceDraftRequest(
                "sng-experience-v3-001",
                null,
                0,
                [new("UV", "UV_NO")]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertLevel1Resolution(result.Value!.Level1Resolution, null, null, false, 1, 5);
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
            Assert.Null(item.Level1Resolution);
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

    private static void AssertLevel1Resolution(
        RequirementExperienceLevel1ResolutionResponse? resolution,
        string? expectedSystemTier,
        string? expectedGlassFamily,
        bool expectedIsComplete,
        int expectedAnsweredBenefits,
        int expectedRequiredBenefits)
    {
        Assert.NotNull(resolution);
        Assert.Equal(expectedSystemTier, resolution!.SystemTier);
        Assert.Equal(expectedGlassFamily, resolution.GlassFamily);
        Assert.Equal(expectedIsComplete, resolution.IsComplete);
        Assert.Equal(expectedAnsweredBenefits, resolution.AnsweredBenefits);
        Assert.Equal(expectedRequiredBenefits, resolution.RequiredBenefits);
    }

    private static void AssertV4Options(
        RequirementExperienceCatalogResponse catalog,
        string benefitCode,
        params string[] expectedOptionCodes)
    {
        var question = Assert.Single(catalog.Questions, value => value.BenefitCode == benefitCode);

        Assert.Equal(
            expectedOptionCodes,
            question.Options.Select(option => option.OptionCode).ToArray());
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
