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

public sealed class RequirementExperienceConfigurationServiceTests
{
    [Theory]
    [InlineData(RequirementExtractionValueStatus.Explicit, null)]
    [InlineData(RequirementExtractionValueStatus.Inferred, null)]
    [InlineData(RequirementExtractionValueStatus.Ambiguous, "QUANTITY_AMBIGUOUS")]
    [InlineData(RequirementExtractionValueStatus.Unknown, "QUANTITY_UNKNOWN")]
    [InlineData(null, null)]
    public async Task Resolver_QuantityProvenance_OnlyUncertainExtractionNeedsReview(
        RequirementExtractionValueStatus? status, string? expectedReason)
    {
        var context = CreateContext();
        var extracted = (RequirementExtractedItem)Activator.CreateInstance(typeof(RequirementExtractedItem), nonPublic: true)!;
        SetPrivateProperty(extracted, "Quantity", (int?)1);
        SetPrivateProperty(extracted, "QuantityExtractionStatus", status);
        SetPrivateProperty(extracted, "QuantityExtractionConfidence", (decimal?)0.5m);
        SetPrivateProperty(context.Item, "ExtractedItem", extracted);
        var resolver = new RequirementExperienceConfigurationResolver(
            context.Requirements,
            Substitute.For<IProductSystemCatalogRepository>(),
            Substitute.For<IGlassTypeCatalogRepository>(),
            new RequirementExperienceLevel2Resolver(
                Substitute.For<ISgProductSystemConstraintEvaluator>(),
                Substitute.For<IGlassCandidateResolver>(),
                Substitute.For<ISgTechnicalSelector>()));
        var resolved = await resolver.ResolveAsync(context.Proposal, Xunit.TestContext.Current.CancellationToken);
        var item = Assert.Single(resolved.Items);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(RequirementQuantitySource.EXTRACTED, item.QuantitySource);
        Assert.Equal(status?.ToString().ToUpperInvariant(), item.QuantityExtractionStatus);
        Assert.Equal(0.5m, item.QuantityExtractionConfidence);
        Assert.Equal(expectedReason is null ? [] : new[] { expectedReason },
            item.ReviewReasons.Where(reason => reason.StartsWith("QUANTITY_")));
        Assert.DoesNotContain("QUANTITY_REQUIRED",
            RequirementExperienceConfigurationReadinessEvaluator.Evaluate(resolved).BlockingReasons);
    }

    [Fact]
    public async Task Resolver_AssumedQuantity_EmitsItemWarningAndDeduplicatesGlobalReview()
    {
        var context = CreateContext();
        SetPrivateProperty(context.Item, "BaseQuantity", (int?)null);
        var second = CreateProposalItem(context.Proposal.Id);
        SetPrivateProperty(second, "Sequence", 2);
        SetPrivateProperty(second, "BaseQuantity", (int?)null);
        context.Proposal.AddItem(second);
        var resolver = new RequirementExperienceConfigurationResolver(
            context.Requirements,
            Substitute.For<IProductSystemCatalogRepository>(),
            Substitute.For<IGlassTypeCatalogRepository>(),
            new RequirementExperienceLevel2Resolver(
                Substitute.For<ISgProductSystemConstraintEvaluator>(),
                Substitute.For<IGlassCandidateResolver>(),
                Substitute.For<ISgTechnicalSelector>()));

        var result = await resolver.ResolveAsync(context.Proposal, CancellationToken.None);
        Assert.All(result.Items, item =>
        {
            Assert.Equal(1, item.Quantity);
            Assert.Equal(RequirementQuantitySource.ASSUMED_DEFAULT, item.QuantitySource);
            Assert.Contains("QUANTITY_ASSUMED", item.ReviewReasons);
        });
        var readiness = RequirementExperienceConfigurationReadinessEvaluator.Evaluate(result);
        Assert.Equal(1, readiness.ReviewReasons.Count(reason => reason == "QUANTITY_ASSUMED"));
        Assert.DoesNotContain("QUANTITY_REQUIRED", readiness.BlockingReasons);
        Assert.Contains("FINISH_REQUIRED", readiness.BlockingReasons);
        Assert.False(readiness.CanConfirm);
        Assert.Null(context.Item.BaseQuantity);
    }

    [Theory]
    [InlineData(1, "CONFIRMED", "QUANTITY_ASSUMED")]
    [InlineData(2, "STALE", "QUANTITY_ASSUMED")]
    [InlineData(1, "CONFIRMED", "QUANTITY_AMBIGUOUS")]
    [InlineData(2, "STALE", "QUANTITY_AMBIGUOUS")]
    [InlineData(1, "CONFIRMED", "QUANTITY_UNKNOWN")]
    public async Task QuantityReview_IsWarningAndSnapshotOnlyStalesWhenValueChanges(int manual, string status, string reason)
    {
        var context = CreateContext();
        context.CurrentItem = context.CurrentItem with
        {
            Quantity = 1,
            QuantitySource = reason == "QUANTITY_ASSUMED" ? RequirementQuantitySource.ASSUMED_DEFAULT : RequirementQuantitySource.EXTRACTED,
            QuantityExtractionStatus = reason == "QUANTITY_AMBIGUOUS" ? "AMBIGUOUS" : "UNKNOWN",
            QuantityExtractionConfidence = 0.5m,
            ReviewReasons = [reason]
        };
        var before = await context.Service.GetStatusAsync(context.Proposal.Id, CancellationToken.None);
        Assert.True(before.Value!.CanConfirm);
        Assert.Equal(context.CurrentItem.QuantitySource.ToString(), Assert.Single(before.Value.Items).QuantitySource);
        Assert.Equal(context.CurrentItem.QuantityExtractionStatus, Assert.Single(before.Value.Items).QuantityExtractionStatus);
        Assert.Equal(0.5m, Assert.Single(before.Value.Items).QuantityExtractionConfidence);
        Assert.Equal("WARNING", Assert.Single(before.Value.Items).ReadinessState);
        Assert.Equal(new[] { reason }, before.Value.ReviewReasons);
        Assert.DoesNotContain("QUANTITY_REQUIRED", before.Value.BlockingReasons);

        var confirmed = await context.Service.ConfirmAsync(context.Proposal.Id, CancellationToken.None);
        Assert.True(confirmed.IsSuccess);
        Assert.Contains(reason, Assert.Single(context.LatestConfirmation!.Items).ReviewReasons);
        context.CurrentItem = context.CurrentItem with
        {
            Quantity = manual,
            QuantitySource = RequirementQuantitySource.MANUAL,
            ReviewReasons = []
        };
        var after = await context.Service.GetStatusAsync(context.Proposal.Id, CancellationToken.None);
        Assert.Equal(status, after.Value!.Status);
        Assert.Equal("MANUAL", Assert.Single(after.Value.Items).QuantitySource);
        Assert.DoesNotContain(reason, after.Value.ReviewReasons);
        Assert.Contains(reason, Assert.Single(context.LatestConfirmation.Items).ReviewReasons);
    }

    [Fact]
    public async Task Confirm_ResolvesOnceAndSnapshotsExactlyThatConfiguration()
    {
        var context = CreateContext();
        var expected = context.CurrentItem;
        var result = await context.Service.ConfirmAsync(context.Proposal.Id, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, context.Resolver.Calls);
        var snapshot = Assert.Single(context.LatestConfirmation!.Items);
        Assert.Equal(expected.Quantity, snapshot.Quantity);
        Assert.Equal(expected.SystemId, snapshot.SystemId);
        Assert.Equal(expected.GlassTypeId, snapshot.GlassTypeId);
        Assert.Equal(expected.FinishTypeId, snapshot.FinishTypeId);
    }

    [Fact]
    public async Task Confirm_StateChangedBeforeLockCompletes_IsEvaluatedAndBlocked()
    {
        var context = CreateContext();
        context.Requirements.LockExperienceConfigurationAsync(context.Proposal.Id, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                context.CurrentItem = context.CurrentItem with { Quantity = null };
                return Task.CompletedTask;
            });
        var result = await context.Service.ConfirmAsync(context.Proposal.Id, CancellationToken.None);
        Assert.Equal(RequirementExperienceFailure.ConfigurationBlocked, result.Error);
        Assert.Empty(context.Confirmations);
        Assert.Equal(1, context.Resolver.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Confirm_MapsOnlyConcurrencyFailuresToConflict(bool conflict)
    {
        var context = CreateContext();
        Exception exception = conflict
            ? new RequirementExperienceConflictException(new Exception("race"))
            : new RequirementPersistenceException(new Exception("storage"));
        context.Requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(exception));
        var result = await context.Service.ConfirmAsync(context.Proposal.Id, CancellationToken.None);
        Assert.Equal(conflict ? RequirementExperienceFailure.Conflict
            : RequirementExperienceFailure.PersistenceError, result.Error);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(500)]
    public async Task Confirm_LongLocation_PreservesSnapshotAndRemainsConfirmed(int length)
    {
        var context = CreateContext();
        var location = new string('L', length);
        context.CurrentItem = context.CurrentItem with { Location = location };
        var confirmed = await context.Service.ConfirmAsync(context.Proposal.Id, CancellationToken.None);
        Assert.True(confirmed.IsSuccess);
        Assert.Equal(location, Assert.Single(context.LatestConfirmation!.Items).Location);
        var status = await context.Service.GetStatusAsync(context.Proposal.Id, CancellationToken.None);
        Assert.True(status.IsSuccess);
        Assert.Equal(RequirementExperienceConfigurationStatuses.Confirmed, status.Value!.Status);
        Assert.Equal(location, Assert.Single(status.Value.Items).Location);
    }

    [Theory]
    [InlineData(UserRole.User, true)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.User, false)]
    [InlineData(UserRole.Admin, false)]
    public async Task Access_AdminMayReadButOnlyOwnerMayConfirm(UserRole role, bool owner)
    {
        var context = CreateContext();
        SetPrivateProperty(context.User, "Role", role);
        if (!owner)
            SetPrivateProperty(context.Proposal.Requirement.PreQuote.Project, "CreatedByUserId", Guid.NewGuid());
        var status = await context.Service.GetStatusAsync(context.Proposal.Id, CancellationToken.None);
        Assert.Equal(owner || role == UserRole.Admin, status.IsSuccess);
        var confirmed = await context.Service.ConfirmAsync(context.Proposal.Id, CancellationToken.None);
        Assert.Equal(owner, confirmed.IsSuccess);
        if (!owner)
        {
            Assert.Equal(RequirementExperienceFailure.Forbidden, confirmed.Error);
            Assert.Empty(context.Confirmations);
            await context.Requirements.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        }
    }

    private static readonly DateTimeOffset At =
        new(2026, 10, 13, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetStatusAsync_WithoutConfirmation_ReturnsDraft()
    {
        var context = CreateContext();

        var result = await context.Service.GetStatusAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            RequirementExperienceConfigurationStatuses.Draft,
            result.Value!.Status);
        Assert.True(result.Value.CanConfirm);
        Assert.Null(result.Value.LatestConfirmationRevision);
    }

    [Fact]
    public async Task ConfirmAsync_WithValidConfiguration_CreatesRevisionOne()
    {
        var context = CreateContext();

        var result = await context.Service.ConfirmAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            RequirementExperienceConfigurationStatuses.Confirmed,
            result.Value!.Status);
        Assert.Equal(1, result.Value.LatestConfirmationRevision);
        Assert.Single(context.Confirmations);
        var snapshot = Assert.Single(context.Confirmations[0].Items);
        Assert.True(snapshot.IsIncluded);
        Assert.Equal(context.Item.Id, snapshot.TechnicalProposalItemId);
        Assert.Equal(context.CurrentItem.SystemId, snapshot.SystemId);
        Assert.Equal(context.CurrentItem.GlassTypeId, snapshot.GlassTypeId);
        Assert.Equal(context.CurrentItem.FinishTypeId, snapshot.FinishTypeId);
    }

    [Fact]
    public async Task GetStatusAsync_AfterConfirmationWithoutChanges_ReturnsConfirmed()
    {
        var context = CreateContext();
        context.SetLatestFromCurrent();

        var result = await context.Service.GetStatusAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            RequirementExperienceConfigurationStatuses.Confirmed,
            result.Value!.Status);
        Assert.Equal(1, result.Value.LatestConfirmationRevision);
    }

    [Fact]
    public async Task ConfirmAsync_WhenAlreadyConfirmed_DoesNotCreateRevisionTwo()
    {
        var context = CreateContext();
        context.SetLatestFromCurrent();

        var result = await context.Service.ConfirmAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.LatestConfirmationRevision);
        Assert.Single(context.Confirmations);
        context.Requirements.DidNotReceive()
            .AddExperienceConfigurationConfirmation(
                Arg.Any<RequirementExperienceConfigurationConfirmation>());
    }

    [Theory]
    [InlineData("system")]
    [InlineData("glass")]
    [InlineData("quantity")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("location")]
    [InlineData("excluded")]
    public async Task GetStatusAsync_WhenCurrentConfigurationChanges_ReturnsStale(
        string changed)
    {
        var context = CreateContext();
        context.SetLatestFromCurrent();
        context.ApplyChange(changed);

        var result = await context.Service.GetStatusAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            RequirementExperienceConfigurationStatuses.Stale,
            result.Value!.Status);
    }

    [Fact]
    public async Task ConfirmAsync_WhenStaleAndConfirmable_CreatesRevisionTwo()
    {
        var context = CreateContext();
        context.SetLatestFromCurrent();
        context.ApplyChange("system");

        var result = await context.Service.ConfirmAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.LatestConfirmationRevision);
        Assert.Equal(2, context.Confirmations.Count);
        Assert.Equal(2, context.Confirmations[^1].Revision);
    }

    [Fact]
    public async Task ConfirmAsync_WithRequiresReviewEffectiveValue_WarnsButConfirms()
    {
        var context = CreateContext();
        context.CurrentItem = context.CurrentItem with
        {
            SystemOverrideState =
                RequirementExperienceLevel2OverrideStates.RequiresReview,
            ReviewReasons = ["SYSTEM_REQUIRES_REVIEW"]
        };

        var result = await context.Service.ConfirmAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CanConfirm);
        Assert.Equal(["SYSTEM_REQUIRES_REVIEW"], result.Value.ReviewReasons);
        Assert.Empty(result.Value.BlockingReasons);
    }

    [Theory]
    [InlineData("system", "SYSTEM_REQUIRED")]
    [InlineData("glass", "GLASS_REQUIRED")]
    [InlineData("finish", "FINISH_REQUIRED")]
    public async Task ConfirmAsync_WithMissingRequiredValue_IsBlocked(
        string missing,
        string expectedReason)
    {
        var context = CreateContext();
        context.CurrentItem = missing switch
        {
            "system" => context.CurrentItem with { SystemId = null },
            "glass" => context.CurrentItem with { GlassTypeId = null },
            _ => context.CurrentItem with { FinishTypeId = null }
        };

        var result = await context.Service.ConfirmAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.ConfigurationBlocked, result.Error);
        Assert.Contains(expectedReason, result.Value!.BlockingReasons);
        Assert.Empty(context.Confirmations);
    }

    [Fact]
    public async Task ConfirmAsync_WithFinishPendingExternalSource_IsBlocked()
    {
        var context = CreateContext();
        context.CurrentItem = context.CurrentItem with
        {
            FinishTypeId = null,
            FinishState =
                RequirementExperienceLevel2States.PendingExternalSource
        };

        var result = await context.Service.ConfirmAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RequirementExperienceFailure.ConfigurationBlocked, result.Error);
        Assert.Contains(
            "FINISH_PENDING_EXTERNAL_SOURCE",
            result.Value!.BlockingReasons);
        Assert.Empty(context.Confirmations);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsItemIdentityAndReadyState()
    {
        var context = CreateContext();

        var result = await context.Service.GetStatusAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(context.Item.Id, item.TechnicalProposalItemId);
        Assert.Equal("V-01", item.Reference);
        Assert.Equal(1, item.Sequence);
        Assert.Equal("READY", item.ReadinessState);
        Assert.Empty(item.BlockingReasons);
        Assert.Empty(item.ReviewReasons);
    }

    [Fact]
    public async Task GetStatusAsync_WithMissingFields_ReturnsItemLevelBlockers()
    {
        var context = CreateContext();
        context.CurrentItem = context.CurrentItem with
        {
            Quantity = null,
            GlassTypeId = null,
            FinishTypeId = null,
            FinishState =
                RequirementExperienceLevel2States.PendingExternalSource
        };

        var result = await context.Service.GetStatusAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("BLOCKED", item.ReadinessState);
        Assert.Contains("QUANTITY_REQUIRED", item.BlockingReasons);
        Assert.Contains("GLASS_REQUIRED", item.BlockingReasons);
        Assert.Contains("FINISH_REQUIRED", item.BlockingReasons);
        Assert.Contains("FINISH_PENDING_EXTERNAL_SOURCE", item.BlockingReasons);
        Assert.DoesNotContain("SYSTEM_REQUIRED", item.BlockingReasons);
        Assert.Equal(
            item.BlockingReasons.Order(StringComparer.Ordinal),
            result.Value.BlockingReasons);
    }

    [Fact]
    public async Task GetStatusAsync_WithQuantityPresent_DoesNotReturnQuantityBlocker()
    {
        var context = CreateContext();
        context.CurrentItem = context.CurrentItem with
        {
            GlassTypeId = null
        };

        var result = await context.Service.GetStatusAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.DoesNotContain("QUANTITY_REQUIRED", item.BlockingReasons);
        Assert.Contains("GLASS_REQUIRED", item.BlockingReasons);
    }

    [Fact]
    public async Task GetStatusAsync_WithReviewOnly_ReturnsWarningWithoutBlocking()
    {
        var context = CreateContext();
        context.CurrentItem = context.CurrentItem with
        {
            ReviewReasons = ["GLASS_PANE_GEOMETRY_UNRESOLVED"]
        };

        var result = await context.Service.GetStatusAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.True(result.Value.CanConfirm);
        Assert.Equal("WARNING", item.ReadinessState);
        Assert.Equal(["GLASS_PANE_GEOMETRY_UNRESOLVED"], item.ReviewReasons);
        Assert.Empty(item.BlockingReasons);
        Assert.Equal(item.ReviewReasons, result.Value.ReviewReasons);
    }

    [Fact]
    public async Task GetStatusAsync_WithExcludedItem_DoesNotAddRequiredFieldBlockers()
    {
        var context = CreateContext();
        context.CurrentItem = RequirementExperienceCurrentConfigurationItem.Excluded(
            context.Item.Id,
            context.Item.Reference,
            context.Item.Sequence);

        var result = await context.Service.GetStatusAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.False(item.IsIncluded);
        Assert.Equal("READY", item.ReadinessState);
        Assert.Empty(item.BlockingReasons);
        Assert.Contains("NO_INCLUDED_ITEMS", result.Value.BlockingReasons);
        Assert.DoesNotContain("QUANTITY_REQUIRED", result.Value.BlockingReasons);
        Assert.DoesNotContain("GLASS_REQUIRED", result.Value.BlockingReasons);
        Assert.DoesNotContain("FINISH_REQUIRED", result.Value.BlockingReasons);
    }

    [Fact]
    public async Task ConfirmAsync_DoesNotTouchLegacySelectionOrCommercialState()
    {
        var context = CreateContext();
        var commercialRevision = context.Proposal.CommercialRevision;

        var result = await context.Service.ConfirmAsync(
            context.Proposal.Id,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(context.Item.SelectedSystemId);
        Assert.Null(context.Item.SelectedGlassTypeId);
        Assert.Null(context.Item.SelectedFinishTypeId);
        Assert.Equal(commercialRevision, context.Proposal.CommercialRevision);
        Assert.Null(context.Proposal.CommercialConfirmedAtUtc);
        Assert.Null(context.Proposal.CommercialConfirmedByUserId);
    }

    private static TestContext CreateContext()
    {
        var requirements = Substitute.For<IRequirementRepository>();
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
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
        var proposal = RequirementTechnicalProposal.Create(
            requirement.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            false,
            At);
        SetPrivateProperty(proposal, "Requirement", requirement);
        var item = CreateProposalItem(proposal.Id);
        proposal.AddItem(item);

        var context = new TestContext(
            requirements,
            proposal,
            item,
            new FakeRequirementExperienceConfigurationResolver(),
            user);

        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(user.Id);
        identity.FindUserByIdAsync(user.Id, Arg.Any<CancellationToken>())
            .Returns(user);
        requirements.FindTechnicalProposalForUpdateAsync(
                proposal.Id,
                Arg.Any<CancellationToken>())
            .Returns(proposal);
        requirements.GetLatestExperienceConfigurationConfirmationAsync(
                proposal.Id,
                Arg.Any<CancellationToken>())
            .Returns(_ => context.LatestConfirmation);
        requirements.BeginExperienceConfigurationTransactionAsync(
                Arg.Any<CancellationToken>())
            .Returns(new NoopRequirementPersistenceTransaction());
        requirements.When(repository =>
                repository.AddExperienceConfigurationConfirmation(
                    Arg.Any<RequirementExperienceConfigurationConfirmation>()))
            .Do(call =>
            {
                var confirmation =
                    call.Arg<RequirementExperienceConfigurationConfirmation>();
                context.Confirmations.Add(confirmation);
                context.LatestConfirmation = confirmation;
            });
        requirements.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        context.Service = new RequirementExperienceConfigurationService(
            requirements,
            identity,
            currentUser,
            context.Resolver,
            new FixedTimeProvider(At));
        context.CurrentItem = ValidItem(item.Id);
        return context;
    }

    private static RequirementExperienceCurrentConfigurationItem ValidItem(Guid itemId) =>
        RequirementExperienceCurrentConfigurationItem.Included(
            itemId,
            reference: "V-01",
            sequence: 1,
            quantity: 1,
            widthMm: 1000,
            heightMm: 2400,
            areaM2: 2.4m,
            location: "Nivel 1",
            systemId: Guid.NewGuid(),
            glassTypeId: Guid.NewGuid(),
            finishTypeId: Guid.NewGuid(),
            systemState: RequirementExperienceLevel2States.Resolved,
            glassState: RequirementExperienceLevel2States.Resolved,
            finishState: RequirementExperienceLevel2States.Resolved,
            systemOverrideState: RequirementExperienceLevel2OverrideStates.Valid,
            glassOverrideState: RequirementExperienceLevel2OverrideStates.Valid,
            reviewReasons: [],
            blockingReasons: []);

    private static RequirementTechnicalProposalItem CreateProposalItem(Guid proposalId)
    {
        return RequirementTechnicalProposalItem.Create(
            proposalId,
            requirementExtractedItemId: Guid.NewGuid(),
            suggestedSystemId: Guid.NewGuid(),
            suggestedGlassTypeId: Guid.NewGuid(),
            suggestedFinishTypeId: Guid.NewGuid(),
            sequence: 1,
            reference: "V-01",
            description: "Item V-01",
            elementType: StructuredElementType.Window,
            quantity: 1,
            widthMillimeters: 1000,
            heightMillimeters: 2400,
            overallConfidence: 0.9m,
            systemConfidence: 0.9m,
            glassConfidence: 0.9m,
            finishConfidence: 0.9m,
            requiresReview: false,
            isTechnicallyComplete: true,
            isPriceable: true,
            reviewReasons: [],
            systemResolutionReasons: [],
            glassResolutionReasons: [],
            finishResolutionReasons: [],
            historicalSupportCount: 0,
            historicalBestSimilarity: null,
            historicalAverageSimilarity: null,
            historicalSimilarityStatus: "NotEvaluated",
            createdAtUtc: At);
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

    private sealed class FakeRequirementExperienceConfigurationResolver
        : IRequirementExperienceConfigurationResolver
    {
        public TestContext? Context { get; set; }
        public int Calls { get; private set; }

        public Task<RequirementExperienceCurrentConfiguration> ResolveAsync(
            RequirementTechnicalProposal proposal,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RequirementExperienceCurrentConfiguration(
                proposal.RequirementId,
                proposal.Id,
                [Context!.CurrentItem]));
        }
    }

    private sealed class NoopRequirementPersistenceTransaction
        : IRequirementPersistenceTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestContext
    {
        public TestContext(
            IRequirementRepository requirements,
            RequirementTechnicalProposal proposal,
            RequirementTechnicalProposalItem item,
            FakeRequirementExperienceConfigurationResolver resolver,
            User user)
        {
            Requirements = requirements;
            Proposal = proposal;
            Item = item;
            Resolver = resolver;
            User = user;
            resolver.Context = this;
        }

        public IRequirementRepository Requirements { get; }

        public RequirementTechnicalProposal Proposal { get; }

        public RequirementTechnicalProposalItem Item { get; }

        public FakeRequirementExperienceConfigurationResolver Resolver { get; }

        public User User { get; }

        public RequirementExperienceConfigurationService Service { get; set; } =
            null!;

        public RequirementExperienceCurrentConfigurationItem CurrentItem { get; set; } =
            null!;

        public RequirementExperienceConfigurationConfirmation? LatestConfirmation { get; set; }

        public List<RequirementExperienceConfigurationConfirmation> Confirmations { get; } =
            [];

        public void SetLatestFromCurrent()
        {
            var confirmation =
                RequirementExperienceConfigurationConfirmation.Create(
                    Proposal.RequirementId,
                    Proposal.Id,
                    Confirmations.Count + 1,
                    User.Id,
                    At);
            confirmation.AddItem(CurrentItem.ToSnapshot(confirmation.Id));
            Confirmations.Add(confirmation);
            LatestConfirmation = confirmation;
        }

        public void ApplyChange(string changed)
        {
            CurrentItem = changed switch
            {
                "system" => CurrentItem with { SystemId = Guid.NewGuid() },
                "glass" => CurrentItem with { GlassTypeId = Guid.NewGuid() },
                "quantity" => CurrentItem with { Quantity = 2 },
                "width" => CurrentItem with { WidthMm = 1200, AreaM2 = 2.88m },
                "height" => CurrentItem with { HeightMm = 2600, AreaM2 = 2.6m },
                "location" => CurrentItem with { Location = "Nivel 2" },
                "excluded" => RequirementExperienceCurrentConfigurationItem.Excluded(
                    Item.Id),
                _ => CurrentItem
            };
        }
    }
}
