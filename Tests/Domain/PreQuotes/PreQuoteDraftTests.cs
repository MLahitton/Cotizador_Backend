using System.Reflection;
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
            1, "Project updated", "Client", "BOGOTA",
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
    public void Create_FromAiInitializesRequestedTechnicalSelection()
    {
        var draft = Create();

        var item = draft.Items.Single();

        Assert.NotNull(item.TechnicalSnapshot);
        Assert.NotNull(item.TechnicalSelection);
        Assert.Equal("K40", item.TechnicalSelection.RequestedSystemCode);
        Assert.Equal("K40", item.TechnicalSelection.RequestedSystemOriginalText);
        Assert.Equal("LAM_4_4", item.TechnicalSelection.RequestedGlassCode);
        Assert.Equal("LAM 4+4", item.TechnicalSelection.RequestedGlassOriginalText);
        Assert.Equal("NATURAL", item.TechnicalSelection.RequestedFinishCode);
        Assert.Equal(PreQuoteDraftTechnicalSelectionState.Pending,
            item.TechnicalSelection.SelectionState);
        Assert.Null(item.TechnicalSelection.SuggestedSystemCode);
        Assert.Null(item.TechnicalSelection.SelectedSystemCode);
    }

    [Fact]
    public void Update_ChangesSelectedTechnicalValuesWithoutChangingSnapshot()
    {
        var draft = Create();
        var item = draft.Items.Single();

        draft.Update(
            1, "Project", "Client", "BOGOTA",
            [new(item.Id, 1, item.Reference, item.Description,
                item.ElementType, item.RawMeasurements,
                item.WidthMillimeters, item.HeightMillimeters,
                item.Quantity, true,
                new("K50", "TEMP_8", "BLACK_MATTE", null, false))],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            PendingIssues(draft),
            PendingConflicts(draft),
            UserId,
            At.AddMinutes(1));

        Assert.Equal("K40", item.TechnicalSnapshot!.SystemCode);
        Assert.Equal("LAM_4_4", item.GlassSnapshot!.NormalizedCodeSnapshot);
        Assert.Equal("K50", item.TechnicalSelection!.SelectedSystemCode);
        Assert.Equal("TEMP_8", item.TechnicalSelection.SelectedGlassCode);
        Assert.Equal("BLACK_MATTE", item.TechnicalSelection.SelectedFinishCode);
        Assert.Equal(PreQuoteDraftTechnicalSelectionState.Modified,
            item.TechnicalSelection.SelectionState);
    }

    [Fact]
    public void TechnicalSelection_ConfirmSelectionUsesSuggestedValues()
    {
        var selection = PreQuoteDraftItemTechnicalSelection.Create(
            Guid.NewGuid(),
            new(
                RequestedSystemCode: null,
                RequestedSystemOriginalText: null,
                SuggestedSystemCode: "K70",
                SuggestedGlassCode: "TEMP_10",
                SuggestedFinishCode: "BLACK_MATTE",
                SelectionState: PreQuoteDraftTechnicalSelectionState.Suggested,
                RequiresReview: false,
                ReviewReasons: [],
                SuggestedSource: PreQuoteDraftTechnicalSelectionSource.Rule));

        selection.UpdateSelected(new(null, null, null, null, true));

        Assert.Equal("K70", selection.SelectedSystemCode);
        Assert.Equal("TEMP_10", selection.SelectedGlassCode);
        Assert.Equal("BLACK_MATTE", selection.SelectedFinishCode);
        Assert.Equal(PreQuoteDraftTechnicalSelectionState.Confirmed,
            selection.SelectionState);
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

    [Fact]
    public void Approve_WithMediumConfidenceAndNoLimitations_Approves()
    {
        var draft = Create();
        UpdateResolutions(draft, PreQuoteDraftResolutionStatus.Resolved,
            PreQuoteDraftResolutionStatus.Dismissed);
        var valuation = draft.Items.Single().ValuationSnapshot!;
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.ConfidenceScore), 64);
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.ConfidenceLevel),
            PreQuoteDraftPricingConfidenceLevel.Medium);

        draft.Approve(2, UserId, At.AddMinutes(3));

        Assert.Equal(PreQuoteDraftStatus.Approved, draft.Status);
    }

    [Theory]
    [InlineData("limited_scope")]
    [InlineData("valuation_requires_review")]
    [InlineData("aluminum_base_rate_not_configured")]
    [InlineData("transport_not_confirmed")]
    [InlineData("project_location_not_confirmed")]
    [InlineData("stale")]
    [InlineData("not_priceable")]
    public void Approve_RejectsEconomicallyBlockedDraftAndPreservesState(
        string scenario)
    {
        var draft = Create();
        UpdateResolutions(draft, PreQuoteDraftResolutionStatus.Resolved,
            PreQuoteDraftResolutionStatus.Dismissed);
        ApplyEconomicBlocker(draft, scenario);
        var status = draft.Status;
        var version = draft.Version;
        var approvedAtUtc = draft.ApprovedAtUtc;

        Assert.Throws<InvalidOperationException>(() =>
            draft.Approve(version, UserId, At.AddMinutes(3)));

        Assert.Equal(status, draft.Status);
        Assert.Equal(version, draft.Version);
        Assert.Equal(approvedAtUtc, draft.ApprovedAtUtc);
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
            draft.Version, "Project", "Client", "BOGOTA",
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
            draft.Version, "Project", "Client", "BOGOTA",
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

    private static PreQuoteDraft Create()
    {
        var draft = PreQuoteDraft.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Project",
            "Client", "BOGOTA", UserId, At,
            [new(Guid.NewGuid(), 1, "I-1", "Item",
                StructuredElementType.Window, null, 100, 100, 1,
                Glass(), Valuation(), Technical())],
            [new(Guid.NewGuid(), 1, RequirementCategory.GeneralNote, "Note")],
            [new(Guid.NewGuid(), 1, "R-1", "Reference", null, 1)],
            [new(Guid.NewGuid(), 1, StructuredIssueCode.OcrReviewRequired,
                "Issue", 1, [1])],
            [new(Guid.NewGuid(), 1,
                StructuredConflictCode.DuplicateItemReference,
                "Conflict", [1], [1])]);

        MakeEconomicallyComplete(draft.Items.Single().ValuationSnapshot!);
        return draft;
    }

    private static PreQuoteDraftItemGlassSnapshotSource Glass() => new(
        Guid.NewGuid(), Guid.NewGuid(), "LAM 4+4", "LAM_4_4",
        GlassAssignmentScope.Item, false, [], [1],
        [new(1, 1, EvidenceSourceType.Native, "LAM 4+4")]);

    private static PreQuoteDraftItemValuationSnapshotSource Valuation() => new(
        Guid.NewGuid(), PreQuoteDraftValuationStatus.Valued, null,
        Guid.NewGuid(), Guid.NewGuid(), 100, 100, 1, 1m, 1m, 90000m,
        90000m, 90000m, "COP", At, null, null, 1, 90000m,
        90000m, 90000m);

    private static PreQuoteDraftItemTechnicalSnapshotSource Technical() => new(
        Guid.NewGuid(), "K40", "K40", TechnicalClassificationSource.Explicit,
        0.95m, "MARCO_47", "SG0047", TechnicalClassificationSource.Alias,
        0.95m, "NATURAL", "NATURAL", TechnicalClassificationSource.Explicit,
        0.95m, false, []);

    private static void MakeEconomicallyComplete(
        PreQuoteDraftItemValuationSnapshot valuation)
    {
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.ItemMinimumAmount),
            90000m);
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.ItemExpectedAmount),
            90000m);
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.ItemMaximumAmount),
            90000m);
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.RequiresReview),
            false);
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.Assumptions),
            Array.Empty<string>());
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.MissingData),
            Array.Empty<string>());
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.ConfidenceScore),
            95);
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.ConfidenceLevel),
            PreQuoteDraftPricingConfidenceLevel.High);
        Set(valuation, nameof(PreQuoteDraftItemValuationSnapshot.CalculatedAtUtc),
            At);
    }

    private static void ApplyEconomicBlocker(
        PreQuoteDraft draft,
        string scenario)
    {
        var item = draft.Items.Single();
        var valuation = item.ValuationSnapshot!;
        switch (scenario)
        {
            case "limited_scope":
                Set(valuation,
                    nameof(PreQuoteDraftItemValuationSnapshot.Assumptions),
                    new[] { "NON_BLOCKING_LIMITATION" });
                break;
            case "valuation_requires_review":
                Set(valuation,
                    nameof(PreQuoteDraftItemValuationSnapshot.RequiresReview),
                    true);
                break;
            case "aluminum_base_rate_not_configured":
                Set(valuation,
                    nameof(PreQuoteDraftItemValuationSnapshot.Assumptions),
                    new[] { "ALUMINUM_BASE_RATE_NOT_CONFIGURED" });
                break;
            case "transport_not_confirmed":
                Set(valuation,
                    nameof(PreQuoteDraftItemValuationSnapshot.Assumptions),
                    new[] { "TRANSPORT_NOT_CONFIRMED" });
                break;
            case "project_location_not_confirmed":
                Set(valuation,
                    nameof(PreQuoteDraftItemValuationSnapshot.MissingData),
                    new[] { "PROJECT_LOCATION_NOT_CONFIRMED" });
                break;
            case "stale":
                Set(item, nameof(PreQuoteDraftItem.ValuationStatus),
                    PreQuoteDraftValuationStatus.Stale);
                Set(valuation,
                    nameof(PreQuoteDraftItemValuationSnapshot.InvalidatedAtUtc),
                    At.AddMinutes(2));
                break;
            case "not_priceable":
                Set(item, nameof(PreQuoteDraftItem.ValuationStatus),
                    PreQuoteDraftValuationStatus.NotPriceable);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario,
                    null);
        }
    }

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }

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
