using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes;
using Application.PreQuotes.UpdatePreQuoteDraft;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class UpdatePreQuoteDraftServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PreQuoteId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateForeignOwner_ReturnsNotFound()
    {
        var draft = CreateDraft();
        var user = CreateUser();
        var currentUserId = user.Id;
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var repository = Substitute.For<IPreQuoteDraftRepository>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(currentUserId);
        identity.FindUserByIdAsync(currentUserId, Arg.Any<CancellationToken>())
            .Returns(user);
        Guid? ownerFromRepository = null;
        repository.FindForUpdateAsync(
                PreQuoteId,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                ownerFromRepository = call.ArgAt<Guid>(1);
                return (PreQuoteDraft?)null;
            });
        var service = new UpdatePreQuoteDraftService(
            new UpdatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At.AddMinutes(1)));

        var command = CreateCommand(draft);
        var result = await service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.NotFound, result.Failure);
        Assert.Equal(user.Id, ownerFromRepository);
    }

    [Fact]
    public async Task Execute_RealReviewEdit_SucceedsAndSavesOnce()
    {
        var draft = CreateDraft();
        var context = CreateContext(draft);
        var command = CreateCommand(
            draft,
            resolveIssue: true,
            addManualRows: true,
            excludeExistingRequirement: true);

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(draft, result.Draft);
        Assert.Equal(PreQuoteDraftStatus.InReview, draft.Status);
        Assert.Equal(2, draft.Version);
        Assert.Equal(
            PreQuoteDraftResolutionStatus.Resolved,
            draft.Issues.Single().ResolutionStatus);
        Assert.Equal("reviewed", draft.Issues.Single().ResolutionNote);
        Assert.Equal(2, draft.Items.Count);
        Assert.Equal(2, draft.Requirements.Count);
        Assert.Equal(2, draft.DocumentReferences.Count);
        Assert.False(draft.Requirements.Single(x => x.Origin == PreQuoteDraftOrigin.Ai).IsIncluded);
        await context.Repository.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_LeavingIssuePending_Succeeds()
    {
        var draft = CreateDraft();
        var context = CreateContext(draft);

        var result = await context.Service.ExecuteAsync(
            CreateCommand(draft),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(PreQuoteDraftFailure.PendingIssues, result.Failure);
        Assert.Equal(
            PreQuoteDraftResolutionStatus.Pending,
            draft.Issues.Single().ResolutionStatus);
        Assert.Equal(PreQuoteDraftStatus.InReview, draft.Status);
        Assert.Equal(2, draft.Version);
        await context.Repository.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_InvalidEdit_DoesNotSaveOrIncrementVersion()
    {
        var draft = CreateDraft();
        var context = CreateContext(draft);
        var command = CreateCommand(draft) with { Items = [] };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.InvalidDraftContent, result.Failure);
        Assert.Equal(1, draft.Version);
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateWidth_ReturnsStaleItem()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        var item = draft.Items.Single();

        var command = CreateCommand(draft) with
        {
            Items =
            [
                new(
                    item.Id, 1, item.Reference, item.Description,
                    item.ElementType, item.RawMeasurements,
                    120, item.HeightMillimeters, item.Quantity, item.IsIncluded)
            ]
        };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(PreQuoteDraftValuationStatus.Stale, item.ValuationStatus);
        Assert.Equal(
            PreQuoteDraftValuationInvalidationReason.WidthChanged,
            item.ValuationSnapshot?.InvalidationReason);
    }

    [Fact]
    public async Task UpdateHeight_ReturnsStaleItem()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        var item = draft.Items.Single();

        var command = CreateCommand(draft) with
        {
            Items =
            [
                new(
                    item.Id, 1, item.Reference, item.Description,
                    item.ElementType, item.RawMeasurements,
                    item.WidthMillimeters, 150, item.Quantity, item.IsIncluded)
            ]
        };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(PreQuoteDraftValuationStatus.Stale, item.ValuationStatus);
        Assert.Equal(
            PreQuoteDraftValuationInvalidationReason.HeightChanged,
            item.ValuationSnapshot?.InvalidationReason);
    }

    [Fact]
    public async Task UpdateQuantity_ReturnsStaleItem()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        var item = draft.Items.Single();

        var command = CreateCommand(draft) with
        {
            Items =
            [
                new(
                    item.Id, 1, item.Reference, item.Description,
                    item.ElementType, item.RawMeasurements,
                    item.WidthMillimeters, item.HeightMillimeters, 5, item.IsIncluded)
            ]
        };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(PreQuoteDraftValuationStatus.Stale, item.ValuationStatus);
        Assert.Equal(
            PreQuoteDraftValuationInvalidationReason.QuantityChanged,
            item.ValuationSnapshot?.InvalidationReason);
    }

    [Fact]
    public async Task UpdateDescription_DoesNotInvalidate()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        var item = draft.Items.Single();

        var command = CreateCommand(draft) with
        {
            Items =
            [
                new(
                    item.Id, 1, item.Reference, "Description updated",
                    item.ElementType, item.RawMeasurements,
                    item.WidthMillimeters, item.HeightMillimeters, item.Quantity,
                    item.IsIncluded)
            ]
        };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            PreQuoteDraftValuationStatus.Valued, item.ValuationStatus);
        Assert.Null(item.ValuationSnapshot?.InvalidationReason);
        Assert.Null(item.ValuationSnapshot?.InvalidatedAtUtc);
    }

    [Fact]
    public async Task UpdateReference_DoesNotInvalidate()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        var item = draft.Items.Single();

        var command = CreateCommand(draft) with
        {
            Items =
            [
                new(
                    item.Id, 1, "R-2", item.Description,
                    item.ElementType, item.RawMeasurements,
                    item.WidthMillimeters, item.HeightMillimeters, item.Quantity,
                    item.IsIncluded)
            ]
        };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            PreQuoteDraftValuationStatus.Valued, item.ValuationStatus);
        Assert.Null(item.ValuationSnapshot?.InvalidationReason);
    }

    [Fact]
    public async Task UpdateRawMeasurements_DoesNotInvalidate()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        var item = draft.Items.Single();

        var command = CreateCommand(draft) with
        {
            Items =
            [
                new(
                    item.Id, 1, item.Reference, item.Description,
                    item.ElementType, "120x150 mm",
                    item.WidthMillimeters, item.HeightMillimeters, item.Quantity,
                    item.IsIncluded)
            ]
        };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            PreQuoteDraftValuationStatus.Valued, item.ValuationStatus);
        Assert.Null(item.ValuationSnapshot?.InvalidationReason);
    }

    [Fact]
    public async Task UpdateIsIncluded_RecalculatesSummary()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        var item = draft.Items.Single();

        var command = CreateCommand(draft) with
        {
            Items =
            [
                new(
                    item.Id, 1, item.Reference, item.Description,
                    item.ElementType, item.RawMeasurements,
                    item.WidthMillimeters, item.HeightMillimeters, item.Quantity,
                    false)
            ]
        };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(item.IsIncluded);
        Assert.Equal(0, draft.EconomicSummary.IncludedItemCount);
        Assert.Equal(0, draft.EconomicSummary.ValuedItemCount);
    }

    [Fact]
    public async Task UpdateWithExpectedVersion_PreservesConcurrency()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        var command = CreateCommand(draft) with { PreQuoteId = PreQuoteId, ExpectedVersion = 2 };

        var result = await context.Service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.VersionConflict, result.Failure);
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateQueryFailure_ReturnsQueryError()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        context.Repository.FindForUpdateAsync(
            PreQuoteId,
            UserId,
            Arg.Any<CancellationToken>()).Returns<Task<PreQuoteDraft?>>(
            x => throw new PreQuoteDraftQueryException(new Exception("db")));

        var result = await context.Service.ExecuteAsync(
            CreateCommand(draft),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.QueryError, result.Failure);
        await context.Repository.DidNotReceive().SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatePersistenceFailure_ReturnsPersistenceError()
    {
        var draft = CreateDraftWithValuation();
        var context = CreateContext(draft);
        context.Repository.SaveChangesAsync(
            Arg.Any<CancellationToken>()).Returns<Task>(
            x => throw new PreQuoteDraftPersistenceException(new Exception("db")));

        var result = await context.Service.ExecuteAsync(
            CreateCommand(draft),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(PreQuoteDraftFailure.PersistenceError, result.Failure);
    }

    private static Context CreateContext(PreQuoteDraft draft)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var identity = Substitute.For<IIdentityRepository>();
        var repository = Substitute.For<IPreQuoteDraftRepository>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(UserId);
        identity.FindUserByIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(CreateUser());
        repository.FindForUpdateAsync(
                PreQuoteId,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(draft);
        var service = new UpdatePreQuoteDraftService(
            new UpdatePreQuoteDraftCommandValidator(),
            currentUser,
            identity,
            repository,
            new FixedProvider(At.AddMinutes(1)));
        return new(repository, service);
    }

    private static UpdatePreQuoteDraftCommand CreateCommand(
        PreQuoteDraft draft,
        bool resolveIssue = false,
        bool addManualRows = false,
        bool excludeExistingRequirement = false)
    {
        var item = draft.Items.Single();
        var requirement = draft.Requirements.Single();
        var reference = draft.DocumentReferences.Single();
        var items = new List<PreQuoteDraftItemEdit>
        {
            new(
                item.Id, 1, item.Reference, item.Description,
                item.ElementType, item.RawMeasurements,
                item.WidthMillimeters, item.HeightMillimeters,
                item.Quantity, item.IsIncluded)
        };
        var requirements = new List<PreQuoteDraftRequirementEdit>
        {
            new(
                requirement.Id, 1, requirement.Category,
                requirement.Value, !excludeExistingRequirement)
        };
        var references = new List<PreQuoteDraftReferenceEdit>
        {
            new(
                reference.Id, 1, reference.Reference,
                reference.Description, reference.Detail,
                reference.Quantity, reference.IsIncluded)
        };
        if (addManualRows)
        {
            items.Add(new(
                null, 2, "M-1", "Manual item",
                StructuredElementType.Door, null,
                200, 300, 2, true));
            requirements.Add(new(
                null, 2, RequirementCategory.Finish,
                "Manual requirement", true));
            references.Add(new(
                null, 2, "MR-1", "Manual reference",
                "Detail", 1, true));
        }
        return new(
            PreQuoteId,
            draft.Version,
            "Project",
            "Client",
            "Location",
            items,
            requirements,
            references,
            draft.Issues.Select(x => new PreQuoteDraftResolutionEdit(
                x.Id,
                resolveIssue
                    ? PreQuoteDraftResolutionStatus.Resolved
                    : PreQuoteDraftResolutionStatus.Pending,
                resolveIssue ? "reviewed" : null)).ToArray(),
            draft.Conflicts.Select(x => new PreQuoteDraftResolutionEdit(
                x.Id,
                PreQuoteDraftResolutionStatus.Pending,
                null)).ToArray());
    }

    private static PreQuoteDraft CreateDraft() =>
        CreateDraftWithItems([CreateDraftItem(
            PreQuoteDraftValuationStatus.Pending,
            1,
            "I-1")]);

    private static PreQuoteDraft CreateDraftWithValuation() =>
        CreateDraftWithItems([CreateDraftItem(
            PreQuoteDraftValuationStatus.Valued,
            1,
            "I-1")]);

    private static PreQuoteDraft CreateDraftWithItems(
        PreQuoteDraftItemSource[] items) => PreQuoteDraft.Create(
        PreQuoteId,
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Project",
        "Client",
        "Location",
        UserId,
        At,
        items,
        [new(Guid.NewGuid(), 1,
            RequirementCategory.GeneralNote, "Requirement")],
        [new(Guid.NewGuid(), 1, "R-1", "Reference", null, 1)],
        [new(Guid.NewGuid(), 1,
            StructuredIssueCode.OcrReviewRequired,
            "Issue", 1, [1])],
        [new(Guid.NewGuid(), 1,
            StructuredConflictCode.DuplicateItemReference,
            "Conflict", [1], [1])]);

    private static PreQuoteDraftItemSource CreateDraftItem(
        PreQuoteDraftValuationStatus valuationStatus,
        int sequence,
        string reference)
    {
        PreQuoteDraftItemValuationSnapshotSource? valuation = valuationStatus == PreQuoteDraftValuationStatus.Pending
            ? null
            : new(
                Guid.NewGuid(),
                valuationStatus,
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                100,
                100,
                1,
                1.5m,
                3,
                90000.123456m,
                270000.370368m,
                810001.111104m,
                "COP",
                At.AddMinutes(2),
                null,
                null);

        return new(
            Guid.NewGuid(),
            sequence,
            reference,
            "Item",
            StructuredElementType.Window,
            null,
            100,
            100,
            1,
            null,
            valuation);
    }

    private static User CreateUser() => User.CreateFromGoogle(
        "user@example.com", "Test", "User", null, At);

    private sealed record Context(
        IPreQuoteDraftRepository Repository,
        UpdatePreQuoteDraftService Service);

    private sealed class FixedProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
