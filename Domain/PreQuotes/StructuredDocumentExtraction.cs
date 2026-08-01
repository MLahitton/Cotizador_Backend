using Domain.Catalogs;

namespace Domain.PreQuotes;

public enum StructuredExtractionStatus { Completed = 1, RequiresReview = 2 }
public enum EvidenceSourceType { Native = 1, Ocr = 2 }
public enum GlassAssignmentScope { Item = 1, Section, General, Unassigned }
public enum GlassReviewReason
{
    GlassTypeNotIdentified = 1,
    GlassTypeAmbiguous,
    GlassTypeConflict
}
public enum GlassValuationStatus { Valued = 1, NotValued = 2 }
public enum GlassValuationReason
{
    MissingMeasurements = 1,
    MissingQuantity,
    GlassNotNormalized,
    GlassTypeNotResolved,
    PriceRangeNotAvailable,
    CurrencyMismatch
}
public sealed record StructuredItemGlassValuationInput(
    GlassValuationStatus Status,
    GlassValuationReason? Reason,
    Guid? GlassTypeId,
    Guid? GlassPriceRangeVersionId,
    int? PriceRangeVersion,
    GlassPriceRangeStatus? PriceRangeStatus,
    string? Currency,
    decimal? UnitAreaSquareMeters,
    decimal? TotalAreaSquareMeters,
    decimal? MinimumPricePerSquareMeter,
    decimal? MaximumPricePerSquareMeter,
    decimal? MinimumAmount,
    decimal? MaximumAmount);
public enum StructuredElementType
{
    Window = 1, Door, Facade, Partition, Railing, Skylight, Other
}
public enum RequirementCategory
{
    GlassSpecification = 1,
    ProfileSpecification,
    Finish,
    AccessoriesAndSealants,
    GeneralNote
}
public enum StructuredIssueCode
{
    ProjectNameNotFound = 1,
    NoQuoteableItemsFound,
    IncompleteTableRow,
    MissingItemReference,
    MissingOrInvalidMeasurements,
    MissingOrInvalidQuantity,
    UnknownElementType,
    OcrReviewRequired,
    GlassTypeNotIdentified,
    GlassTypeAmbiguous,
    GlassTypeConflict
}
public enum StructuredConflictCode
{
    ConflictingProjectName = 1,
    ConflictingClientName,
    ConflictingLocation,
    DuplicateItemReference
}

public sealed record StructuredItemInput(
    int Sequence, string? Reference, string Description,
    StructuredElementType ElementType, string? RawMeasurements,
    int? WidthMillimeters, int? HeightMillimeters, int? Quantity,
    bool RequiresReview,
    StructuredItemGlassInput? Glass = null,
    StructuredItemGlassValuationInput? Valuation = null);
public sealed record StructuredItemGlassEvidenceInput(
    int Sequence, int PageNumber, EvidenceSourceType SourceType, string Text);
public sealed record StructuredItemGlassInput(
    Guid? GlassTypeId,
    string? RawSpecification,
    string? NormalizedCodeSnapshot,
    GlassAssignmentScope AssignmentScope,
    bool RequiresReview,
    IReadOnlyList<GlassReviewReason> ReviewReasons,
    IReadOnlyList<int> SourcePages,
    IReadOnlyList<StructuredItemGlassEvidenceInput> Evidence);
public sealed record StructuredRequirementInput(
    int Sequence, RequirementCategory Category, string Value);
public sealed record StructuredDocumentReferenceInput(
    int Sequence, string? Reference, string Description,
    string? Detail, int? Quantity);
public sealed record StructuredIssueInput(
    int Sequence, StructuredIssueCode Code, string Message,
    int? ItemSequence, int[] PageNumbers);
public sealed record StructuredConflictInput(
    int Sequence, StructuredConflictCode Code, string Message,
    int[] ItemSequences, int[] PageNumbers);

public sealed class StructuredDocumentExtraction
{
    private readonly List<StructuredExtractionItem> _items = [];
    private readonly List<StructuredExtractionRequirement> _requirements = [];
    private readonly List<StructuredExtractionDocumentReference>
        _documentReferences = [];
    private readonly List<StructuredExtractionIssue> _issues = [];
    private readonly List<StructuredExtractionConflict> _conflicts = [];

    private StructuredDocumentExtraction() { }

    public Guid Id { get; private set; }
    public Guid DocumentExtractionResultId { get; private set; }
    public StructuredExtractionStatus Status { get; private set; }
    public string? ProjectName { get; private set; }
    public string? ClientName { get; private set; }
    public string? Location { get; private set; }
    public int ItemCount { get; private set; }
    public int DocumentReferenceCount { get; private set; }
    public int ItemsRequiringReview { get; private set; }
    public int KnownQuoteableUnitCount { get; private set; }
    public int? IdentifiedGlassItemCount { get; private set; }
    public int? GlassItemsRequiringReview { get; private set; }
    public string ProcessingMethod { get; private set; } = string.Empty;
    public int DurationMs { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DocumentExtractionResult DocumentExtractionResult { get; private set; } = null!;
    public IReadOnlyCollection<StructuredExtractionItem> Items => _items;
    public IReadOnlyCollection<StructuredExtractionRequirement> Requirements => _requirements;
    public IReadOnlyCollection<StructuredExtractionDocumentReference> DocumentReferences => _documentReferences;
    public IReadOnlyCollection<StructuredExtractionIssue> Issues => _issues;
    public IReadOnlyCollection<StructuredExtractionConflict> Conflicts => _conflicts;

    public static StructuredDocumentExtraction Create(
        Guid resultId, StructuredExtractionStatus status,
        string? projectName, string? clientName, string? location,
        int itemCount, int referenceCount, int reviewCount,
        int knownUnits, string method, int durationMs,
        IReadOnlyList<StructuredItemInput> items,
        IReadOnlyList<StructuredRequirementInput> requirements,
        IReadOnlyList<StructuredDocumentReferenceInput> references,
        IReadOnlyList<StructuredIssueInput> issues,
        IReadOnlyList<StructuredConflictInput> conflicts,
        DateTimeOffset createdAtUtc,
        int? identifiedGlassItemCount = null,
        int? glassItemsRequiringReview = null)
    {
        if (resultId == Guid.Empty || createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Identificador y fecha UTC son obligatorios.");
        if (!Enum.IsDefined(status)
            || method is not ("rule_based_v1" or "rule_based_v2")
            || durationMs < 0 || itemCount != items.Count
            || referenceCount != references.Count
            || reviewCount != items.Count(x => x.RequiresReview)
            || knownUnits != items.Sum(x => x.Quantity ?? 0)
            || (identifiedGlassItemCount is null)
                != (glassItemsRequiringReview is null)
            || identifiedGlassItemCount is < 0
            || glassItemsRequiringReview is < 0
            || identifiedGlassItemCount is { } identified
                && identified != items.Count(x =>
                    x.Glass?.NormalizedCodeSnapshot is not null)
            || glassItemsRequiringReview is { } glassReview
                && glassReview != items.Count(x =>
                    x.Glass?.RequiresReview == true))
            throw new ArgumentException("El resumen estructurado no es coherente.");

        ValidateSequences(items, x => x.Sequence, nameof(items));
        ValidateSequences(
            requirements,
            x => x.Sequence,
            nameof(requirements));
        ValidateSequences(
            references,
            x => x.Sequence,
            nameof(references));
        ValidateSequences(issues, x => x.Sequence, nameof(issues));
        ValidateSequences(
            conflicts,
            x => x.Sequence,
            nameof(conflicts));

        var entity = new StructuredDocumentExtraction
        {
            Id = Guid.NewGuid(),
            DocumentExtractionResultId = resultId,
            Status = status,
            ProjectName = NormalizeOptional(projectName),
            ClientName = NormalizeOptional(clientName),
            Location = NormalizeOptional(location),
            ItemCount = itemCount,
            DocumentReferenceCount = referenceCount,
            ItemsRequiringReview = reviewCount,
            KnownQuoteableUnitCount = knownUnits,
            IdentifiedGlassItemCount = identifiedGlassItemCount,
            GlassItemsRequiringReview = glassItemsRequiringReview,
            ProcessingMethod = method,
            DurationMs = durationMs,
            CreatedAtUtc = createdAtUtc
        };
        entity._items.AddRange(items.Select(x => StructuredExtractionItem.Create(entity.Id, x, createdAtUtc)));
        entity._requirements.AddRange(requirements.Select(x => StructuredExtractionRequirement.Create(entity.Id, x, createdAtUtc)));
        entity._documentReferences.AddRange(references.Select(x => StructuredExtractionDocumentReference.Create(entity.Id, x, createdAtUtc)));
        entity._issues.AddRange(issues.Select(x => StructuredExtractionIssue.Create(entity.Id, x, createdAtUtc)));
        entity._conflicts.AddRange(conflicts.Select(x => StructuredExtractionConflict.Create(entity.Id, x, createdAtUtc)));
        return entity;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateSequences<T>(
        IReadOnlyList<T> values,
        Func<T, int> sequenceSelector,
        string parameterName)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (sequenceSelector(values[index]) != index + 1)
            {
                throw new ArgumentException(
                    "Las secuencias deben ser consecutivas, ordenadas y comenzar en uno.",
                    parameterName);
            }
        }
    }
}

public sealed class StructuredExtractionItem
{
    private StructuredExtractionItem() { }
    public Guid Id { get; private set; }
    public Guid StructuredDocumentExtractionId { get; private set; }
    public int Sequence { get; private set; }
    public string? Reference { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public StructuredElementType ElementType { get; private set; }
    public string? RawMeasurements { get; private set; }
    public int? WidthMillimeters { get; private set; }
    public int? HeightMillimeters { get; private set; }
    public int? Quantity { get; private set; }
    public bool RequiresReview { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredDocumentExtraction StructuredDocumentExtraction { get; private set; } = null!;
    public StructuredExtractionItemGlassDetection? GlassDetection { get; private set; }
    public StructuredExtractionItemGlassValuation? GlassValuation { get; private set; }
    internal static StructuredExtractionItem Create(Guid parentId, StructuredItemInput x, DateTimeOffset at)
    {
        if (x.Sequence < 1 || string.IsNullOrWhiteSpace(x.Description)
            || !Enum.IsDefined(x.ElementType)
            || (x.WidthMillimeters is null) != (x.HeightMillimeters is null)
            || x.WidthMillimeters is <= 0 || x.HeightMillimeters is <= 0
            || x.Quantity is <= 0) throw new ArgumentException("Item estructurado inválido.");
        var item = new StructuredExtractionItem { Id = Guid.NewGuid(), StructuredDocumentExtractionId = parentId,
            Sequence = x.Sequence, Reference = Trim(x.Reference),
            Description = x.Description.Trim(), ElementType = x.ElementType,
            RawMeasurements = Trim(x.RawMeasurements), WidthMillimeters = x.WidthMillimeters,
            HeightMillimeters = x.HeightMillimeters, Quantity = x.Quantity,
            RequiresReview = x.RequiresReview, CreatedAtUtc = at };
        item.GlassDetection = x.Glass is null
            ? null
            : StructuredExtractionItemGlassDetection.Create(
                item.Id, x.Glass, at);
        item.GlassValuation = x.Valuation is null
            ? null
            : StructuredExtractionItemGlassValuation.Create(
                item.Id, x.Valuation, at);
        return item;
    }
    private static string? Trim(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
}

public sealed class StructuredExtractionItemGlassValuation
{
    private StructuredExtractionItemGlassValuation() { }
    public Guid Id { get; private set; }
    public Guid StructuredExtractionItemId { get; private set; }
    public GlassValuationStatus Status { get; private set; }
    public GlassValuationReason? Reason { get; private set; }
    public Guid? GlassTypeId { get; private set; }
    public Guid? GlassPriceRangeVersionId { get; private set; }
    public int? PriceRangeVersion { get; private set; }
    public GlassPriceRangeStatus? PriceRangeStatus { get; private set; }
    public string? Currency { get; private set; }
    public decimal? UnitAreaSquareMeters { get; private set; }
    public decimal? TotalAreaSquareMeters { get; private set; }
    public decimal? MinimumPricePerSquareMeter { get; private set; }
    public decimal? MaximumPricePerSquareMeter { get; private set; }
    public decimal? MinimumAmount { get; private set; }
    public decimal? MaximumAmount { get; private set; }
    public DateTimeOffset CalculatedAtUtc { get; private set; }
    public StructuredExtractionItem StructuredExtractionItem { get; private set; } = null!;
    public GlassType? GlassType { get; private set; }
    public GlassPriceRangeVersion? GlassPriceRangeVersion { get; private set; }

    public static StructuredItemGlassValuationInput Calculate(
        int widthMillimeters, int heightMillimeters, int quantity,
        Guid glassTypeId, Guid priceRangeId, int priceRangeVersion,
        GlassPriceRangeStatus priceRangeStatus, string currency,
        decimal minimumPrice, decimal maximumPrice)
    {
        if (widthMillimeters <= 0 || heightMillimeters <= 0
            || quantity <= 0 || glassTypeId == Guid.Empty
            || priceRangeId == Guid.Empty || priceRangeVersion <= 0
            || minimumPrice <= 0 || maximumPrice <= 0
            || maximumPrice < minimumPrice
            || string.IsNullOrWhiteSpace(currency)
            || currency.Trim().Length != 3)
            throw new ArgumentException("Datos de valoracion invalidos.");
        var unitArea = (decimal)widthMillimeters * heightMillimeters
            / 1_000_000m;
        var totalArea = unitArea * quantity;
        return new(GlassValuationStatus.Valued, null, glassTypeId,
            priceRangeId, priceRangeVersion, priceRangeStatus,
            currency.Trim().ToUpperInvariant(), unitArea, totalArea,
            minimumPrice, maximumPrice,
            Math.Round(totalArea * minimumPrice, 2,
                MidpointRounding.AwayFromZero),
            Math.Round(totalArea * maximumPrice, 2,
                MidpointRounding.AwayFromZero));
    }

    internal static StructuredExtractionItemGlassValuation Create(
        Guid itemId, StructuredItemGlassValuationInput input,
        DateTimeOffset calculatedAtUtc)
    {
        if (itemId == Guid.Empty || calculatedAtUtc.Offset != TimeSpan.Zero
            || !Enum.IsDefined(input.Status)
            || input.Reason is { } reason && !Enum.IsDefined(reason))
            throw new ArgumentException("Valoracion de vidrio invalida.");
        var valued = input.Status == GlassValuationStatus.Valued;
        if (valued != (input.Reason is null)
            || valued && (input.GlassTypeId is null
                || input.GlassPriceRangeVersionId is null
                || input.PriceRangeVersion is null
                || input.PriceRangeStatus is null
                || string.IsNullOrWhiteSpace(input.Currency)
                || input.Currency.Length != 3
                || input.UnitAreaSquareMeters is <= 0
                || input.TotalAreaSquareMeters is <= 0
                || input.MinimumPricePerSquareMeter is <= 0
                || input.MaximumPricePerSquareMeter < input.MinimumPricePerSquareMeter
                || input.MinimumAmount is < 0
                || input.MaximumAmount < input.MinimumAmount)
            || !valued && new object?[] { input.GlassPriceRangeVersionId,
                input.PriceRangeVersion, input.PriceRangeStatus, input.Currency,
                input.UnitAreaSquareMeters, input.TotalAreaSquareMeters,
                input.MinimumPricePerSquareMeter,
                input.MaximumPricePerSquareMeter, input.MinimumAmount,
                input.MaximumAmount }.Any(value => value is not null))
            throw new ArgumentException("Snapshot de valoracion incoherente.");
        return new()
        {
            Id = Guid.NewGuid(), StructuredExtractionItemId = itemId,
            Status = input.Status, Reason = input.Reason,
            GlassTypeId = input.GlassTypeId,
            GlassPriceRangeVersionId = input.GlassPriceRangeVersionId,
            PriceRangeVersion = input.PriceRangeVersion,
            PriceRangeStatus = input.PriceRangeStatus,
            Currency = input.Currency?.ToUpperInvariant(),
            UnitAreaSquareMeters = input.UnitAreaSquareMeters,
            TotalAreaSquareMeters = input.TotalAreaSquareMeters,
            MinimumPricePerSquareMeter = input.MinimumPricePerSquareMeter,
            MaximumPricePerSquareMeter = input.MaximumPricePerSquareMeter,
            MinimumAmount = input.MinimumAmount,
            MaximumAmount = input.MaximumAmount,
            CalculatedAtUtc = calculatedAtUtc
        };
    }
}

public sealed class StructuredExtractionItemGlassDetection
{
    private readonly List<StructuredExtractionItemGlassReviewReason>
        _reviewReasons = [];
    private readonly List<StructuredExtractionItemGlassSourcePage>
        _sourcePages = [];
    private readonly List<StructuredExtractionItemGlassEvidence> _evidence = [];

    private StructuredExtractionItemGlassDetection() { }

    public Guid Id { get; private set; }
    public Guid StructuredExtractionItemId { get; private set; }
    public Guid? GlassTypeId { get; private set; }
    public string? RawSpecification { get; private set; }
    public string? NormalizedCodeSnapshot { get; private set; }
    public GlassAssignmentScope AssignmentScope { get; private set; }
    public bool RequiresReview { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredExtractionItem StructuredExtractionItem { get; private set; }
        = null!;
    public Domain.Catalogs.GlassType? GlassType { get; private set; }
    public IReadOnlyCollection<StructuredExtractionItemGlassReviewReason>
        ReviewReasons => _reviewReasons;
    public IReadOnlyCollection<StructuredExtractionItemGlassSourcePage>
        SourcePages => _sourcePages;
    public IReadOnlyCollection<StructuredExtractionItemGlassEvidence>
        Evidence => _evidence;

    internal static StructuredExtractionItemGlassDetection Create(
        Guid itemId,
        StructuredItemGlassInput input,
        DateTimeOffset createdAtUtc)
    {
        var raw = NormalizeOptional(input.RawSpecification, 500);
        var code = NormalizeOptional(input.NormalizedCodeSnapshot, 30);
        var identified = code is not null;
        if (itemId == Guid.Empty
            || createdAtUtc.Offset != TimeSpan.Zero
            || !Enum.IsDefined(input.AssignmentScope)
            || input.ReviewReasons.Any(reason => !Enum.IsDefined(reason))
            || input.RequiresReview != (input.ReviewReasons.Count > 0)
            || identified != input.GlassTypeId.HasValue
            || input.ReviewReasons.Distinct().Count()
                != input.ReviewReasons.Count
            || input.SourcePages.Any(page => page <= 0)
            || !input.SourcePages.SequenceEqual(
                input.SourcePages.Distinct().Order())
            || !IsAssignmentCoherent(input, raw, code))
        {
            throw new ArgumentException("Deteccion de vidrio invalida.");
        }

        var entity = new StructuredExtractionItemGlassDetection
        {
            Id = Guid.NewGuid(),
            StructuredExtractionItemId = itemId,
            GlassTypeId = input.GlassTypeId,
            RawSpecification = raw,
            NormalizedCodeSnapshot = code,
            AssignmentScope = input.AssignmentScope,
            RequiresReview = input.RequiresReview,
            CreatedAtUtc = createdAtUtc
        };
        entity._reviewReasons.AddRange(input.ReviewReasons.Select(
            (reason, index) => StructuredExtractionItemGlassReviewReason.Create(
                entity.Id, index + 1, reason, createdAtUtc)));
        entity._sourcePages.AddRange(input.SourcePages.Select(
            (page, index) => StructuredExtractionItemGlassSourcePage.Create(
                entity.Id, index + 1, page, createdAtUtc)));
        entity._evidence.AddRange(input.Evidence.Select(value =>
            StructuredExtractionItemGlassEvidence.Create(
                entity.Id, value, createdAtUtc)));
        if (entity._evidence.Select(value => new
            {
                value.PageNumber,
                value.SourceType,
                value.Text
            }).Distinct().Count() != entity._evidence.Count
            || !entity._evidence.Select(value => value.Sequence)
                .SequenceEqual(Enumerable.Range(1, entity._evidence.Count)))
        {
            throw new ArgumentException("Evidence de vidrio invalida.");
        }
        return entity;
    }

    private static bool IsAssignmentCoherent(
        StructuredItemGlassInput input,
        string? raw,
        string? code)
    {
        var unassigned = input.AssignmentScope
            == GlassAssignmentScope.Unassigned;
        if (unassigned)
        {
            return raw is null
                && code is null
                && input.RequiresReview
                && input.ReviewReasons.SequenceEqual(
                    [GlassReviewReason.GlassTypeNotIdentified])
                && input.SourcePages.Count == 0
                && input.Evidence.Count == 0;
        }
        if (raw is null || input.Evidence.Count == 0)
        {
            return false;
        }
        var evidencePages = input.Evidence.Select(value => value.PageNumber)
            .Distinct().Order();
        return input.SourcePages.SequenceEqual(evidencePages)
            && (code is not null
                || input.RequiresReview
                    && input.ReviewReasons.Count > 0);
    }

    private static string? NormalizeOptional(string? value, int maximum)
    {
        if (value is null)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximum
            || value != value.Trim())
        {
            throw new ArgumentException("Texto de vidrio invalido.");
        }
        return value;
    }
}

public sealed class StructuredExtractionItemGlassReviewReason
{
    private StructuredExtractionItemGlassReviewReason() { }
    public Guid Id { get; private set; }
    public Guid GlassDetectionId { get; private set; }
    public int Sequence { get; private set; }
    public GlassReviewReason Code { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredExtractionItemGlassDetection GlassDetection { get; private set; }
        = null!;
    internal static StructuredExtractionItemGlassReviewReason Create(
        Guid parentId, int sequence, GlassReviewReason code, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), GlassDetectionId = parentId,
            Sequence = sequence, Code = code, CreatedAtUtc = at };
}

public sealed class StructuredExtractionItemGlassSourcePage
{
    private StructuredExtractionItemGlassSourcePage() { }
    public Guid Id { get; private set; }
    public Guid GlassDetectionId { get; private set; }
    public int Sequence { get; private set; }
    public int PageNumber { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredExtractionItemGlassDetection GlassDetection { get; private set; }
        = null!;
    internal static StructuredExtractionItemGlassSourcePage Create(
        Guid parentId, int sequence, int page, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), GlassDetectionId = parentId,
            Sequence = sequence, PageNumber = page, CreatedAtUtc = at };
}

public sealed class StructuredExtractionItemGlassEvidence
{
    private StructuredExtractionItemGlassEvidence() { }
    public Guid Id { get; private set; }
    public Guid GlassDetectionId { get; private set; }
    public int Sequence { get; private set; }
    public int PageNumber { get; private set; }
    public EvidenceSourceType SourceType { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredExtractionItemGlassDetection GlassDetection { get; private set; }
        = null!;
    internal static StructuredExtractionItemGlassEvidence Create(
        Guid parentId,
        StructuredItemGlassEvidenceInput input,
        DateTimeOffset at)
    {
        if (input.Sequence < 1 || input.PageNumber < 1
            || !Enum.IsDefined(input.SourceType)
            || string.IsNullOrWhiteSpace(input.Text)
            || input.Text.Length > 500 || input.Text != input.Text.Trim())
        {
            throw new ArgumentException("Evidence de vidrio invalida.");
        }
        return new() { Id = Guid.NewGuid(), GlassDetectionId = parentId,
            Sequence = input.Sequence, PageNumber = input.PageNumber,
            SourceType = input.SourceType, Text = input.Text,
            CreatedAtUtc = at };
    }
}

public sealed class StructuredExtractionRequirement
{
    private StructuredExtractionRequirement() { }
    public Guid Id { get; private set; }
    public Guid StructuredDocumentExtractionId { get; private set; }
    public int Sequence { get; private set; }
    public RequirementCategory Category { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredDocumentExtraction StructuredDocumentExtraction { get; private set; } = null!;
    internal static StructuredExtractionRequirement Create(Guid p, StructuredRequirementInput x, DateTimeOffset at)
    {
        if (x.Sequence < 1 || !Enum.IsDefined(x.Category) || string.IsNullOrWhiteSpace(x.Value))
            throw new ArgumentException("Requisito estructurado inválido.");
        return new() { Id = Guid.NewGuid(), StructuredDocumentExtractionId = p,
            Sequence = x.Sequence, Category = x.Category, Value = x.Value.Trim(), CreatedAtUtc = at };
    }
}

public sealed class StructuredExtractionDocumentReference
{
    private StructuredExtractionDocumentReference() { }
    public Guid Id { get; private set; }
    public Guid StructuredDocumentExtractionId { get; private set; }
    public int Sequence { get; private set; }
    public string? Reference { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public int? Quantity { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredDocumentExtraction StructuredDocumentExtraction { get; private set; } = null!;
    internal static StructuredExtractionDocumentReference Create(Guid p, StructuredDocumentReferenceInput x, DateTimeOffset at)
    {
        if (x.Sequence < 1 || string.IsNullOrWhiteSpace(x.Description) || x.Quantity is <= 0)
            throw new ArgumentException("Referencia documental inválida.");
        return new() { Id = Guid.NewGuid(), StructuredDocumentExtractionId = p,
            Sequence = x.Sequence, Reference = Trim(x.Reference), Description = x.Description.Trim(),
            Detail = Trim(x.Detail), Quantity = x.Quantity, CreatedAtUtc = at };
    }
    private static string? Trim(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
}

public sealed class StructuredExtractionIssue
{
    private StructuredExtractionIssue() { }
    public Guid Id { get; private set; }
    public Guid StructuredDocumentExtractionId { get; private set; }
    public int Sequence { get; private set; }
    public StructuredIssueCode Code { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public int? ItemSequence { get; private set; }
    public int[] PageNumbers { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredDocumentExtraction StructuredDocumentExtraction { get; private set; } = null!;
    internal static StructuredExtractionIssue Create(Guid p, StructuredIssueInput x, DateTimeOffset at)
    {
        if (x.Sequence < 1 || !Enum.IsDefined(x.Code) || string.IsNullOrWhiteSpace(x.Message)
            || x.ItemSequence is <= 0) throw new ArgumentException("Issue estructurado inválido.");
        return new() { Id = Guid.NewGuid(), StructuredDocumentExtractionId = p, Sequence = x.Sequence,
            Code = x.Code, Message = x.Message.Trim(), ItemSequence = x.ItemSequence,
            PageNumbers = [.. x.PageNumbers], CreatedAtUtc = at };
    }
}

public sealed class StructuredExtractionConflict
{
    private StructuredExtractionConflict() { }
    public Guid Id { get; private set; }
    public Guid StructuredDocumentExtractionId { get; private set; }
    public int Sequence { get; private set; }
    public StructuredConflictCode Code { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public int[] ItemSequences { get; private set; } = [];
    public int[] PageNumbers { get; private set; } = [];
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public StructuredDocumentExtraction StructuredDocumentExtraction { get; private set; } = null!;
    internal static StructuredExtractionConflict Create(Guid p, StructuredConflictInput x, DateTimeOffset at)
    {
        if (x.Sequence < 1 || !Enum.IsDefined(x.Code) || string.IsNullOrWhiteSpace(x.Message))
            throw new ArgumentException("Conflicto estructurado inválido.");
        return new() { Id = Guid.NewGuid(), StructuredDocumentExtractionId = p, Sequence = x.Sequence,
            Code = x.Code, Message = x.Message.Trim(), ItemSequences = [.. x.ItemSequences],
            PageNumbers = [.. x.PageNumbers], CreatedAtUtc = at };
    }
}
