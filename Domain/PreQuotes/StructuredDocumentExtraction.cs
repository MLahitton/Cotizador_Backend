namespace Domain.PreQuotes;

public enum StructuredExtractionStatus { Completed = 1, RequiresReview = 2 }
public enum EvidenceSourceType { Native = 1, Ocr = 2 }
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
    OcrReviewRequired
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
    bool RequiresReview);
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
        DateTimeOffset createdAtUtc)
    {
        if (resultId == Guid.Empty || createdAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Identificador y fecha UTC son obligatorios.");
        if (!Enum.IsDefined(status) || method != "rule_based_v1"
            || durationMs < 0 || itemCount != items.Count
            || referenceCount != references.Count
            || reviewCount != items.Count(x => x.RequiresReview)
            || knownUnits != items.Sum(x => x.Quantity ?? 0))
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
    internal static StructuredExtractionItem Create(Guid parentId, StructuredItemInput x, DateTimeOffset at)
    {
        if (x.Sequence < 1 || string.IsNullOrWhiteSpace(x.Description)
            || !Enum.IsDefined(x.ElementType)
            || (x.WidthMillimeters is null) != (x.HeightMillimeters is null)
            || x.WidthMillimeters is <= 0 || x.HeightMillimeters is <= 0
            || x.Quantity is <= 0) throw new ArgumentException("Item estructurado inválido.");
        return new() { Id = Guid.NewGuid(), StructuredDocumentExtractionId = parentId,
            Sequence = x.Sequence, Reference = Trim(x.Reference),
            Description = x.Description.Trim(), ElementType = x.ElementType,
            RawMeasurements = Trim(x.RawMeasurements), WidthMillimeters = x.WidthMillimeters,
            HeightMillimeters = x.HeightMillimeters, Quantity = x.Quantity,
            RequiresReview = x.RequiresReview, CreatedAtUtc = at };
    }
    private static string? Trim(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
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
