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
    RequiresReview,
    NotPriceable
}
public enum PreQuoteDraftValuationInvalidationReason
{
    MultipleInputsChanged,
    WidthChanged,
    HeightChanged,
    QuantityChanged
}
public enum PreQuoteDraftTechnicalSelectionState
{
    Pending = 1,
    Suggested,
    Confirmed,
    Modified
}
public enum PreQuoteDraftTechnicalSelectionSource
{
    Extracted = 1,
    Requested,
    Rule,
    AiSuggestion,
    Manual
}

public sealed record PreQuoteDraftItemGlassReviewReasonSource(
    int Sequence, GlassReviewReason Code);
public sealed record PreQuoteDraftItemGlassSourcePageSource(
    int Sequence, int PageNumber);
public sealed record PreQuoteDraftItemGlassEvidenceSource(
    int Sequence, int? PageNumber, EvidenceSourceType SourceType,
    string Text, string? SheetName = null, string? CellRange = null);
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
    PreQuoteDraftValuationInvalidationReason? InvalidationReason,
    int? PriceRangeVersion = null,
    decimal? ExpectedPricePerSquareMeter = null,
    decimal? ExpectedAmount = null,
    decimal? MaximumPricePerSquareMeter = null);
public sealed record PreQuoteDraftItemTechnicalSnapshotSource(
    Guid SourceStructuredItemTechnicalClassificationId,
    string? SystemCode,
    string? SystemOriginalText,
    TechnicalClassificationSource? SystemSource,
    decimal? SystemConfidence,
    string? FrameCode,
    string? FrameOriginalText,
    TechnicalClassificationSource? FrameSource,
    decimal? FrameConfidence,
    string? FinishCode,
    string? FinishOriginalText,
    TechnicalClassificationSource? FinishSource,
    decimal? FinishConfidence,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons);
public sealed record PreQuoteDraftItemTechnicalSelectionSource(
    string? RequestedSystemCode,
    string? RequestedSystemOriginalText,
    string? SuggestedSystemCode = null,
    string? SelectedSystemCode = null,
    string? RequestedGlassCode = null,
    string? RequestedGlassOriginalText = null,
    string? SuggestedGlassCode = null,
    string? SelectedGlassCode = null,
    string? RequestedFinishCode = null,
    string? RequestedFinishOriginalText = null,
    string? SuggestedFinishCode = null,
    string? SelectedFinishCode = null,
    string? RequestedHardwareCode = null,
    string? RequestedHardwareOriginalText = null,
    string? SuggestedHardwareCode = null,
    string? SelectedHardwareCode = null,
    PreQuoteDraftTechnicalSelectionState SelectionState =
        PreQuoteDraftTechnicalSelectionState.Pending,
    bool RequiresReview = false,
    decimal? Confidence = null,
    IReadOnlyList<string>? ReviewReasons = null,
    PreQuoteDraftTechnicalSelectionSource RequestedSource =
        PreQuoteDraftTechnicalSelectionSource.Extracted,
    PreQuoteDraftTechnicalSelectionSource? SuggestedSource = null,
    PreQuoteDraftTechnicalSelectionSource? SelectedSource = null);
public sealed record PreQuoteDraftItemTechnicalSelectionEdit(
    string? SelectedSystemCode,
    string? SelectedGlassCode,
    string? SelectedFinishCode,
    string? SelectedHardwareCode,
    bool ConfirmSelection);

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
    public decimal? BillableAreaUnitSquareMeters { get; private set; }
    public string? GlassCode { get; private set; }
    public int? GlassPriceRangeVersion { get; private set; }
    public decimal? GlassMinimumPricePerSquareMeter { get; private set; }
    public decimal? GlassExpectedPricePerSquareMeter { get; private set; }
    public decimal? GlassMaximumPricePerSquareMeter { get; private set; }
    public string? SystemCode { get; private set; }
    public TechnicalClassificationSource? SystemSource { get; private set; }
    public string? FrameCode { get; private set; }
    public string? FinishCode { get; private set; }
    public string? LaborProfileCode { get; private set; }
    public string? AssemblyProfileCode { get; private set; }
    public decimal? FinishFactorMinimum { get; private set; }
    public decimal? FinishFactorExpected { get; private set; }
    public decimal? FinishFactorMaximum { get; private set; }
    public decimal? AccessoryFactor { get; private set; }
    public decimal? GlassMinimumAmount { get; private set; }
    public decimal? GlassExpectedAmount { get; private set; }
    public decimal? GlassMaximumAmount { get; private set; }
    public decimal? LaborMinimumAmount { get; private set; }
    public decimal? LaborExpectedAmount { get; private set; }
    public decimal? LaborMaximumAmount { get; private set; }
    public decimal? AssemblyMinimumAmount { get; private set; }
    public decimal? AssemblyExpectedAmount { get; private set; }
    public decimal? AssemblyMaximumAmount { get; private set; }
    public decimal? AccessoriesMinimumAmount { get; private set; }
    public decimal? AccessoriesExpectedAmount { get; private set; }
    public decimal? AccessoriesMaximumAmount { get; private set; }
    public decimal? ItemMinimumAmount { get; private set; }
    public decimal? ItemExpectedAmount { get; private set; }
    public decimal? ItemMaximumAmount { get; private set; }
    public string? PricingProfileVersion { get; private set; }
    public int? ConfidenceScore { get; private set; }
    public PreQuoteDraftPricingConfidenceLevel? ConfidenceLevel { get; private set; }
    public string[] Assumptions { get; private set; } = [];
    public string[] MissingData { get; private set; } = [];
    public bool? RequiresReview { get; private set; }
    public DateTimeOffset? CalculatedAtUtc { get; private set; }

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
        PreQuoteDraftValuationInvalidationReason? invalidationReason,
        PreQuotePreliminaryPricingResult? preliminaryPricing = null)
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
            InvalidationReason = invalidationReason,
            BillableAreaUnitSquareMeters =
                preliminaryPricing?.BillableAreaUnitSquareMeters,
            GlassCode = preliminaryPricing?.GlassCode,
            GlassPriceRangeVersion = preliminaryPricing?.GlassPriceRangeVersion,
            GlassMinimumPricePerSquareMeter =
                preliminaryPricing?.GlassMinimumPricePerSquareMeter,
            GlassExpectedPricePerSquareMeter =
                preliminaryPricing?.GlassExpectedPricePerSquareMeter,
            GlassMaximumPricePerSquareMeter =
                preliminaryPricing?.GlassMaximumPricePerSquareMeter,
            SystemCode = preliminaryPricing?.SystemCode,
            SystemSource = preliminaryPricing?.SystemSource,
            FrameCode = preliminaryPricing?.FrameCode,
            FinishCode = preliminaryPricing?.FinishCode,
            LaborProfileCode = preliminaryPricing?.LaborProfileCode,
            AssemblyProfileCode = preliminaryPricing?.AssemblyProfileCode,
            FinishFactorMinimum = preliminaryPricing?.FinishFactorMinimum,
            FinishFactorExpected = preliminaryPricing?.FinishFactorExpected,
            FinishFactorMaximum = preliminaryPricing?.FinishFactorMaximum,
            AccessoryFactor = preliminaryPricing?.AccessoryFactor,
            GlassMinimumAmount = preliminaryPricing?.GlassMinimumAmount,
            GlassExpectedAmount = preliminaryPricing?.GlassExpectedAmount,
            GlassMaximumAmount = preliminaryPricing?.GlassMaximumAmount,
            LaborMinimumAmount = preliminaryPricing?.LaborMinimumAmount,
            LaborExpectedAmount = preliminaryPricing?.LaborExpectedAmount,
            LaborMaximumAmount = preliminaryPricing?.LaborMaximumAmount,
            AssemblyMinimumAmount = preliminaryPricing?.AssemblyMinimumAmount,
            AssemblyExpectedAmount = preliminaryPricing?.AssemblyExpectedAmount,
            AssemblyMaximumAmount = preliminaryPricing?.AssemblyMaximumAmount,
            AccessoriesMinimumAmount =
                preliminaryPricing?.AccessoriesMinimumAmount,
            AccessoriesExpectedAmount =
                preliminaryPricing?.AccessoriesExpectedAmount,
            AccessoriesMaximumAmount =
                preliminaryPricing?.AccessoriesMaximumAmount,
            ItemMinimumAmount = preliminaryPricing?.ItemMinimumAmount,
            ItemExpectedAmount = preliminaryPricing?.ItemExpectedAmount,
            ItemMaximumAmount = preliminaryPricing?.ItemMaximumAmount,
            PricingProfileVersion = preliminaryPricing?.PricingProfileVersion,
            ConfidenceScore = preliminaryPricing?.ConfidenceScore,
            ConfidenceLevel = preliminaryPricing?.ConfidenceLevel,
            Assumptions = preliminaryPricing?.Assumptions.ToArray() ?? [],
            MissingData = preliminaryPricing?.MissingData.ToArray() ?? [],
            RequiresReview = preliminaryPricing?.RequiresReview,
            CalculatedAtUtc = preliminaryPricing is null ? null : valuedAtUtc
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

public sealed class PreQuoteDraftItemTechnicalSnapshot
{
    private PreQuoteDraftItemTechnicalSnapshot() { }
    public Guid Id { get; private set; }
    public Guid PreQuoteDraftItemId { get; private set; }
    public Guid SourceStructuredItemTechnicalClassificationId { get; private set; }
    public string? SystemCode { get; private set; }
    public string? SystemOriginalText { get; private set; }
    public TechnicalClassificationSource? SystemSource { get; private set; }
    public decimal? SystemConfidence { get; private set; }
    public string? FrameCode { get; private set; }
    public string? FrameOriginalText { get; private set; }
    public TechnicalClassificationSource? FrameSource { get; private set; }
    public decimal? FrameConfidence { get; private set; }
    public string? FinishCode { get; private set; }
    public string? FinishOriginalText { get; private set; }
    public TechnicalClassificationSource? FinishSource { get; private set; }
    public decimal? FinishConfidence { get; private set; }
    public bool RequiresReview { get; private set; }
    public string[] ReviewReasons { get; private set; } = [];

    public static PreQuoteDraftItemTechnicalSnapshot Create(
        Guid preQuoteDraftItemId,
        PreQuoteDraftItemTechnicalSnapshotSource source)
    {
        return new()
        {
            Id = Guid.NewGuid(),
            PreQuoteDraftItemId = preQuoteDraftItemId,
            SourceStructuredItemTechnicalClassificationId =
                source.SourceStructuredItemTechnicalClassificationId,
            SystemCode = source.SystemCode,
            SystemOriginalText = source.SystemOriginalText,
            SystemSource = source.SystemSource,
            SystemConfidence = source.SystemConfidence,
            FrameCode = source.FrameCode,
            FrameOriginalText = source.FrameOriginalText,
            FrameSource = source.FrameSource,
            FrameConfidence = source.FrameConfidence,
            FinishCode = source.FinishCode,
            FinishOriginalText = source.FinishOriginalText,
            FinishSource = source.FinishSource,
            FinishConfidence = source.FinishConfidence,
            RequiresReview = source.RequiresReview,
            ReviewReasons = source.ReviewReasons
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }
}

public sealed class PreQuoteDraftItemTechnicalSelection
{
    private PreQuoteDraftItemTechnicalSelection() { }
    public Guid Id { get; private set; }
    public Guid PreQuoteDraftItemId { get; private set; }
    public string? RequestedSystemCode { get; private set; }
    public string? RequestedSystemOriginalText { get; private set; }
    public string? SuggestedSystemCode { get; private set; }
    public string? SelectedSystemCode { get; private set; }
    public string? RequestedGlassCode { get; private set; }
    public string? RequestedGlassOriginalText { get; private set; }
    public string? SuggestedGlassCode { get; private set; }
    public string? SelectedGlassCode { get; private set; }
    public string? RequestedFinishCode { get; private set; }
    public string? RequestedFinishOriginalText { get; private set; }
    public string? SuggestedFinishCode { get; private set; }
    public string? SelectedFinishCode { get; private set; }
    public string? RequestedHardwareCode { get; private set; }
    public string? RequestedHardwareOriginalText { get; private set; }
    public string? SuggestedHardwareCode { get; private set; }
    public string? SelectedHardwareCode { get; private set; }
    public PreQuoteDraftTechnicalSelectionState SelectionState { get; private set; }
    public bool RequiresReview { get; private set; }
    public decimal? Confidence { get; private set; }
    public string[] ReviewReasons { get; private set; } = [];
    public PreQuoteDraftTechnicalSelectionSource RequestedSource { get; private set; }
    public PreQuoteDraftTechnicalSelectionSource? SuggestedSource { get; private set; }
    public PreQuoteDraftTechnicalSelectionSource? SelectedSource { get; private set; }

    public static PreQuoteDraftItemTechnicalSelection Create(
        Guid preQuoteDraftItemId,
        PreQuoteDraftItemTechnicalSelectionSource source)
    {
        if (preQuoteDraftItemId == Guid.Empty
            || !Enum.IsDefined(source.SelectionState)
            || !Enum.IsDefined(source.RequestedSource)
            || source.SuggestedSource is { } suggestedSource
                && !Enum.IsDefined(suggestedSource)
            || source.SelectedSource is { } selectedSource
                && !Enum.IsDefined(selectedSource)
            || source.Confidence is < 0 or > 1)
        {
            throw new ArgumentException("Seleccion tecnica invalida.");
        }

        var reasons = NormalizeReasons(source.ReviewReasons ?? []);
        if (source.RequiresReview != (reasons.Length > 0))
        {
            throw new ArgumentException("Seleccion tecnica incoherente.");
        }

        return new()
        {
            Id = Guid.NewGuid(),
            PreQuoteDraftItemId = preQuoteDraftItemId,
            RequestedSystemCode = NormalizeOptionalCode(source.RequestedSystemCode),
            RequestedSystemOriginalText = NormalizeOptionalText(source.RequestedSystemOriginalText),
            SuggestedSystemCode = NormalizeOptionalCode(source.SuggestedSystemCode),
            SelectedSystemCode = NormalizeOptionalCode(source.SelectedSystemCode),
            RequestedGlassCode = NormalizeOptionalCode(source.RequestedGlassCode),
            RequestedGlassOriginalText = NormalizeOptionalText(source.RequestedGlassOriginalText),
            SuggestedGlassCode = NormalizeOptionalCode(source.SuggestedGlassCode),
            SelectedGlassCode = NormalizeOptionalCode(source.SelectedGlassCode),
            RequestedFinishCode = NormalizeOptionalCode(source.RequestedFinishCode),
            RequestedFinishOriginalText = NormalizeOptionalText(source.RequestedFinishOriginalText),
            SuggestedFinishCode = NormalizeOptionalCode(source.SuggestedFinishCode),
            SelectedFinishCode = NormalizeOptionalCode(source.SelectedFinishCode),
            RequestedHardwareCode = NormalizeOptionalCode(source.RequestedHardwareCode),
            RequestedHardwareOriginalText = NormalizeOptionalText(source.RequestedHardwareOriginalText),
            SuggestedHardwareCode = NormalizeOptionalCode(source.SuggestedHardwareCode),
            SelectedHardwareCode = NormalizeOptionalCode(source.SelectedHardwareCode),
            SelectionState = source.SelectionState,
            RequiresReview = source.RequiresReview,
            Confidence = source.Confidence,
            ReviewReasons = reasons,
            RequestedSource = source.RequestedSource,
            SuggestedSource = source.SuggestedSource,
            SelectedSource = source.SelectedSource
        };
    }

    public void UpdateSelected(
        PreQuoteDraftItemTechnicalSelectionEdit edit)
    {
        SelectedSystemCode = NormalizeOptionalCode(edit.SelectedSystemCode);
        SelectedGlassCode = NormalizeOptionalCode(edit.SelectedGlassCode);
        SelectedFinishCode = NormalizeOptionalCode(edit.SelectedFinishCode);
        SelectedHardwareCode = NormalizeOptionalCode(edit.SelectedHardwareCode);
        if (edit.ConfirmSelection && !HasSelectedValue())
        {
            SelectedSystemCode = SuggestedSystemCode;
            SelectedGlassCode = SuggestedGlassCode;
            SelectedFinishCode = SuggestedFinishCode;
            SelectedHardwareCode = SuggestedHardwareCode;
        }

        SelectedSource = HasSelectedValue()
            ? PreQuoteDraftTechnicalSelectionSource.Manual
            : null;
        SelectionState = HasSelectedValue()
            ? MatchesSuggestion()
                ? PreQuoteDraftTechnicalSelectionState.Confirmed
                : PreQuoteDraftTechnicalSelectionState.Modified
            : SuggestedSource is null
                ? PreQuoteDraftTechnicalSelectionState.Pending
                : PreQuoteDraftTechnicalSelectionState.Suggested;
    }

    private bool HasSelectedValue() =>
        SelectedSystemCode is not null
        || SelectedGlassCode is not null
        || SelectedFinishCode is not null
        || SelectedHardwareCode is not null;

    private bool MatchesSuggestion() =>
        string.Equals(SelectedSystemCode, SuggestedSystemCode, StringComparison.Ordinal)
        && string.Equals(SelectedGlassCode, SuggestedGlassCode, StringComparison.Ordinal)
        && string.Equals(SelectedFinishCode, SuggestedFinishCode, StringComparison.Ordinal)
        && string.Equals(SelectedHardwareCode, SuggestedHardwareCode, StringComparison.Ordinal);

    private static string[] NormalizeReasons(IReadOnlyList<string> reasons) =>
        reasons
            .Select(value => NormalizeRequiredCode(value, 100))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string? NormalizeOptionalCode(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeRequiredCode(value, 60);

    private static string NormalizeRequiredCode(string value, int maximum)
    {
        var code = value.Trim().ToUpperInvariant();
        if (code.Length == 0 || code.Length > maximum
            || !code.All(character =>
                character is >= 'A' and <= 'Z'
                || character is >= '0' and <= '9'
                || character is '_' or '-'))
        {
            throw new ArgumentException("Codigo de seleccion invalido.");
        }

        return code;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length > 500)
        {
            throw new ArgumentException("Texto de seleccion invalido.");
        }

        return text;
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
    public int? PageNumber { get; private set; }
    public string? SheetName { get; private set; }
    public string? CellRange { get; private set; }
    public EvidenceSourceType SourceType { get; private set; }
    public string Text { get; private set; } = string.Empty;

    public static PreQuoteDraftItemGlassEvidence Create(
        PreQuoteDraftItemGlassEvidenceSource source)
    {
        if (source.Sequence < 1)
        {
            throw new ArgumentException("Evidence de vidrio invalida.");
        }

        var text = GlassEvidenceValidation.ValidateEvidenceText(source.Text);
        var location = GlassEvidenceValidation.ValidateEvidenceLocation(
            source.SourceType, source.PageNumber, source.SheetName, source.CellRange);

        return new()
        {
            Id = Guid.NewGuid(),
            GlassSnapshotId = Guid.Empty,
            Sequence = source.Sequence,
            PageNumber = location.PageNumber,
            SourceType = source.SourceType,
            SheetName = location.SheetName,
            CellRange = location.CellRange,
            Text = text
        };
    }
}
public sealed record PreQuoteDraftItemSource(
    Guid SourceId, int Sequence, string? Reference, string Description,
    StructuredElementType ElementType, string? RawMeasurements,
    int? WidthMillimeters, int? HeightMillimeters, int? Quantity,
    PreQuoteDraftItemGlassSnapshotSource? Glass = null,
    PreQuoteDraftItemValuationSnapshotSource? Valuation = null,
    PreQuoteDraftItemTechnicalSnapshotSource? TechnicalSnapshot = null,
    PreQuoteDraftItemTechnicalSelectionSource? TechnicalSelection = null);
public sealed record PreQuoteDraftRequirementSource(
    Guid SourceId, int Sequence, RequirementCategory Category, string Value);
public sealed record PreQuoteDraftReferenceSource(
    Guid SourceId, int Sequence, string? Reference, string Description,
    string? Detail, int? Quantity);
public sealed record PreQuoteDraftIssueSource(
    Guid? SourceId, int Sequence, StructuredIssueCode Code, string Message,
    int? ItemSequence, int[] PageNumbers);
public sealed record PreQuoteDraftConflictSource(
    Guid SourceId, int Sequence, StructuredConflictCode Code, string Message,
    int[] ItemSequences, int[] PageNumbers);
public static class PreQuoteDraftIssueCodeMap
{
    public static bool TryMapGlassReviewReasonToIssueCode(
        GlassReviewReason reason,
        out StructuredIssueCode code)
    {
        (code, var isKnown) = reason switch
        {
            GlassReviewReason.GlassTypeNotIdentified => (StructuredIssueCode.GlassTypeNotIdentified, true),
            GlassReviewReason.GlassTypeAmbiguous => (StructuredIssueCode.GlassTypeAmbiguous, true),
            GlassReviewReason.GlassTypeConflict => (StructuredIssueCode.GlassTypeConflict, true),
            _ => (StructuredIssueCode.OcrReviewRequired, false)
        };
        return isKnown;
    }

    public static string MapContractCode(StructuredIssueCode value) => value switch
    {
        StructuredIssueCode.ProjectNameNotFound => "PROJECT_NAME_NOT_FOUND",
        StructuredIssueCode.NoQuoteableItemsFound => "NO_QUOTEABLE_ITEMS_FOUND",
        StructuredIssueCode.IncompleteTableRow => "INCOMPLETE_TABLE_ROW",
        StructuredIssueCode.MissingItemReference => "MISSING_ITEM_REFERENCE",
        StructuredIssueCode.MissingOrInvalidMeasurements =>
            "MISSING_OR_INVALID_MEASUREMENTS",
        StructuredIssueCode.MissingOrInvalidQuantity =>
            "MISSING_OR_INVALID_QUANTITY",
        StructuredIssueCode.UnknownElementType => "UNKNOWN_ELEMENT_TYPE",
        StructuredIssueCode.OcrReviewRequired => "OCR_REVIEW_REQUIRED",
        StructuredIssueCode.GlassTypeNotIdentified =>
            "GLASS_TYPE_NOT_IDENTIFIED",
        StructuredIssueCode.GlassTypeAmbiguous => "GLASS_TYPE_AMBIGUOUS",
        StructuredIssueCode.GlassTypeConflict => "GLASS_TYPE_CONFLICT",
        _ => "OCR_REVIEW_REQUIRED"
    };

    public static string MapIssueMessage(StructuredIssueCode code) => code switch
    {
        StructuredIssueCode.GlassTypeNotIdentified =>
            "No fue posible identificar el tipo de vidrio.",
        StructuredIssueCode.GlassTypeAmbiguous =>
            "Se identificaron múltiples tipos de vidrio posibles.",
        StructuredIssueCode.GlassTypeConflict =>
            "Se detectó información contradictoria sobre el tipo de vidrio.",
        _ => string.Empty
    };
}
public sealed record PreQuoteDraftItemEdit(
    Guid? Id, int Sequence, string? Reference, string Description,
    StructuredElementType ElementType, string? RawMeasurements,
    int? WidthMillimeters, int? HeightMillimeters, int? Quantity,
    bool IsIncluded,
    PreQuoteDraftItemTechnicalSelectionEdit? TechnicalSelection = null);
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
    int NotPriceableItemCount,
    int ItemsRequiringReviewCount,
    decimal? TotalAreaSquareMeters,
    decimal? GlassSubtotal,
    string? Currency,
    bool IsEconomicallyComplete,
    decimal? MinimumTechnicalSubtotal = null,
    decimal? ExpectedTechnicalSubtotal = null,
    decimal? MaximumTechnicalSubtotal = null,
    decimal? TransportMinimum = null,
    decimal? TransportExpected = null,
    decimal? TransportMaximum = null,
    decimal? AdministrationMinimum = null,
    decimal? AdministrationExpected = null,
    decimal? AdministrationMaximum = null,
    decimal? ContingencyMinimum = null,
    decimal? ContingencyExpected = null,
    decimal? ContingencyMaximum = null,
    decimal? ProfitMinimum = null,
    decimal? ProfitExpected = null,
    decimal? ProfitMaximum = null,
    decimal? VatMinimum = null,
    decimal? VatExpected = null,
    decimal? VatMaximum = null,
    decimal? FinalMinimum = null,
    decimal? FinalExpected = null,
    decimal? FinalMaximum = null,
    int? OverallConfidence = null,
    PreQuoteDraftPricingConfidenceLevel? ConfidenceLevel = null,
    IReadOnlyList<string>? Assumptions = null,
    IReadOnlyList<string>? MissingData = null,
    bool HasLimitedPricingScope = false)
{
    public bool HasNotPriceableItems => NotPriceableItemCount > 0;
}

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
        var mergedIssues = MergeIssues(issues, items);
        draft._issues.AddRange(mergedIssues.Select(x => PreQuoteDraftIssue.Create(draft.Id, x, createdAtUtc)));
        draft._conflicts.AddRange(conflicts.Select(x => PreQuoteDraftConflict.Create(draft.Id, x, createdAtUtc)));
        return draft;
    }

    private static string PageKey(int[] pageNumbers) =>
        string.Join(',', pageNumbers);

    private static IReadOnlyList<PreQuoteDraftIssueSource> MergeIssues(
        IReadOnlyList<PreQuoteDraftIssueSource> explicitIssues,
        IReadOnlyList<PreQuoteDraftItemSource> items)
    {
        var merged = new List<PreQuoteDraftIssueSource>();
        var existing = new HashSet<(StructuredIssueCode Code, int? ItemSequence, string PageKey)>();
        var sequence = 1;

        foreach (var issue in explicitIssues.OrderBy(x => x.Sequence))
        {
            var normalizedPages = issue.PageNumbers
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
            var normalized = issue with
            {
                Message = issue.Message.Trim(),
                Sequence = sequence,
                PageNumbers = normalizedPages
            };
            var identity = (normalized.Code, normalized.ItemSequence, PageKey(normalizedPages));
            if (existing.Add(identity))
            {
                merged.Add(normalized);
                sequence++;
            }
        }

        foreach (var item in items.OrderBy(x => x.Sequence))
        {
            if (item.Glass is null || item.Glass.ReviewReasons.Count == 0) continue;

            var pageNumbers = item.Glass.SourcePages
                .Distinct()
                .OrderBy(x => x)
                .ToArray();
            var reasons = item.Glass.ReviewReasons
                .Distinct()
                .ToArray();
            foreach (var reason in reasons)
            {
                if (!PreQuoteDraftIssueCodeMap.TryMapGlassReviewReasonToIssueCode(
                        reason, out var issueCode))
                {
                    continue;
                }

                var message = PreQuoteDraftIssueCodeMap.MapIssueMessage(issueCode);
                var itemSequence = item.Sequence;
                var identity = (issueCode, itemSequence, PageKey(pageNumbers));
                if (!existing.Add(identity))
                {
                    continue;
                }

                merged.Add(new PreQuoteDraftIssueSource(
                    null,
                    sequence,
                    issueCode,
                    message,
                    itemSequence,
                    pageNumbers));
                sequence++;
            }
        }

        return merged;
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
        var notPriceable = included.Where(
            x => x.ValuationStatus == PreQuoteDraftValuationStatus.NotPriceable)
            .ToArray();
        var requiringReview = included.Where(
            x => x.ValuationStatus == PreQuoteDraftValuationStatus.RequiresReview)
            .ToArray();

        var validValuedItems = valued
            .Where(x => x.ValuationSnapshot is not null
                && x.ValuationSnapshot.TotalAmount is not null
                && x.ValuationSnapshot.TotalAreaSquareMeters is not null
                && x.ValuationSnapshot.Currency is { } currency
                && !string.IsNullOrWhiteSpace(currency))
            .ToArray();

        var compatibleCurrencies = validValuedItems
            .Select(x => x.ValuationSnapshot!.Currency!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currency = compatibleCurrencies.Length == 1
            ? compatibleCurrencies[0]
            : null;

        var hasCurrencyCompatibility = valued.Length > 0 && currency is not null
            && valued.Length == validValuedItems.Length;

        var valuedArea = hasCurrencyCompatibility
            ? validValuedItems.Sum(
                x => x.ValuationSnapshot!.TotalAreaSquareMeters!)
            : 0;
        var valuedSubtotal = hasCurrencyCompatibility
            ? validValuedItems.Sum(
                x => x.ValuationSnapshot!.TotalAmount!)
            : 0;
        var preliminarySnapshots = hasCurrencyCompatibility
            ? validValuedItems
                .Select(x => x.ValuationSnapshot!)
                .Where(x => x.ItemExpectedAmount is not null)
                .ToArray()
            : [];
        var totals = preliminarySnapshots.Length > 0
            ? PreQuotePreliminaryPricing.CalculateTotals(
                preliminarySnapshots, Location)
            : null;

        return new(
            included.Length,
            included.Sum(x => (int?)x.Quantity ?? 0),
            valued.Length,
            pending.Length,
            stale.Length,
            notPriceable.Length,
            requiringReview.Length,
            valuedArea == 0 ? null : valuedArea,
            valuedSubtotal == 0 ? null : valuedSubtotal,
            currency,
            valued.Length > 0 && stale.Length == 0 && pending.Length == 0 &&
                notPriceable.Length == 0 &&
                requiringReview.Length == 0 && currency is not null &&
                valued.All(x => x.ValuationSnapshot is not null &&
                    x.ValuationSnapshot.Currency is not null &&
                    !string.IsNullOrWhiteSpace(x.ValuationSnapshot.Currency)),
            totals?.MinimumTechnicalSubtotal,
            totals?.ExpectedTechnicalSubtotal,
            totals?.MaximumTechnicalSubtotal,
            totals?.TransportMinimum,
            totals?.TransportExpected,
            totals?.TransportMaximum,
            totals?.AdministrationMinimum,
            totals?.AdministrationExpected,
            totals?.AdministrationMaximum,
            totals?.ContingencyMinimum,
            totals?.ContingencyExpected,
            totals?.ContingencyMaximum,
            totals?.ProfitMinimum,
            totals?.ProfitExpected,
            totals?.ProfitMaximum,
            totals?.VatMinimum,
            totals?.VatExpected,
            totals?.VatMaximum,
            totals?.FinalMinimum,
            totals?.FinalExpected,
            totals?.FinalMaximum,
            totals?.OverallConfidence,
            totals?.ConfidenceLevel,
            totals?.Assumptions,
            totals?.MissingData,
            totals?.HasLimitedPricingScope ?? false);
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
        EnsureEconomicallyApprovable(included);
        Status = PreQuoteDraftStatus.Approved;
        ApprovedByUserId = userId;
        ApprovedAtUtc = approvedAtUtc;
        UpdatedByUserId = userId;
        UpdatedAtUtc = approvedAtUtc;
        Version++;
    }

    private static readonly string[] EconomicApprovalBlockingCodes =
    [
        "ALUMINUM_BASE_RATE_NOT_CONFIGURED",
        "PROJECT_LOCATION_NOT_CONFIRMED",
        "TRANSPORT_NOT_CONFIRMED"
    ];

    private void EnsureEconomicallyApprovable(IReadOnlyCollection<PreQuoteDraftItem> includedItems)
    {
        if (includedItems.Any(IsItemEconomicallyBlockedForApproval))
        {
            throw new InvalidOperationException("ECONOMIC_APPROVAL_BLOCKED");
        }

        var summary = EconomicSummary;
        if (!summary.IsEconomicallyComplete
            || summary.HasLimitedPricingScope
            || summary.HasNotPriceableItems
            || summary.PendingValuationItemCount > 0
            || summary.StaleValuationItemCount > 0
            || summary.NotPriceableItemCount > 0
            || summary.ItemsRequiringReviewCount > 0
            || string.IsNullOrWhiteSpace(summary.Currency)
            || summary.FinalMinimum is null
            || summary.FinalExpected is null
            || summary.FinalMaximum is null
            || HasMissingEconomicData(summary.MissingData)
            || HasBlockingEconomicCode(summary.Assumptions))
        {
            throw new InvalidOperationException("ECONOMIC_APPROVAL_BLOCKED");
        }
    }

    private static bool IsItemEconomicallyBlockedForApproval(PreQuoteDraftItem item)
    {
        var valuation = item.ValuationSnapshot;
        return valuation is null
            || item.ValuationStatus != PreQuoteDraftValuationStatus.Valued
            || valuation.Status != PreQuoteDraftValuationStatus.Valued
            || valuation.InvalidatedAtUtc is not null
            || valuation.RequiresReview == true
            || !item.IsCompleteForApproval
            || item.GlassSnapshot?.RequiresReview == true
            || item.TechnicalSnapshot?.RequiresReview == true
            || string.IsNullOrWhiteSpace(valuation.Currency)
            || valuation.ItemMinimumAmount is null
            || valuation.ItemExpectedAmount is null
            || valuation.ItemMaximumAmount is null
            || HasMissingEconomicData(valuation.MissingData)
            || HasBlockingEconomicCode(valuation.Assumptions);
    }

    private static bool HasMissingEconomicData(IReadOnlyList<string>? values)
    {
        return values is not null && values.Count > 0;
    }

    private static bool HasBlockingEconomicCode(IReadOnlyList<string>? values)
    {
        return values is not null
            && values.Any(value => EconomicApprovalBlockingCodes.Contains(value, StringComparer.Ordinal));
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
    public PreQuoteDraftItemTechnicalSnapshot? TechnicalSnapshot { get; private set; }
    public PreQuoteDraftItemTechnicalSelection? TechnicalSelection { get; private set; }
    public Guid CreatedByUserId { get; private set; } public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } public DateTimeOffset UpdatedAtUtc { get; private set; }
    public PreQuoteDraft Draft { get; private set; } = null!;
    public bool IsCompleteForApproval => !string.IsNullOrWhiteSpace(Description) && ElementType != StructuredElementType.Other && WidthMillimeters > 0 && HeightMillimeters > 0 && Quantity > 0;
    internal static PreQuoteDraftItem FromAi(Guid draftId, PreQuoteDraftItemSource x, Guid user, DateTimeOffset at) =>
        Create(draftId, x.Sequence, PreQuoteDraftOrigin.Ai, x.SourceId, x.Sequence, x.Reference, x.Description, x.ElementType, x.RawMeasurements, x.WidthMillimeters, x.HeightMillimeters, x.Quantity, true, user, at, x.Glass, x.Valuation, x.TechnicalSnapshot, x.TechnicalSelection ?? BuildInitialSelection(x.Glass, x.TechnicalSnapshot));
    internal static PreQuoteDraftItem Manual(
        Guid draftId, PreQuoteDraftItemEdit x, Guid user, DateTimeOffset at) =>
        Create(draftId, x.Sequence, PreQuoteDraftOrigin.Manual, null, null,
            x.Reference, x.Description, x.ElementType, x.RawMeasurements,
            x.WidthMillimeters, x.HeightMillimeters, x.Quantity, x.IsIncluded,
            user, at, null, null, null);
    private static PreQuoteDraftItem Create(
        Guid draftId, int sequence, PreQuoteDraftOrigin origin, Guid? sourceId,
        int? sourceSequence, string? reference, string description,
        StructuredElementType type, string? raw, int? width, int? height,
        int? quantity, bool included, Guid user, DateTimeOffset at,
        PreQuoteDraftItemGlassSnapshotSource? glass,
        PreQuoteDraftItemValuationSnapshotSource? valuation,
        PreQuoteDraftItemTechnicalSnapshotSource? technical,
        PreQuoteDraftItemTechnicalSelectionSource? selection = null)
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
        var technicalSnapshot = technical is null
            ? null
            : PreQuoteDraftItemTechnicalSnapshot.Create(itemId, technical);
        var technicalSelection = selection is null
            ? null
            : PreQuoteDraftItemTechnicalSelection.Create(itemId, selection);
        var preliminaryPricing = valuation is null
            ? null
            : PreQuotePreliminaryPricing.TryCalculate(
                type, description, valuation, technicalSnapshot);
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
                valuation.InvalidationReason,
                preliminaryPricing);
        var isNotPriceable = technicalSnapshot?.ReviewReasons
            .Contains("SYSTEM_NOT_CURRENTLY_PRICEABLE",
                StringComparer.Ordinal) == true;
        var valuationStatus = valuationSnapshot?.Status
            ?? (isNotPriceable
                ? PreQuoteDraftValuationStatus.NotPriceable
                : glassSnapshot?.RequiresReview is true
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
            TechnicalSnapshot = technicalSnapshot,
            TechnicalSelection = technicalSelection,
            CreatedByUserId = user,
            UpdatedByUserId = user,
            CreatedAtUtc = at,
            UpdatedAtUtc = at
        };
    }

    private static PreQuoteDraftItemTechnicalSelectionSource?
        BuildInitialSelection(
            PreQuoteDraftItemGlassSnapshotSource? glass,
            PreQuoteDraftItemTechnicalSnapshotSource? technical)
    {
        if (glass is null && technical is null)
        {
            return null;
        }

        return new(
            RequestedSystemCode: technical?.SystemCode,
            RequestedSystemOriginalText: technical?.SystemOriginalText,
            RequestedGlassCode: glass?.NormalizedCodeSnapshot,
            RequestedGlassOriginalText: glass?.RawSpecification,
            RequestedFinishCode: technical?.FinishCode,
            RequestedFinishOriginalText: technical?.FinishOriginalText,
            RequiresReview: false,
            ReviewReasons: []);
    }
    internal void Update(PreQuoteDraftItemEdit x, Guid user, DateTimeOffset at)
    {
        PreQuoteDraft.Dimensions(x.WidthMillimeters, x.HeightMillimeters);
        PreQuoteDraft.Quantity(x.Quantity);

        var previousWidth = WidthMillimeters;
        var previousHeight = HeightMillimeters;
        var previousQuantity = Quantity;
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

        var changedEconomicInputs = 0;
        if (widthChanged) changedEconomicInputs++;
        if (heightChanged) changedEconomicInputs++;
        if (quantityChanged) changedEconomicInputs++;

        if (ValuationStatus == PreQuoteDraftValuationStatus.Valued
            && ValuationSnapshot is not null
            && (x.WidthMillimeters != previousWidth
                || x.HeightMillimeters != previousHeight
                || x.Quantity != previousQuantity))
        {
            var reason = changedEconomicInputs > 1
                ? PreQuoteDraftValuationInvalidationReason.MultipleInputsChanged
                : widthChanged
                    ? PreQuoteDraftValuationInvalidationReason.WidthChanged
                    : heightChanged
                        ? PreQuoteDraftValuationInvalidationReason.HeightChanged
                        : PreQuoteDraftValuationInvalidationReason.QuantityChanged;
            ValuationSnapshot.Invalidate(at, reason);
            ValuationStatus = PreQuoteDraftValuationStatus.Stale;
        }

        TechnicalSelection?.UpdateSelected(x.TechnicalSelection ?? new(
            TechnicalSelection.SelectedSystemCode,
            TechnicalSelection.SelectedGlassCode,
            TechnicalSelection.SelectedFinishCode,
            TechnicalSelection.SelectedHardwareCode,
            ConfirmSelection: false));

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
    private PreQuoteDraftIssue(){} public Guid? SourceStructuredIssueId{get;private set;} public int? SourceIssueSequence{get;private set;} public StructuredIssueCode Code{get;private set;} public string Message{get;private set;}=""; public int? ItemSequence{get;private set;} public int[] PageNumbers{get;private set;}=[]; public PreQuoteDraft Draft{get;private set;}=null!;
    internal static PreQuoteDraftIssue Create(Guid d,PreQuoteDraftIssueSource x,DateTimeOffset at)=>new(){Id=Guid.NewGuid(),PreQuoteDraftId=d,Sequence=x.Sequence,SourceStructuredIssueId=x.SourceId,SourceIssueSequence=x.SourceId is null ? null : x.Sequence,Code=x.Code,Message=x.Message,ItemSequence=x.ItemSequence,PageNumbers=x.PageNumbers.ToArray(),ResolutionStatus=PreQuoteDraftResolutionStatus.Pending,CreatedAtUtc=at};
}
public sealed class PreQuoteDraftConflict : PreQuoteDraftFinding
{
    private PreQuoteDraftConflict(){} public Guid SourceStructuredConflictId{get;private set;} public int SourceConflictSequence{get;private set;} public StructuredConflictCode Code{get;private set;} public string Message{get;private set;}=""; public int[] ItemSequences{get;private set;}=[]; public int[] PageNumbers{get;private set;}=[]; public PreQuoteDraft Draft{get;private set;}=null!;
    internal static PreQuoteDraftConflict Create(Guid d,PreQuoteDraftConflictSource x,DateTimeOffset at)=>new(){Id=Guid.NewGuid(),PreQuoteDraftId=d,Sequence=x.Sequence,SourceStructuredConflictId=x.SourceId,SourceConflictSequence=x.Sequence,Code=x.Code,Message=x.Message,ItemSequences=x.ItemSequences.ToArray(),PageNumbers=x.PageNumbers.ToArray(),ResolutionStatus=PreQuoteDraftResolutionStatus.Pending,CreatedAtUtc=at};
}
