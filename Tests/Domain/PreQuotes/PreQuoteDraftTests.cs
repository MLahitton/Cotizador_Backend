using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class PreQuoteDraftTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_CopiesSnapshotAndInitializesLifecycle()
    {
        var draft = Create();

        Assert.Equal(PreQuoteDraftStatus.PendingReview, draft.Status);
        Assert.Equal(1, draft.Version);
        Assert.Single(draft.Items);
        Assert.Single(draft.Requirements);
        Assert.Single(draft.DocumentReferences);
        Assert.Single(draft.Issues);
        Assert.Single(draft.Conflicts);
        Assert.All(draft.Items, x => Assert.Equal(PreQuoteDraftOrigin.Ai, x.Origin));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, null, 1)]
    public void Update_RejectsInvalidMeasurements(
        int? width, int? height, int? quantity)
    {
        var draft = Create();
        var item = draft.Items.Single();

        Assert.Throws<ArgumentException>(() => draft.Update(
            1, "Project", null, null,
            [new(item.Id, 1, null, "Item", StructuredElementType.Window,
                null, width, height, quantity, true)],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            PendingIssues(draft),
            PendingConflicts(draft),
            UserId, At.AddMinutes(1)));
    }

    [Fact]
    public void Update_AddsManualRowsAndIncrementsVersion()
    {
        var draft = Create();
        var item = draft.Items.Single();
        var requirement = draft.Requirements.Single();
        var reference = draft.DocumentReferences.Single();

        draft.Update(
            1, "Project updated", "Client", "Location",
            [
                new(item.Id, 1, null, "AI", StructuredElementType.Window,
                    null, 100, 100, 1, false),
                new(null, 2, null, "Manual", StructuredElementType.Door,
                    null, 200, 300, 2, true)
            ],
            [
                new(requirement.Id, 1, RequirementCategory.GeneralNote,
                    "AI", true),
                new(null, 2, RequirementCategory.Finish, "Manual", true)
            ],
            [
                new(reference.Id, 1, null, "AI", null, 1, true),
                new(null, 2, null, "Manual", null, 1, true)
            ],
            PendingIssues(draft),
            PendingConflicts(draft),
            UserId, At.AddMinutes(1));

        Assert.Equal(PreQuoteDraftStatus.InReview, draft.Status);
        Assert.Equal(2, draft.Version);
        Assert.False(draft.Items.Single(x => x.Id == item.Id).IsIncluded);
        Assert.Equal(
            PreQuoteDraftOrigin.Manual,
            draft.Items.Single(x => x.Sequence == 2).Origin);
    }

    [Fact]
    public void Update_RejectsWrongVersion()
    {
        var draft = Create();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            draft.Update(2, null, null, null, [], [], [], [], [],
                UserId, At.AddMinutes(1)));
        Assert.Equal("VERSION_CONFLICT", exception.Message);
    }

    [Fact]
    public void Update_RejectsOmittedExistingRows()
    {
        var draft = Create();
        Assert.Throws<ArgumentException>(() => draft.Update(
            1, null, null, null, [], ExistingRequirements(draft),
            ExistingReferences(draft), PendingIssues(draft),
            PendingConflicts(draft), UserId, At.AddMinutes(1)));
    }

    [Fact]
    public void Findings_CanResolveDismissAndReturnToPending()
    {
        var draft = Create();
        var issue = draft.Issues.Single();
        var conflict = draft.Conflicts.Single();
        UpdateResolutions(draft, PreQuoteDraftResolutionStatus.Resolved,
            PreQuoteDraftResolutionStatus.Dismissed);
        Assert.Equal(PreQuoteDraftResolutionStatus.Resolved,
            issue.ResolutionStatus);
        Assert.Equal(PreQuoteDraftResolutionStatus.Dismissed,
            conflict.ResolutionStatus);

        UpdateResolutions(draft, PreQuoteDraftResolutionStatus.Pending,
            PreQuoteDraftResolutionStatus.Pending);
        Assert.Null(issue.ResolutionNote);
        Assert.Null(conflict.ResolvedAtUtc);
    }

    [Fact]
    public void Update_AllowsIssueToRemainPending()
    {
        var draft = Create();

        UpdateDraft(draft);

        var issue = draft.Issues.Single();
        Assert.Equal(PreQuoteDraftResolutionStatus.Pending, issue.ResolutionStatus);
        Assert.Null(issue.ResolutionNote);
        Assert.Null(issue.ResolvedByUserId);
        Assert.Null(issue.ResolvedAtUtc);
    }

    [Fact]
    public void Update_AllowsConflictToRemainPending()
    {
        var draft = Create();

        UpdateDraft(draft);

        var conflict = draft.Conflicts.Single();
        Assert.Equal(PreQuoteDraftResolutionStatus.Pending, conflict.ResolutionStatus);
        Assert.Null(conflict.ResolutionNote);
        Assert.Null(conflict.ResolvedByUserId);
        Assert.Null(conflict.ResolvedAtUtc);
    }

    [Theory]
    [InlineData(PreQuoteDraftResolutionStatus.Resolved)]
    [InlineData(PreQuoteDraftResolutionStatus.Dismissed)]
    public void Update_ResolvesOrDismissesPendingIssue(
        PreQuoteDraftResolutionStatus status)
    {
        var draft = Create();

        UpdateDraft(draft, issueStatus: status);

        var issue = draft.Issues.Single();
        Assert.Equal(status, issue.ResolutionStatus);
        Assert.Equal("issue note", issue.ResolutionNote);
        Assert.Equal(UserId, issue.ResolvedByUserId);
        Assert.Equal(At.AddMinutes(1), issue.ResolvedAtUtc);
    }

    [Fact]
    public void Update_AllowsResolvedIssueToReturnToPending()
    {
        var draft = Create();
        UpdateDraft(
            draft,
            issueStatus: PreQuoteDraftResolutionStatus.Resolved);

        UpdateDraft(draft);

        var issue = draft.Issues.Single();
        Assert.Equal(PreQuoteDraftResolutionStatus.Pending, issue.ResolutionStatus);
        Assert.Null(issue.ResolutionNote);
        Assert.Null(issue.ResolvedByUserId);
        Assert.Null(issue.ResolvedAtUtc);
    }

    [Fact]
    public void Update_AllowsPendingConflictToBeResolved()
    {
        var draft = Create();

        UpdateDraft(
            draft,
            conflictStatus: PreQuoteDraftResolutionStatus.Resolved);

        var conflict = draft.Conflicts.Single();
        Assert.Equal(
            PreQuoteDraftResolutionStatus.Resolved,
            conflict.ResolutionStatus);
        Assert.Equal("conflict note", conflict.ResolutionNote);
        Assert.Equal(UserId, conflict.ResolvedByUserId);
        Assert.Equal(At.AddMinutes(1), conflict.ResolvedAtUtc);
    }

    [Fact]
    public void Update_AllowsIncludedIncompleteItemDuringReview()
    {
        var draft = Create();

        UpdateDraft(draft, width: null, height: null, quantity: null);

        Assert.False(draft.Items.Single().IsCompleteForApproval);
        Assert.Equal(PreQuoteDraftStatus.InReview, draft.Status);
        Assert.Equal(2, draft.Version);
    }

    [Fact]
    public void Update_AllowsIncludedOtherItemDuringReview()
    {
        var draft = Create();

        UpdateDraft(draft, elementType: StructuredElementType.Other);

        Assert.Equal(StructuredElementType.Other, draft.Items.Single().ElementType);
        Assert.False(draft.Items.Single().IsCompleteForApproval);
        Assert.Equal(PreQuoteDraftStatus.InReview, draft.Status);
        Assert.Equal(2, draft.Version);
    }

    [Fact]
    public void Approve_WithCompleteResolvedDraft_ApprovesAndAudits()
    {
        var draft = Create();
        UpdateResolutions(draft, PreQuoteDraftResolutionStatus.Resolved,
            PreQuoteDraftResolutionStatus.Dismissed);

        draft.Approve(2, UserId, At.AddMinutes(3));

        Assert.Equal(PreQuoteDraftStatus.Approved, draft.Status);
        Assert.Equal(3, draft.Version);
        Assert.Equal(UserId, draft.ApprovedByUserId);
        Assert.Throws<InvalidOperationException>(() =>
            draft.Approve(3, UserId, At.AddMinutes(4)));
    }

    [Theory]
    [InlineData("issue")]
    [InlineData("conflict")]
    [InlineData("no_items")]
    [InlineData("incomplete")]
    [InlineData("other")]
    public void Approve_RejectsInvalidContent(string scenario)
    {
        var draft = Create();
        var item = draft.Items.Single();
        draft.Update(
            1, "Project", null, null,
            [new(item.Id, 1, null, "Item",
                scenario == "other" ? StructuredElementType.Other
                    : StructuredElementType.Window,
                null,
                scenario == "incomplete" ? null : 100,
                scenario == "incomplete" ? null : 100,
                scenario == "incomplete" ? null : 1,
                scenario != "no_items")],
            ExistingRequirements(draft), ExistingReferences(draft),
            [new(draft.Issues.Single().Id,
                scenario == "issue" ? PreQuoteDraftResolutionStatus.Pending
                    : PreQuoteDraftResolutionStatus.Resolved,
                scenario == "issue" ? null : "ok")],
            [new(draft.Conflicts.Single().Id,
                scenario == "conflict" ? PreQuoteDraftResolutionStatus.Pending
                    : PreQuoteDraftResolutionStatus.Resolved,
                scenario == "conflict" ? null : "ok")],
            UserId, At.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            draft.Approve(2, UserId, At.AddMinutes(2)));
    }

    private static void UpdateResolutions(
        PreQuoteDraft draft,
        PreQuoteDraftResolutionStatus issueStatus,
        PreQuoteDraftResolutionStatus conflictStatus)
    {
        var item = draft.Items.Single();
        draft.Update(
            draft.Version, "Project", null, null,
            [new(item.Id, 1, null, "Item", StructuredElementType.Window,
                null, 100, 100, 1, true)],
            ExistingRequirements(draft), ExistingReferences(draft),
            [new(draft.Issues.Single().Id, issueStatus,
                issueStatus == PreQuoteDraftResolutionStatus.Pending
                    ? null : "resolved")],
            [new(draft.Conflicts.Single().Id, conflictStatus,
                conflictStatus == PreQuoteDraftResolutionStatus.Pending
                    ? null : "resolved")],
            UserId, At.AddMinutes(draft.Version));
    }

    private static void UpdateDraft(
        PreQuoteDraft draft,
        PreQuoteDraftResolutionStatus issueStatus =
            PreQuoteDraftResolutionStatus.Pending,
        PreQuoteDraftResolutionStatus conflictStatus =
            PreQuoteDraftResolutionStatus.Pending,
        StructuredElementType elementType = StructuredElementType.Window,
        int? width = 100,
        int? height = 100,
        int? quantity = 1)
    {
        var item = draft.Items.Single();
        draft.Update(
            draft.Version, "Project", "Client", "Location",
            [new(item.Id, 1, item.Reference, item.Description, elementType,
                item.RawMeasurements, width, height, quantity, true)],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [new(draft.Issues.Single().Id, issueStatus,
                issueStatus == PreQuoteDraftResolutionStatus.Pending
                    ? null : "issue note")],
            [new(draft.Conflicts.Single().Id, conflictStatus,
                conflictStatus == PreQuoteDraftResolutionStatus.Pending
                    ? null : "conflict note")],
            UserId,
            At.AddMinutes(draft.Version));
    }

    private static PreQuoteDraft Create() => PreQuoteDraft.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Project",
        "Client", "Location", UserId, At,
        [new(Guid.NewGuid(), 1, "I-1", "Item",
            StructuredElementType.Window, null, 100, 100, 1)],
        [new(Guid.NewGuid(), 1, RequirementCategory.GeneralNote, "Note")],
        [new(Guid.NewGuid(), 1, "R-1", "Reference", null, 1)],
        [new(Guid.NewGuid(), 1, StructuredIssueCode.OcrReviewRequired,
            "Issue", 1, [1])],
        [new(Guid.NewGuid(), 1,
            StructuredConflictCode.DuplicateItemReference,
            "Conflict", [1], [1])]);

    private static PreQuoteDraftRequirementEdit[] ExistingRequirements(
        PreQuoteDraft draft) =>
        draft.Requirements.OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftRequirementEdit(
                x.Id, x.Sequence, x.Category, x.Value, x.IsIncluded)).ToArray();
    private static PreQuoteDraftReferenceEdit[] ExistingReferences(
        PreQuoteDraft draft) =>
        draft.DocumentReferences.OrderBy(x => x.Sequence)
            .Select(x => new PreQuoteDraftReferenceEdit(
                x.Id, x.Sequence, x.Reference, x.Description,
                x.Detail, x.Quantity, x.IsIncluded)).ToArray();
    private static PreQuoteDraftResolutionEdit[] PendingIssues(
        PreQuoteDraft draft) =>
        draft.Issues.Select(x => new PreQuoteDraftResolutionEdit(
            x.Id, PreQuoteDraftResolutionStatus.Pending, null)).ToArray();
    private static PreQuoteDraftResolutionEdit[] PendingConflicts(
        PreQuoteDraft draft) =>
        draft.Conflicts.Select(x => new PreQuoteDraftResolutionEdit(
            x.Id, PreQuoteDraftResolutionStatus.Pending, null)).ToArray();
}
