using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Domain.PreQuotes;

public sealed class PreQuoteDraftEditingSemanticsTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset At =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Update_AcceptsEmptyPersistedAndRequestedConflicts()
    {
        var draft = Create();

        Update(draft);

        Assert.Empty(draft.Conflicts);
        Assert.Equal(PreQuoteDraftStatus.InReview, draft.Status);
    }

    [Fact]
    public void Update_AcceptsNewRowsWithNullIds()
    {
        var draft = Create();

        Update(draft, addManualRows: true);

        Assert.Equal(2, draft.Items.Count);
        Assert.Equal(2, draft.Requirements.Count);
        Assert.Equal(2, draft.DocumentReferences.Count);
        Assert.All(
            new[]
            {
                draft.Items.Single(x => x.Sequence == 2).Origin,
                draft.Requirements.Single(x => x.Sequence == 2).Origin,
                draft.DocumentReferences.Single(x => x.Sequence == 2).Origin
            },
            origin => Assert.Equal(PreQuoteDraftOrigin.Manual, origin));
    }

    [Fact]
    public void Update_AcceptsExcludedExistingRequirement()
    {
        var draft = Create();

        Update(draft, includeRequirement: false);

        Assert.False(draft.Requirements.Single().IsIncluded);
    }

    [Fact]
    public void Update_AcceptsSkylight()
    {
        var draft = Create();

        Update(draft, elementType: StructuredElementType.Skylight);

        Assert.Equal(
            StructuredElementType.Skylight,
            draft.Items.Single().ElementType);
        Assert.True(draft.Items.Single().IsCompleteForApproval);
    }

    [Fact]
    public void UpdateAndApprove_DoNotRequireItemReference()
    {
        var draft = Create(itemReference: null);

        Update(draft);
        draft.Approve(2, UserId, At.AddMinutes(2));

        Assert.Null(draft.Items.Single().Reference);
        Assert.Equal(PreQuoteDraftStatus.Approved, draft.Status);
    }

    private static void Update(
        PreQuoteDraft draft,
        bool addManualRows = false,
        bool includeRequirement = true,
        StructuredElementType elementType = StructuredElementType.Window)
    {
        var item = draft.Items.Single();
        var requirement = draft.Requirements.Single();
        var reference = draft.DocumentReferences.Single();
        var items = new List<PreQuoteDraftItemEdit>
        {
            new(
                item.Id, 1, item.Reference, item.Description,
                elementType, item.RawMeasurements,
                item.WidthMillimeters, item.HeightMillimeters,
                item.Quantity, true)
        };
        var requirements = new List<PreQuoteDraftRequirementEdit>
        {
            new(
                requirement.Id, 1, requirement.Category,
                requirement.Value, includeRequirement)
        };
        var references = new List<PreQuoteDraftReferenceEdit>
        {
            new(
                reference.Id, 1, reference.Reference,
                reference.Description, reference.Detail,
                reference.Quantity, true)
        };
        if (addManualRows)
        {
            items.Add(new(
                null, 2, "M-1", "Manual item",
                StructuredElementType.Door, null,
                100, 100, 1, true));
            requirements.Add(new(
                null, 2, RequirementCategory.Finish,
                "Manual requirement", true));
            references.Add(new(
                null, 2, "MR-1", "Manual reference",
                null, 1, true));
        }
        draft.Update(
            1,
            "Project",
            "Client",
            "Location",
            items,
            requirements,
            references,
            [],
            [],
            UserId,
            At.AddMinutes(1));
    }

    private static PreQuoteDraft Create(string? itemReference = "I-1") =>
        PreQuoteDraft.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Project",
            "Client",
            "Location",
            UserId,
            At,
            [new(
                Guid.NewGuid(),
                1,
                itemReference,
                "Item",
                StructuredElementType.Window,
                null,
                100,
                100,
                1)],
            [new(
                Guid.NewGuid(),
                1,
                RequirementCategory.GeneralNote,
                "Requirement")],
            [new(
                Guid.NewGuid(),
                1,
                "R-1",
                "Reference",
                null,
                1)],
            [],
            []);
}
