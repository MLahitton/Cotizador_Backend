using System.Reflection;
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

    [Fact]
    public void UpdateWidth_MarksValuationStale()
    {
        var draft = CreateWithValuation();
        var item = draft.Items.Single();
        var valuedAmount = item.ValuationSnapshot!.TotalAmount;

        draft.Update(
            1,
            "Project",
            "Client",
            "BOGOTA",
            [CreateItemEdit(item, widthMillimeters: 120)],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [],
            [],
            UserId,
            At.AddMinutes(1));

        Assert.Equal(PreQuoteDraftValuationStatus.Stale, item.ValuationStatus);
        Assert.Equal(
            PreQuoteDraftValuationInvalidationReason.WidthChanged,
            item.ValuationSnapshot!.InvalidationReason);
        Assert.Equal(At.AddMinutes(1), item.ValuationSnapshot.InvalidatedAtUtc);
        Assert.Equal(valuedAmount, item.ValuationSnapshot.TotalAmount);
    }

    [Fact]
    public void UpdateHeight_MarksValuationStale()
    {
        var draft = CreateWithValuation();
        var item = draft.Items.Single();
        var valuedArea = item.ValuationSnapshot!.TotalAreaSquareMeters;

        draft.Update(
            1,
            "Project",
            "Client",
            "BOGOTA",
            [CreateItemEdit(item, heightMillimeters: 120)],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [],
            [],
            UserId,
            At.AddMinutes(1));

        Assert.Equal(PreQuoteDraftValuationStatus.Stale, item.ValuationStatus);
        Assert.Equal(
            PreQuoteDraftValuationInvalidationReason.HeightChanged,
            item.ValuationSnapshot!.InvalidationReason);
        Assert.Equal(At.AddMinutes(1), item.ValuationSnapshot.InvalidatedAtUtc);
        Assert.Equal(valuedArea, item.ValuationSnapshot.TotalAreaSquareMeters);
    }

    [Fact]
    public void UpdateQuantity_MarksValuationStale()
    {
        var draft = CreateWithValuation();
        var item = draft.Items.Single();
        var valuedSubtotal = item.ValuationSnapshot!.TotalAmount;

        draft.Update(
            1,
            "Project",
            "Client",
            "BOGOTA",
            [CreateItemEdit(item, quantity: 5)],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [],
            [],
            UserId,
            At.AddMinutes(1));

        Assert.Equal(PreQuoteDraftValuationStatus.Stale, item.ValuationStatus);
        Assert.Equal(
            PreQuoteDraftValuationInvalidationReason.QuantityChanged,
            item.ValuationSnapshot!.InvalidationReason);
        Assert.Equal(At.AddMinutes(1), item.ValuationSnapshot.InvalidatedAtUtc);
        Assert.Equal(valuedSubtotal, item.ValuationSnapshot.TotalAmount);
    }

    [Fact]
    public void UpdateMultipleInputs_UsesMultipleInputsReason()
    {
        var draft = CreateWithValuation();
        var item = draft.Items.Single();

        draft.Update(
            1,
            "Project",
            "Client",
            "BOGOTA",
            [CreateItemEdit(item, widthMillimeters: 120, heightMillimeters: 120)],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [],
            [],
            UserId,
            At.AddMinutes(1));

        Assert.Equal(PreQuoteDraftValuationStatus.Stale, item.ValuationStatus);
        Assert.Equal(
            PreQuoteDraftValuationInvalidationReason.MultipleInputsChanged,
            item.ValuationSnapshot!.InvalidationReason);
    }

    [Fact]
    public void UpdateDescription_PreservesValued()
    {
        var draft = CreateWithValuation();
        var item = draft.Items.Single();

        draft.Update(
            1,
            "Project",
            "Client",
            "BOGOTA",
            [CreateItemEdit(item, description: "Description updated")],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [],
            [],
            UserId,
            At.AddMinutes(1));

        Assert.Equal(PreQuoteDraftValuationStatus.Valued, item.ValuationStatus);
        Assert.Equal("Description updated", item.Description);
        Assert.Null(item.ValuationSnapshot!.InvalidationReason);
        Assert.Null(item.ValuationSnapshot.InvalidatedAtUtc);
    }

    [Fact]
    public void UpdateReference_PreservesValued()
    {
        var draft = CreateWithValuation();
        var item = draft.Items.Single();

        draft.Update(
            1,
            "Project",
            "Client",
            "BOGOTA",
            [CreateItemEdit(item, reference: "R-2")],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [],
            [],
            UserId,
            At.AddMinutes(1));

        Assert.Equal(PreQuoteDraftValuationStatus.Valued, item.ValuationStatus);
        Assert.Equal("R-2", item.Reference);
        Assert.Null(item.ValuationSnapshot!.InvalidationReason);
        Assert.Null(item.ValuationSnapshot.InvalidatedAtUtc);
    }

    [Fact]
    public void UpdateRawMeasurements_PreservesValued()
    {
        var draft = CreateWithValuation();
        var item = draft.Items.Single();

        draft.Update(
            1,
            "Project",
            "Client",
            "BOGOTA",
            [CreateItemEdit(
                item,
                rawMeasurements: "90x120 mm")],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [],
            [],
            UserId,
            At.AddMinutes(1));

        Assert.Equal(PreQuoteDraftValuationStatus.Valued, item.ValuationStatus);
        Assert.Equal("90x120 mm", item.RawMeasurements);
        Assert.Null(item.ValuationSnapshot!.InvalidationReason);
        Assert.Null(item.ValuationSnapshot.InvalidatedAtUtc);
    }

    [Fact]
    public void UpdateIsIncluded_PreservesSnapshot()
    {
        var draft = CreateWithValuation();
        var item = draft.Items.Single();
        var snapshotId = item.ValuationSnapshot!.Id;

        draft.Update(
            1,
            "Project",
            "Client",
            "BOGOTA",
            [CreateItemEdit(item, isIncluded: false)],
            ExistingRequirements(draft),
            ExistingReferences(draft),
            [],
            [],
            UserId,
            At.AddMinutes(1));

        Assert.False(item.IsIncluded);
        Assert.Equal(snapshotId, item.ValuationSnapshot!.Id);
        Assert.Equal(PreQuoteDraftValuationStatus.Valued, item.ValuationStatus);
        Assert.Null(item.ValuationSnapshot.InvalidationReason);
        Assert.Null(item.ValuationSnapshot.InvalidatedAtUtc);
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
            "BOGOTA",
            items,
            requirements,
            references,
            [],
            [],
            UserId,
            At.AddMinutes(1));
    }

    private static PreQuoteDraft Create(string? itemReference = "I-1")
    {
        var draft = PreQuoteDraft.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Project",
            "Client",
            "BOGOTA",
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
                1,
                null,
                new(
                    Guid.NewGuid(),
                    PreQuoteDraftValuationStatus.Valued,
                    null,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    100,
                    100,
                    1,
                    1.000000m,
                    1.000000m,
                    90000m,
                    90000m,
                    90000m,
                    "COP",
                    At,
                    null,
                    null,
                    1,
                    90000m,
                    90000m,
                    90000m))],
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

        MakeEconomicallyComplete(draft.Items.Single().ValuationSnapshot!);
        return draft;
    }

    private static PreQuoteDraft CreateWithValuation() =>
        PreQuoteDraft.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Project",
            "Client",
            "BOGOTA",
            UserId,
            At,
            [
                new(
                    Guid.NewGuid(),
                    1,
                    "I-1",
                    "Item",
                    StructuredElementType.Window,
                    "100 x 100 mm",
                    100,
                    100,
                    1,
                    null,
                    new(
                        Guid.NewGuid(),
                        PreQuoteDraftValuationStatus.Valued,
                        null,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        100,
                        100,
                        1,
                        1.500000m,
                        4.500000m,
                        90000.123456m,
                        270000.370368m,
                        810001.111104m,
                        "COP",
                        At.AddMinutes(2),
                        null,
                        null))
            ],
            [],
            [],
            [],
            []);

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

    private static void Set<T>(object target, string propertyName, T value)
    {
        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(target, value);
    }

    private static PreQuoteDraftItemEdit CreateItemEdit(
        PreQuoteDraftItem item,
        string? reference = null,
        string description = "Item",
        int? widthMillimeters = null,
        int? heightMillimeters = null,
        int? quantity = null,
        string? rawMeasurements = null,
        bool? isIncluded = null)
    {
        return new(
            item.Id,
            item.Sequence,
            reference ?? item.Reference,
            description,
            item.ElementType,
            rawMeasurements ?? item.RawMeasurements,
            widthMillimeters ?? item.WidthMillimeters,
            heightMillimeters ?? item.HeightMillimeters,
            quantity ?? item.Quantity,
            isIncluded ?? item.IsIncluded);
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
}