using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.UpdatePreQuoteDraft;
using Domain.Identity;
using Domain.PreQuotes;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class UpdatePreQuoteDraftCompleteRequestTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PreQuoteId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execute_CompleteSmokeRequest_UpdatesEntireDraft()
    {
        var draft = CreateDraft();
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
            Substitute.For<IProductSystemCatalogRepository>(),
            Substitute.For<IGlassTypeCatalogRepository>(),
            Substitute.For<IFinishTypeCatalogRepository>(),
            new FixedProvider(At.AddMinutes(1)));

        var command = CreateCommand(draft);
        var result = await service.ExecuteAsync(
            command,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(draft, result.Draft);
        Assert.Equal(PreQuoteDraftStatus.InReview, draft.Status);
        Assert.Equal(2, draft.Version);
        Assert.Equal(7, draft.Items.Count);
        Assert.Equal(7, draft.Items.Count(x => x.IsIncluded));
        Assert.Equal(0, draft.Items.Count(x => !x.IsIncluded));
        Assert.Equal(
            1,
            draft.Items.Count(x => x.Origin == PreQuoteDraftOrigin.Manual));
        Assert.Equal(
            0,
            draft.Items.Count(x => x.IsIncluded && !x.IsCompleteForApproval));
        Assert.Equal(
            19,
            draft.Items.Where(x => x.IsIncluded).Sum(x => x.Quantity ?? 0));
        Assert.Equal(13, draft.Requirements.Count);
        Assert.Equal(12, draft.Requirements.Count(x => x.IsIncluded));
        Assert.Equal(3, draft.DocumentReferences.Count);
        Assert.Equal(3, draft.DocumentReferences.Count(x => x.IsIncluded));
        Assert.Equal(
            0,
            draft.Issues.Count(
                x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Pending));
        Assert.Equal(
            1,
            draft.Issues.Count(
                x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Resolved));
        Assert.Empty(draft.Conflicts);

        var manualItem = draft.Items.Single(
            x => x.Origin == PreQuoteDraftOrigin.Manual);
        Assert.Equal("L-01", manualItem.Reference);
        Assert.Equal(StructuredElementType.Skylight, manualItem.ElementType);
        Assert.Null(manualItem.SourceItemSequence);
        Assert.Null(draft.Requirements.Single(
            x => x.Origin == PreQuoteDraftOrigin.Manual)
            .SourceRequirementSequence);
        Assert.Null(draft.DocumentReferences.Single(
            x => x.Origin == PreQuoteDraftOrigin.Manual)
            .SourceDocumentReferenceSequence);

        var issue = draft.Issues.Single();
        Assert.Equal(
            PreQuoteDraftResolutionStatus.Resolved,
            issue.ResolutionStatus);
        Assert.Equal("Validado por el usuario.", issue.ResolutionNote);
        Assert.Equal(UserId, issue.ResolvedByUserId);
        Assert.Equal(At.AddMinutes(1), issue.ResolvedAtUtc);
        await repository.Received(1).SaveChangesAsync(
            Arg.Any<CancellationToken>());
    }

    private static UpdatePreQuoteDraftCommand CreateCommand(
        PreQuoteDraft draft)
    {
        var items = draft.Items.OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftItemEdit(
                x.Id, x.Sequence, x.Reference, x.Description,
                x.ElementType, x.RawMeasurements, x.WidthMillimeters,
                x.HeightMillimeters, x.Quantity, true))
            .Append(new(
                null, 7, "L-01", "Lucernario manual",
                StructuredElementType.Skylight, null,
                2000, 1400, 2, true))
            .ToArray();
        var requirements = draft.Requirements.OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftRequirementEdit(
                x.Id, x.Sequence, x.Category, x.Value,
                x.Sequence != 8))
            .Append(new(
                null, 13, RequirementCategory.GeneralNote,
                "Requirement manual", true))
            .ToArray();
        var references = draft.DocumentReferences.OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftReferenceEdit(
                x.Id, x.Sequence, x.Reference, x.Description,
                x.Detail, x.Quantity, true))
            .Append(new(
                null, 3, "R-03", "Referencia manual",
                null, 1, true))
            .ToArray();
        return new(
            PreQuoteId,
            1,
            "Project",
            "Client",
            "Location",
            items,
            requirements,
            references,
            [new(
                draft.Issues.Single().Id,
                PreQuoteDraftResolutionStatus.Resolved,
                "Validado por el usuario.")],
            []);
    }

    private static PreQuoteDraft CreateDraft()
    {
        var quantities = new[] { 1, 2, 3, 4, 3, 4 };
        return PreQuoteDraft.Create(
            PreQuoteId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Project",
            "Client",
            "Location",
            UserId,
            At,
            quantities.Select((quantity, index) =>
                new PreQuoteDraftItemSource(
                    Guid.NewGuid(),
                    index + 1,
                    index == 0 ? null : $"I-{index + 1:00}",
                    $"Item {index + 1}",
                    StructuredElementType.Window,
                    null,
                    1000,
                    1000,
                    quantity)).ToArray(),
            Enumerable.Range(1, 12).Select(sequence =>
                new PreQuoteDraftRequirementSource(
                    Guid.NewGuid(),
                    sequence,
                    RequirementCategory.GeneralNote,
                    $"Requirement {sequence}")).ToArray(),
            Enumerable.Range(1, 2).Select(sequence =>
                new PreQuoteDraftReferenceSource(
                    Guid.NewGuid(),
                    sequence,
                    $"R-{sequence:00}",
                    $"Reference {sequence}",
                    null,
                    1)).ToArray(),
            [new(
                Guid.NewGuid(),
                1,
                StructuredIssueCode.OcrReviewRequired,
                "Issue",
                1,
                [1])],
            []);
    }

    private static User CreateUser() => User.CreateFromGoogle(
        "user@example.com", "Test", "User", null, At);

    private sealed class FixedProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
