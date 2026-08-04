namespace Domain.PreQuotes;

public enum PreQuoteDraftStatus { PendingReview = 1, InReview, Approved }
public enum PreQuoteDraftOrigin { Ai = 1, Manual }
public enum PreQuoteDraftResolutionStatus { Pending = 1, Resolved, Dismissed }
public enum PreQuoteDraftValuationStatus
{
    NotApplicable,
    Pending,
    Valued,
    Stale,
    RequiresReview
}
public enum PreQuoteDraftValuationInvalidationReason
{
    MultipleInputsChanged,
    WidthChanged,
    HeightChanged,
    QuantityChanged
}

public sealed record PreQuoteDraftItemGlassReviewReasonSource(
    int Sequence, GlassReviewReason Code);
public sealed record PreQuoteDraftItemGlassSourcePageSource(
    int Sequence, int PageNumber);
public sealed record PreQuoteDraftItemGlassEvidenceSource(
    int Sequence, int PageNumber, EvidenceSourceType SourceType,
    string Text);
public sealed record PreQuoteDraftItemGlassSnapshotSource(
    Guid SourceStructuredItemGlassId, Guid? GlassTypeId,
    string? RawSpecification, string? NormalizedCodeSnapshot,
    GlassAssignmentScope AssignmentScope, bool RequiresReview,
    IReadOnlyList<GlassReviewReason> ReviewReasons,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<PreQuoteDraftItemGlassEvidenceSource> Evidence);
public sealed record PreQuoteDraftItemValuationSnapshotSource(
    Guid SourceStructuredItemValuationId,
    PreQuoteDraftValuationStatus Status,
    GlassValuationReason? Reason,
    Guid? GlassTypeId,
    Guid? GlassPriceRangeVersionId,
    int? WidthMillimetersUsed,
    int? HeightMillimetersUsed,
    int? QuantityUsed,
    decimal? UnitAreaSquareMeters,
    decimal? TotalAreaSquareMeters,
    decimal? UnitPricePerSquareMeter,
    decimal? UnitAmount,
    decimal? TotalAmount,
    string? Currency,
    DateTimeOffset ValuedAtUtc,
    DateTimeOffset? InvalidatedAtUtc,
    PreQuoteDraftValuationInvalidationReason? InvalidationReason);

public sealed class PreQuoteDraftItemGlassSnapshot
{
    public Guid Id { get; private set; }
    public Guid PreQuoteDraftItemId { get; private set; }
    public Guid SourceStructuredItemGlassId { get; private set; }
    public Guid? GlassTypeId { get; private set; }
    public string? RawSpecification { get; private set; }
    public string? NormalizedCodeSnapshot { get; private set; }
    public GlassAssignmentScope AssignmentScope { get; private set; }
    public bool RequiresReview { get; private set; }
    private List<PreQuoteDraftItemGlassReviewReason> _reviewReasons = [];
    private List<PreQuoteDraftItemGlassSourcePage> _sourcePages = [];
    private List<PreQuoteDraftItemGlassEvidence> _evidence = [];
    public ICollection<PreQuoteDraftItemGlassReviewReason> ReviewReasons => _reviewReasons;
    public ICollection<PreQuoteDraftItemGlassSourcePage> SourcePages => _sourcePages;
    public ICollection<PreQuoteDraftItemGlassEvidence> Evidence => _evidence;

    private PreQuoteDraftItemGlassSnapshot()
    {
    }

    public static PreQuoteDraftItemGlassSnapshot Create(
        Guid preQuoteDraftItemId, Guid sourceStructuredItemGlassId, Guid? glassTypeId,
        string? rawSpecification, string? normalizedCodeSnapshot,
        GlassAssignmentScope assignmentScope, bool requiresReview,
        IReadOnlyList<GlassReviewReason> reviewReasons,
        IReadOnlyList<int> sourcePages,
        IReadOnlyList<PreQuoteDraftItemGlassEvidenceSource> evidence)
    {
        return new()
        {
            Id = Guid.NewGuid(),
            PreQuoteDraftItemId = preQuoteDraftItemId,
            SourceStructuredItemGlassId = sourceStructuredItemGlassId,
            GlassTypeId = glassTypeId,
            RawSpecification = rawSpecification,
            NormalizedCodeSnapshot = normalizedCodeSnapshot,
            AssignmentScope = assignmentScope,
            RequiresReview = requiresReview,
            _reviewReasons = reviewReasons
                .Select((reason, index) =>
                    PreQuoteDraftItemGlassReviewReason.Create(
                        Guid.NewGuid(), index + 1, reason))
                .ToList(),
            _sourcePages = sourcePages
                .Select((page, index) =>
                    PreQuoteDraftItemGlassSourcePage.Create(
                        Guid.NewGuid(), index + 1, page))
                .ToList(),
            _evidence = evidence.Select(value =>
                    PreQuoteDraftItemGlassEvidence.Create(value))
                .ToList()
        };
    }
}

public sealed class PreQuoteDraftItemValuationSnapshot
{
    public Guid Id { get; private set; }
    public Guid PreQuoteDraftItemId { get; private set; }
    public Guid SourceStructuredItemValuationId { get; private set; }
    public PreQuoteDraftValuationStatus Status { get; private set; }
    public GlassValuationReason? Reason { get; private set; }
    public Guid? GlassTypeId { get; private set; }
    public Guid? GlassPriceRangeVersionId { get; private set; }
    public int? WidthMillimetersUsed { get; private set; }
    public int? HeightMillimetersUsed { get; private set; }
    public int? QuantityUsed { get; private set; }
    public decimal? UnitAreaSquareMeters { get; private set; }
    public decimal? TotalAreaSquareMeters { get; private set; }
    public decimal? UnitPricePerSquareMeter { get; private set; }
    public decimal? UnitAmount { get; private set; }
    public decimal? TotalAmount { get; private set; }
    public string? Currency { get; private set; }
    public DateTimeOffset ValuedAtUtc { get; private set; }
    public DateTimeOffset? InvalidatedAtUtc { get; private set; }
    public PreQuoteDraftValuationInvalidationReason? InvalidationReason { get; private set; }

    private PreQuoteDraftItemValuationSnapshot() { }

    public static PreQuoteDraftItemValuationSnapshot Create(
        Guid sourceStructuredItemValuationId, Guid preQuoteDraftItemId,
        PreQuoteDraftValuationStatus status, GlassValuationReason? reason,
        Guid? glassTypeId, Guid? glassPriceRangeVersionId,
        int? widthMillimetersUsed, int? heightMillimetersUsed,
        int? quantityUsed, decimal? unitAreaSquareMeters,
        decimal? totalAreaSquareMeters, decimal? unitPricePerSquareMeter,
        decimal? unitAmount, decimal? totalAmount, string? currency,
        DateTimeOffset valuedAtUtc, DateTimeOffset? invalidatedAtUtc,
        PreQuoteDraftValuationInvalidationReason? invalidationReason)
    {
        return new()
        {
            Id = Guid.NewGuid(),
            PreQuoteDraftItemId = preQuoteDraftItemId,
            SourceStructuredItemValuationId = sourceStructuredItemValuationId,
            Status = status,
            Reason = reason,
            GlassTypeId = glassTypeId,
            GlassPriceRangeVersionId = glassPriceRangeVersionId,
            WidthMillimetersUsed = widthMillimetersUsed,
            HeightMillimetersUsed = heightMillimetersUsed,
            QuantityUsed = quantityUsed,
            UnitAreaSquareMeters = unitAreaSquareMeters,
            TotalAreaSquareMeters = totalAreaSquareMeters,
            UnitPricePerSquareMeter = unitPricePerSquareMeter,
            UnitAmount = unitAmount,
            TotalAmount = totalAmount,
            Currency = currency,
            ValuedAtUtc = valuedAtUtc,
            InvalidatedAtUtc = invalidatedAtUtc,
            InvalidationReason = invalidationReason
        };
    }

    public void Invalidate(
        DateTimeOffset invalidatedAtUtc,
        PreQuoteDraftValuationInvalidationReason reason)
    {
        if (invalidatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Fecha UTC obligatoria.", nameof(invalidatedAtUtc));
        }

        Status = PreQuoteDraftValuationStatus.Stale;
        InvalidatedAtUtc = invalidatedAtUtc;
        InvalidationReason = reason;
    }
}

public sealed class PreQuoteDraftItemGlassReviewReason
{
    private PreQuoteDraftItemGlassReviewReason() { }
    public Guid Id { get; private set; }
    public Guid GlassSnapshotId { get; private set; }
    public int Sequence { get; private set; }
    public GlassReviewReason Code { get; private set; }

    public static PreQuoteDraftItemGlassReviewReason Create(
        Guid id, int sequence, GlassReviewReason code) => new()
    {
        Id = id,
        GlassSnapshotId = Guid.Empty,
        Sequence = sequence,
        Code = code
    };
}

public sealed class PreQuoteDraftItemGlassSourcePage
{
    private PreQuoteDraftItemGlassSourcePage() { }
    public Guid Id { get; private set; }
    public Guid GlassSnapshotId { get; private set; }
    public int Sequence { get; private set; }
    public int PageNumber { get; private set; }

    public static PreQuoteDraftItemGlassSourcePage Create(
        Guid id, int sequence, int pageNumber) => new()
    {
        Id = id,
        GlassSnapshotId = Guid.Empty,
        Sequence = sequence,
        PageNumber = pageNumber
    };
}

public sealed class PreQuoteDraftItemGlassEvidence
{
    private PreQuoteDraftItemGlassEvidence() { }
    public Guid Id { get; private set; }
    public Guid GlassSnapshotId { get; private set; }
    public int Sequence { get; private set; }
    public int PageNumber { get; private set; }
    public EvidenceSourceType SourceType { get; private set; }
    public string Text { get; private set; } = string.Empty;

    public static PreQuoteDraftItemGlassEvidence Create(
        PreQuoteDraftItemGlassEvidenceSource source) => new()
    {
        Id = Guid.NewGuid(),
        GlassSnapshotId = Guid.Empty,
        Sequence = source.Sequence,
        PageNumber = source.PageNumber,
        SourceType = source.SourceType,
        Text = source.Text
    };
}
public sealed record PreQuoteDraftItemSource(
    Guid SourceId, int Sequence, string? Reference, string Description,
    StructuredElementType ElementType, string? RawMeasurements,
    int? WidthMillimeters, int? HeightMillimeters, int? Quantity,
    PreQuoteDraftItemGlassSnapshotSource? Glass = null,
    PreQuoteDraftItemValuationSnapshotSource? Valuation = null);
public sealed record PreQuoteDraftRequirementSource(
    Guid SourceId, int Sequence, RequirementCategory Category, string Value);
public sealed record PreQuoteDraftReferenceSource(
    Guid SourceId, int Sequence, string? Reference, string Description,
    string? Detail, int? Quantity);
public sealed record PreQuoteDraftIssueSource(
    Guid SourceId, int Sequence, StructuredIssueCode Code, string Message,
    int? ItemSequence, int[] PageNumbers);
public sealed record PreQuoteDraftConflictSource(
    Guid SourceId, int Sequence, StructuredConflictCode Code, string Message,
    int[] ItemSequences, int[] PageNumbers);
public sealed record PreQuoteDraftItemEdit(
    Guid? Id, int Sequence, string? Reference, string Description,
    StructuredElementType ElementType, string? RawMeasurements,
    int? WidthMillimeters, int? HeightMillimeters, int? Quantity,
    bool IsIncluded);
public sealed record PreQuoteDraftRequirementEdit(
    Guid? Id, int Sequence, RequirementCategory Category, string Value,
    bool IsIncluded);
public sealed record PreQuoteDraftReferenceEdit(
    Guid? Id, int Sequence, string? Reference, string Description,
    string? Detail, int? Quantity, bool IsIncluded);
public sealed record PreQuoteDraftResolutionEdit(
    Guid Id, PreQuoteDraftResolutionStatus Status, string? Note);
public sealed record PreQuoteDraftEconomicSummary(
    int IncludedItemCount,
    int IncludedKnownQuoteableUnitCount,
    int ValuedItemCount,
    int PendingValuationItemCount,
    int StaleValuationItemCount,
    int ItemsRequiringReviewCount,
    decimal? TotalAreaSquareMeters,
    decimal? GlassSubtotal,
    string? Currency,
    bool IsEconomicallyComplete);

public sealed class PreQuoteDraft
{
    private readonly List<PreQuoteDraftItem> _items = [];
    private readonly List<PreQuoteDraftRequirement> _requirements = [];
    private readonly List<PreQuoteDraftDocumentReference> _documentReferences = [];
    private readonly List<PreQuoteDraftIssue> _issues = [];
    private readonly List<PreQuoteDraftConflict> _conflicts = [];
    private PreQuoteDraft() { }

    public Guid Id { get; private set; }
    public Guid PreQuoteId { get; private set; }
    public Guid SourceDocumentId { get; private set; }
    public Guid SourceStructuredExtractionId { get; private set; }
    public PreQuoteDraftStatus Status { get; private set; }
    public string? ProjectName { get; private set; }
    public string? ClientName { get; private set; }
    public string? Location { get; private set; }
    public int Version { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ApprovedAtUtc { get; private set; }
    public PreQuote PreQuote { get; private set; } = null!;
    public PreQuoteDocument SourceDocument { get; private set; } = null!;
    public StructuredDocumentExtraction SourceStructuredExtraction { get; private set; } = null!;
    public IReadOnlyCollection<PreQuoteDraftItem> Items => _items;
    public IReadOnlyCollection<PreQuoteDraftRequirement> Requirements => _requirements;
    public IReadOnlyCollection<PreQuoteDraftDocumentReference> DocumentReferences => _documentReferences;
    public IReadOnlyCollection<PreQuoteDraftIssue> Issues => _issues;
    public IReadOnlyCollection<PreQuoteDraftConflict> Conflicts => _conflicts;
    public PreQuoteDraftEconomicSummary EconomicSummary => CalculateEconomicSummary();

    public PreQuoteDraftItem? FindItem(Guid itemId) =>
        _items.SingleOrDefault(x => x.Id == itemId);

    public static PreQuoteDraft Create(
        Guid preQuoteId, Guid sourceDocumentId,
        Guid sourceStructuredExtractionId, string? projectName,
        string? clientName, string? location, Guid userId,
        DateTimeOffset createdAtUtc,
        IReadOnlyList<PreQuoteDraftItemSource> items,
        IReadOnlyList<PreQuoteDraftRequirementSource> requirements,
        IReadOnlyList<PreQuoteDraftReferenceSource> references,
        IReadOnlyList<PreQuoteDraftIssueSource> issues,
        IReadOnlyList<PreQuoteDraftConflictSource> conflicts)
    {
        Required(preQuoteId, nameof(preQuoteId));
        Required(sourceDocumentId, nameof(sourceDocumentId));
        Required(sourceStructuredExtractionId, nameof(sourceStructuredExtractionId));
        Required(userId, nameof(userId));
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        Consecutive(items.Select(x => x.Sequence));
        Consecutive(requirements.Select(x => x.Sequence));
        Consecutive(references.Select(x => x.Sequence));
        Consecutive(issues.Select(x => x.Sequence));
        Consecutive(conflicts.Select(x => x.Sequence));

        var draft = new PreQuoteDraft
        {
            Id = Guid.NewGuid(),
            PreQuoteId = preQuoteId,
            SourceDocumentId = sourceDocumentId,
            SourceStructuredExtractionId = sourceStructuredExtractionId,
            Status = PreQuoteDraftStatus.PendingReview,
            ProjectName = NormalizeOptional(projectName, 500),
            ClientName = NormalizeOptional(clientName, 500),
            Location = NormalizeOptional(location, 500),
            Version = 1,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
        draft._items.AddRange(items.Select(x => PreQuoteDraftItem.FromAi(draft.Id, x, userId, createdAtUtc)));
        draft._requirements.AddRange(requirements.Select(x => PreQuoteDraftRequirement.FromAi(draft.Id, x, userId, createdAtUtc)));
        draft._documentReferences.AddRange(references.Select(x => PreQuoteDraftDocumentReference.FromAi(draft.Id, x, userId, createdAtUtc)));
        draft._issues.AddRange(issues.Select(x => PreQuoteDraftIssue.Create(draft.Id, x, createdAtUtc)));
        draft._conflicts.AddRange(conflicts.Select(x => PreQuoteDraftConflict.Create(draft.Id, x, createdAtUtc)));
        return draft;
    }

    private PreQuoteDraftEconomicSummary CalculateEconomicSummary()
    {
        var included = _items.Where(x => x.IsIncluded).ToArray();
        var valued = included.Where(
            x => x.ValuationStatus == PreQuoteDraftValuationStatus.Valued).ToArray();
        var pending = included.Where(
            x => x.ValuationStatus == PreQuoteDraftValuationStatus.Pending).ToArray();
        var stale = included.Where(
            x => x.ValuationStatus == PreQuoteDraftValuationStatus.Stale).ToArray();
        var requiringReview = included.Where(
            x => x.ValuationStatus == PreQuoteDraftValuationStatus.RequiresReview)
            .ToArray();

        var valuedArea = valued.Sum(x => x.ValuationSnapshot!.TotalAreaSquareMeters
            ?? 0);
        var valuedSubtotal = valued.Sum(x => x.ValuationSnapshot!.TotalAmount ?? 0);
        var currencies = valued.Select(x => x.ValuationSnapshot!.Currency)
            .Where(currency => currency is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currency = currencies.Length == 1 ? currencies[0] : null;

        return new(
            included.Length,
            included.Sum(x => (int?)x.Quantity ?? 0),
            valued.Length,
            pending.Length,
            stale.Length,
            requiringReview.Length,
            valuedArea == 0 ? null : valuedArea,
            valuedSubtotal == 0 ? null : valuedSubtotal,
            currency,
            valued.Length > 0 && stale.Length == 0 && pending.Length == 0 &&
                requiringReview.Length == 0 && currency is not null);
    }

    public void Update(
        int expectedVersion, string? projectName, string? clientName,
        string? location, IReadOnlyList<PreQuoteDraftItemEdit> items,
        IReadOnlyList<PreQuoteDraftRequirementEdit> requirements,
        IReadOnlyList<PreQuoteDraftReferenceEdit> references,
        IReadOnlyList<PreQuoteDraftResolutionEdit> issues,
        IReadOnlyList<PreQuoteDraftResolutionEdit> conflicts,
        Guid userId, DateTimeOffset updatedAtUtc)
    {
        Mutable(expectedVersion);
        Required(userId, nameof(userId));
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc) throw new ArgumentException("Fecha inválida.", nameof(updatedAtUtc));
        Consecutive(items.Select(x => x.Sequence));
        Consecutive(requirements.Select(x => x.Sequence));
        Consecutive(references.Select(x => x.Sequence));
        ApplyItems(items, userId, updatedAtUtc);
        ApplyRequirements(requirements, userId, updatedAtUtc);
        ApplyReferences(references, userId, updatedAtUtc);
        ApplyResolutions(_issues, issues, userId, updatedAtUtc);
        ApplyResolutions(_conflicts, conflicts, userId, updatedAtUtc);
        ProjectName = NormalizeOptional(projectName, 500);
        ClientName = NormalizeOptional(clientName, 500);
        Location = NormalizeOptional(location, 500);
        Status = PreQuoteDraftStatus.InReview;
        UpdatedByUserId = userId;
        UpdatedAtUtc = updatedAtUtc;
        Version++;
    }

    public void Approve(int expectedVersion, Guid userId, DateTimeOffset approvedAtUtc)
    {
        Mutable(expectedVersion);
        Required(userId, nameof(userId));
        EnsureUtc(approvedAtUtc, nameof(approvedAtUtc));
        if (string.IsNullOrWhiteSpace(ProjectName)) throw new InvalidOperationException("PROJECT_INCOMPLETE");
        var included = _items.Where(x => x.IsIncluded).ToArray();
        if (included.Length == 0) throw new InvalidOperationException("NO_INCLUDED_ITEMS");
        if (included.Any(x => !x.IsCompleteForApproval)) throw new InvalidOperationException("INCOMPLETE_ITEMS");
        if (_issues.Any(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Pending)) throw new InvalidOperationException("PENDING_ISSUES");
        if (_conflicts.Any(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Pending)) throw new InvalidOperationException("PENDING_CONFLICTS");
        Status = PreQuoteDraftStatus.Approved;
        ApprovedByUserId = userId;
        ApprovedAtUtc = approvedAtUtc;
        UpdatedByUserId = userId;
        UpdatedAtUtc = approvedAtUtc;
        Version++;
    }

    private void ApplyItems(IReadOnlyList<PreQuoteDraftItemEdit> edits, Guid userId, DateTimeOffset at)
    {
        EnsureIdentity(_items.Select(x => x.Id), edits.Select(x => x.Id));
        foreach (var edit in edits)
        {
            if (edit.Id is Guid id) _items.Single(x => x.Id == id).Update(edit, userId, at);
            else _items.Add(PreQuoteDraftItem.Manual(Id, edit, userId, at));
        }
    }
    private void ApplyRequirements(IReadOnlyList<PreQuoteDraftRequirementEdit> edits, Guid userId, DateTimeOffset at)
    {
        EnsureIdentity(_requirements.Select(x => x.Id), edits.Select(x => x.Id));
        foreach (var edit in edits)
        {
            if (edit.Id is Guid id) _requirements.Single(x => x.Id == id).Update(edit, userId, at);
            else _requirements.Add(PreQuoteDraftRequirement.Manual(Id, edit, userId, at));
        }
    }
    private void ApplyReferences(IReadOnlyList<PreQuoteDraftReferenceEdit> edits, Guid userId, DateTimeOffset at)
    {
        EnsureIdentity(_documentReferences.Select(x => x.Id), edits.Select(x => x.Id));
        foreach (var edit in edits)
        {
            if (edit.Id is Guid id) _documentReferences.Single(x => x.Id == id).Update(edit, userId, at);
            else _documentReferences.Add(PreQuoteDraftDocumentReference.Manual(Id, edit, userId, at));
        }
    }
    private static void ApplyResolutions<T>(IReadOnlyCollection<T> rows, IReadOnlyList<PreQuoteDraftResolutionEdit> edits, Guid user, DateTimeOffset at) where T : PreQuoteDraftFinding
    {
        EnsureIdentity(rows.Select(x => x.Id), edits.Select(x => (Guid?)x.Id));
        foreach (var edit in edits) rows.Single(x => x.Id == edit.Id).Resolve(edit.Status, edit.Note, user, at);
    }
    private void Mutable(int expected) {
        if (Status == PreQuoteDraftStatus.Approved) throw new InvalidOperationException("DRAFT_APPROVED");
        if (expected != Version) throw new InvalidOperationException("VERSION_CONFLICT");
    }
    private static void EnsureIdentity(IEnumerable<Guid> existing, IEnumerable<Guid?> supplied)
    {
        var ids = supplied.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        if (ids.Length != ids.Distinct().Count() || !existing.Order().SequenceEqual(ids.Order()))
            throw new ArgumentException("Identidad de filas inválida.");
    }
    internal static void Consecutive(IEnumerable<int> values) {
        var array = values.ToArray();
        if (!array.SequenceEqual(Enumerable.Range(1, array.Length))) throw new ArgumentException("Secuencias inválidas.");
    }
    internal static string RequiredText(string? value, int max) {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Valor obligatorio.");
        var result = value.Trim(); if (result.Length > max) throw new ArgumentException("Valor demasiado largo."); return result;
    }
    internal static string? NormalizeOptional(string? value, int max) {
        if (string.IsNullOrWhiteSpace(value)) return null; var result = value.Trim();
        if (result.Length > max) throw new ArgumentException("Valor demasiado largo."); return result;
    }
    internal static void Dimensions(int? width, int? height) {
        if ((width is null) != (height is null) || width <= 0 || height <= 0) {
            if (width is not null || height is not null) throw new ArgumentException("Dimensiones inválidas.");
        }
    }
    internal static void Quantity(int? value) { if (value <= 0) throw new ArgumentException("Cantidad inválida."); }
    private static void Required(Guid value, string name) { if (value == Guid.Empty) throw new ArgumentException("Identificador obligatorio.", name); }
    private static void EnsureUtc(DateTimeOffset value, string name) { if (value.Offset != TimeSpan.Zero) throw new ArgumentException("Fecha UTC obligatoria.", name); }
}

public sealed class PreQuoteDraftItem
{
    private PreQuoteDraftItem() { }
    public Guid Id { get; private set; } public Guid PreQuoteDraftId { get; private set; }
    public int Sequence { get; private set; } public PreQuoteDraftOrigin Origin { get; private set; }
    public Guid? SourceStructuredItemId { get; private set; } public int? SourceItemSequence { get; private set; }
    public string? Reference { get; private set; } public string Description { get; private set; } = "";
    public StructuredElementType ElementType { get; private set; } public string? RawMeasurements { get; private set; }
    public int? WidthMillimeters { get; private set; } public int? HeightMillimeters { get; private set; }
    public int? Quantity { get; private set; } public bool IsIncluded { get; private set; }
    public PreQuoteDraftValuationStatus ValuationStatus { get; private set; }
    public PreQuoteDraftItemGlassSnapshot? GlassSnapshot { get; private set; }
    public PreQuoteDraftItemValuationSnapshot? ValuationSnapshot { get; private set; }
    public Guid CreatedByUserId { get; private set; } public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } public DateTimeOffset UpdatedAtUtc { get; private set; }
    public PreQuoteDraft Draft { get; private set; } = null!;
    public bool IsCompleteForApproval => !string.IsNullOrWhiteSpace(Description) && ElementType != StructuredElementType.Other && WidthMillimeters > 0 && HeightMillimeters > 0 && Quantity > 0;
    internal static PreQuoteDraftItem FromAi(Guid draftId, PreQuoteDraftItemSource x, Guid user, DateTimeOffset at) =>
        Create(draftId, x.Sequence, PreQuoteDraftOrigin.Ai, x.SourceId, x.Sequence, x.Reference, x.Description, x.ElementType, x.RawMeasurements, x.WidthMillimeters, x.HeightMillimeters, x.Quantity, true, user, at, x.Glass, x.Valuation);
    internal static PreQuoteDraftItem Manual(
        Guid draftId, PreQuoteDraftItemEdit x, Guid user, DateTimeOffset at) =>
        Create(draftId, x.Sequence, PreQuoteDraftOrigin.Manual, null, null,
            x.Reference, x.Description, x.ElementType, x.RawMeasurements,
            x.WidthMillimeters, x.HeightMillimeters, x.Quantity, x.IsIncluded,
            user, at, null, null);
    private static PreQuoteDraftItem Create(
        Guid draftId, int sequence, PreQuoteDraftOrigin origin, Guid? sourceId,
        int? sourceSequence, string? reference, string description,
        StructuredElementType type, string? raw, int? width, int? height,
        int? quantity, bool included, Guid user, DateTimeOffset at,
        PreQuoteDraftItemGlassSnapshotSource? glass,
        PreQuoteDraftItemValuationSnapshotSource? valuation)
    {
        PreQuoteDraft.Dimensions(width, height); PreQuoteDraft.Quantity(quantity);
        if (!Enum.IsDefined(type)) throw new ArgumentException("Tipo inválido.");
        var itemId = Guid.NewGuid();
        var glassSnapshot = glass is null
            ? null
            : PreQuoteDraftItemGlassSnapshot.Create(
                itemId,
                glass.SourceStructuredItemGlassId,
                glass.GlassTypeId,
                glass.RawSpecification,
                glass.NormalizedCodeSnapshot,
                glass.AssignmentScope,
                glass.RequiresReview,
                glass.ReviewReasons,
                glass.SourcePages,
                glass.Evidence);
        var valuationSnapshot = valuation is null
            ? null
            : PreQuoteDraftItemValuationSnapshot.Create(
                valuation.SourceStructuredItemValuationId,
                itemId,
                valuation.Status,
                valuation.Reason,
                valuation.GlassTypeId,
                valuation.GlassPriceRangeVersionId,
                valuation.WidthMillimetersUsed,
                valuation.HeightMillimetersUsed,
                valuation.QuantityUsed,
                valuation.UnitAreaSquareMeters,
                valuation.TotalAreaSquareMeters,
                valuation.UnitPricePerSquareMeter,
                valuation.UnitAmount,
                valuation.TotalAmount,
                valuation.Currency,
                valuation.ValuedAtUtc,
                valuation.InvalidatedAtUtc,
                valuation.InvalidationReason);
        var valuationStatus = valuationSnapshot?.Status
            ?? (glassSnapshot?.RequiresReview is true
                ? PreQuoteDraftValuationStatus.RequiresReview
                : PreQuoteDraftValuationStatus.Pending);
        return new()
        {
            Id = itemId,
            PreQuoteDraftId = draftId,
            Sequence = sequence,
            Origin = origin,
            SourceStructuredItemId = sourceId,
            SourceItemSequence = sourceSequence,
            Reference = PreQuoteDraft.NormalizeOptional(reference, 200),
            Description = PreQuoteDraft.RequiredText(description, 1000),
            ElementType = type,
            RawMeasurements = PreQuoteDraft.NormalizeOptional(raw, 500),
            WidthMillimeters = width,
            HeightMillimeters = height,
            Quantity = quantity,
            IsIncluded = included,
            ValuationStatus = valuationStatus,
            GlassSnapshot = glassSnapshot,
            ValuationSnapshot = valuationSnapshot,
            CreatedByUserId = user,
            UpdatedByUserId = user,
            CreatedAtUtc = at,
            UpdatedAtUtc = at
        };
    }
    internal void Update(PreQuoteDraftItemEdit x, Guid user, DateTimeOffset at)
    {
        PreQuoteDraft.Dimensions(x.WidthMillimeters, x.HeightMillimeters);
        PreQuoteDraft.Quantity(x.Quantity);

        var widthChanged = x.WidthMillimeters != WidthMillimeters;
        var heightChanged = x.HeightMillimeters != HeightMillimeters;
        var quantityChanged = x.Quantity != Quantity;

        Sequence = x.Sequence;
        Reference = PreQuoteDraft.NormalizeOptional(x.Reference, 200);
        Description = PreQuoteDraft.RequiredText(x.Description, 1000);
        ElementType = x.ElementType;
        RawMeasurements = PreQuoteDraft.NormalizeOptional(x.RawMeasurements, 500);
        WidthMillimeters = x.WidthMillimeters;
        HeightMillimeters = x.HeightMillimeters;
        Quantity = x.Quantity;
        IsIncluded = x.IsIncluded;

        if (ValuationStatus == PreQuoteDraftValuationStatus.Valued
            && ValuationSnapshot is not null
            && (widthChanged || heightChanged || quantityChanged))
        {
            var reason = widthChanged && heightChanged && quantityChanged
                ? PreQuoteDraftValuationInvalidationReason.MultipleInputsChanged
                : widthChanged
                    ? PreQuoteDraftValuationInvalidationReason.WidthChanged
                    : heightChanged
                        ? PreQuoteDraftValuationInvalidationReason.HeightChanged
                        : PreQuoteDraftValuationInvalidationReason.QuantityChanged;
            ValuationSnapshot.Invalidate(at, reason);
            ValuationStatus = PreQuoteDraftValuationStatus.Stale;
        }

        UpdatedByUserId = user;
        UpdatedAtUtc = at;
    }
}

public sealed class PreQuoteDraftRequirement
{
    private PreQuoteDraftRequirement() { }
    public Guid Id { get; private set; } public Guid PreQuoteDraftId { get; private set; } public int Sequence { get; private set; }
    public PreQuoteDraftOrigin Origin { get; private set; } public Guid? SourceStructuredRequirementId { get; private set; } public int? SourceRequirementSequence { get; private set; }
    public RequirementCategory Category { get; private set; } public string Value { get; private set; } = ""; public bool IsIncluded { get; private set; }
    public Guid CreatedByUserId { get; private set; } public Guid UpdatedByUserId { get; private set; } public DateTimeOffset CreatedAtUtc { get; private set; } public DateTimeOffset UpdatedAtUtc { get; private set; }
    public PreQuoteDraft Draft { get; private set; } = null!;
    internal static PreQuoteDraftRequirement FromAi(Guid d, PreQuoteDraftRequirementSource x, Guid u, DateTimeOffset at) => Create(d,x.Sequence,PreQuoteDraftOrigin.Ai,x.SourceId,x.Sequence,x.Category,x.Value,true,u,at);
    internal static PreQuoteDraftRequirement Manual(Guid d, PreQuoteDraftRequirementEdit x, Guid u, DateTimeOffset at) => Create(d,x.Sequence,PreQuoteDraftOrigin.Manual,null,null,x.Category,x.Value,x.IsIncluded,u,at);
    private static PreQuoteDraftRequirement Create(Guid d,int s,PreQuoteDraftOrigin o,Guid? sid,int? ss,RequirementCategory c,string v,bool included,Guid u,DateTimeOffset at) => new(){Id=Guid.NewGuid(),PreQuoteDraftId=d,Sequence=s,Origin=o,SourceStructuredRequirementId=sid,SourceRequirementSequence=ss,Category=c,Value=included?PreQuoteDraft.RequiredText(v,1000):PreQuoteDraft.NormalizeOptional(v,1000)??"",IsIncluded=included,CreatedByUserId=u,UpdatedByUserId=u,CreatedAtUtc=at,UpdatedAtUtc=at};
    internal void Update(PreQuoteDraftRequirementEdit x,Guid u,DateTimeOffset at){Sequence=x.Sequence;Category=x.Category;Value=x.IsIncluded?PreQuoteDraft.RequiredText(x.Value,1000):PreQuoteDraft.NormalizeOptional(x.Value,1000)??"";IsIncluded=x.IsIncluded;UpdatedByUserId=u;UpdatedAtUtc=at;}
}

public sealed class PreQuoteDraftDocumentReference
{
    private PreQuoteDraftDocumentReference() { }
    public Guid Id { get; private set; } public Guid PreQuoteDraftId { get; private set; } public int Sequence { get; private set; }
    public PreQuoteDraftOrigin Origin { get; private set; } public Guid? SourceStructuredDocumentReferenceId { get; private set; } public int? SourceDocumentReferenceSequence { get; private set; }
    public string? Reference { get; private set; } public string Description { get; private set; }=""; public string? Detail { get; private set; } public int? Quantity { get; private set; } public bool IsIncluded { get; private set; }
    public Guid CreatedByUserId { get; private set; } public Guid UpdatedByUserId { get; private set; } public DateTimeOffset CreatedAtUtc { get; private set; } public DateTimeOffset UpdatedAtUtc { get; private set; } public PreQuoteDraft Draft { get; private set; }=null!;
    internal static PreQuoteDraftDocumentReference FromAi(Guid d,PreQuoteDraftReferenceSource x,Guid u,DateTimeOffset at)=>Create(d,x.Sequence,PreQuoteDraftOrigin.Ai,x.SourceId,x.Sequence,x.Reference,x.Description,x.Detail,x.Quantity,true,u,at);
    internal static PreQuoteDraftDocumentReference Manual(Guid d,PreQuoteDraftReferenceEdit x,Guid u,DateTimeOffset at)=>Create(d,x.Sequence,PreQuoteDraftOrigin.Manual,null,null,x.Reference,x.Description,x.Detail,x.Quantity,x.IsIncluded,u,at);
    private static PreQuoteDraftDocumentReference Create(Guid d,int s,PreQuoteDraftOrigin o,Guid? sid,int? ss,string? r,string desc,string? detail,int? q,bool included,Guid u,DateTimeOffset at){PreQuoteDraft.Quantity(q);return new(){Id=Guid.NewGuid(),PreQuoteDraftId=d,Sequence=s,Origin=o,SourceStructuredDocumentReferenceId=sid,SourceDocumentReferenceSequence=ss,Reference=PreQuoteDraft.NormalizeOptional(r,200),Description=PreQuoteDraft.RequiredText(desc,1000),Detail=PreQuoteDraft.NormalizeOptional(detail,2000),Quantity=q,IsIncluded=included,CreatedByUserId=u,UpdatedByUserId=u,CreatedAtUtc=at,UpdatedAtUtc=at};}
    internal void Update(PreQuoteDraftReferenceEdit x,Guid u,DateTimeOffset at){PreQuoteDraft.Quantity(x.Quantity);Sequence=x.Sequence;Reference=PreQuoteDraft.NormalizeOptional(x.Reference,200);Description=PreQuoteDraft.RequiredText(x.Description,1000);Detail=PreQuoteDraft.NormalizeOptional(x.Detail,2000);Quantity=x.Quantity;IsIncluded=x.IsIncluded;UpdatedByUserId=u;UpdatedAtUtc=at;}
}

public abstract class PreQuoteDraftFinding
{
    public Guid Id { get; protected set; } public Guid PreQuoteDraftId { get; protected set; } public int Sequence { get; protected set; }
    public PreQuoteDraftResolutionStatus ResolutionStatus { get; protected set; } public string? ResolutionNote { get; protected set; }
    public Guid? ResolvedByUserId { get; protected set; } public DateTimeOffset? ResolvedAtUtc { get; protected set; } public DateTimeOffset CreatedAtUtc { get; protected set; }
    internal void Resolve(PreQuoteDraftResolutionStatus status,string? note,Guid user,DateTimeOffset at){if(!Enum.IsDefined(status))throw new ArgumentException("Resolución inválida.");ResolutionStatus=status;if(status==PreQuoteDraftResolutionStatus.Pending){ResolutionNote=null;ResolvedByUserId=null;ResolvedAtUtc=null;}else{ResolutionNote=PreQuoteDraft.RequiredText(note,2000);ResolvedByUserId=user;ResolvedAtUtc=at;}}
}
public sealed class PreQuoteDraftIssue : PreQuoteDraftFinding
{
    private PreQuoteDraftIssue(){} public Guid SourceStructuredIssueId{get;private set;} public int SourceIssueSequence{get;private set;} public StructuredIssueCode Code{get;private set;} public string Message{get;private set;}=""; public int? ItemSequence{get;private set;} public int[] PageNumbers{get;private set;}=[]; public PreQuoteDraft Draft{get;private set;}=null!;
    internal static PreQuoteDraftIssue Create(Guid d,PreQuoteDraftIssueSource x,DateTimeOffset at)=>new(){Id=Guid.NewGuid(),PreQuoteDraftId=d,Sequence=x.Sequence,SourceStructuredIssueId=x.SourceId,SourceIssueSequence=x.Sequence,Code=x.Code,Message=x.Message,ItemSequence=x.ItemSequence,PageNumbers=x.PageNumbers.ToArray(),ResolutionStatus=PreQuoteDraftResolutionStatus.Pending,CreatedAtUtc=at};
}
public sealed class PreQuoteDraftConflict : PreQuoteDraftFinding
{
    private PreQuoteDraftConflict(){} public Guid SourceStructuredConflictId{get;private set;} public int SourceConflictSequence{get;private set;} public StructuredConflictCode Code{get;private set;} public string Message{get;private set;}=""; public int[] ItemSequences{get;private set;}=[]; public int[] PageNumbers{get;private set;}=[]; public PreQuoteDraft Draft{get;private set;}=null!;
    internal static PreQuoteDraftConflict Create(Guid d,PreQuoteDraftConflictSource x,DateTimeOffset at)=>new(){Id=Guid.NewGuid(),PreQuoteDraftId=d,Sequence=x.Sequence,SourceStructuredConflictId=x.SourceId,SourceConflictSequence=x.Sequence,Code=x.Code,Message=x.Message,ItemSequences=x.ItemSequences.ToArray(),PageNumbers=x.PageNumbers.ToArray(),ResolutionStatus=PreQuoteDraftResolutionStatus.Pending,CreatedAtUtc=at};
}
