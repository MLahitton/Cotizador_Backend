using Domain.Identity;

namespace Domain.PreQuotes;

public enum RequirementStatus
{
    Pending = 1,
    Processing,
    Processed,
    Failed,
    Cancelled,
    Superseded
}

public enum RequirementExtractionValueStatus
{
    Explicit = 1,
    Inferred = 2,
    Ambiguous = 3,
    Unknown = 4,
    NotApplicable = 5
}

public enum RequirementCommercialLine
{
    Classic = 1,
    Essential = 2,
    Bioconfort = 3,
    Signature = 4
}

public enum RequirementTechnicalProposalStatus
{
    Completed = 1,
    RequiresReview = 2
}

public enum RequirementTechnicalProposalCommercialConfirmationState
{
    PendingConfirmation = 1,
    Confirmed = 2
}

public enum TechnicalProposalItemInclusionState
{
    Included = 1,
    Excluded = 2
}

public sealed class Requirement
{
    private Requirement() { }

    private readonly List<RequirementFile> _files = [];
    private readonly List<RequirementProcessingAttempt> _processingAttempts = [];

    public Guid Id { get; private set; }
    public Guid PreQuoteId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public RequirementStatus Status { get; private set; }
    public RequirementCommercialLine? CommercialLine { get; private set; }
    public Guid? SupersedesRequirementId { get; private set; }
    public Guid? SupersededByRequirementId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool IsActive { get; private set; }
    public PreQuote PreQuote { get; private set; } = null!;
    public User CreatedByUser { get; private set; } = null!;
    public Requirement? SupersedesRequirement { get; private set; }
    public Requirement? SupersededByRequirement { get; private set; }
    public IReadOnlyCollection<RequirementFile> Files => _files;
    public IReadOnlyCollection<RequirementProcessingAttempt> ProcessingAttempts =>
        _processingAttempts;

    public static Requirement Create(
        Guid preQuoteId,
        Guid createdByUserId,
        RequirementCommercialLine commercialLine,
        DateTimeOffset createdAtUtc)
    {
        if (preQuoteId == Guid.Empty)
        {
            throw new ArgumentException(
                "La precotizacion es obligatoria.",
                nameof(preQuoteId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario creador es obligatorio.",
                nameof(createdByUserId));
        }

        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new Requirement
        {
            Id = Guid.NewGuid(),
            PreQuoteId = preQuoteId,
            CreatedByUserId = createdByUserId,
            Status = RequirementStatus.Pending,
            CommercialLine = commercialLine,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            IsActive = true
        };
    }

    public void StartProcessing(DateTimeOffset updatedAtUtc)
    {
        if (!IsCurrent)
        {
            throw new InvalidOperationException(
                "Solo el requerimiento vigente puede iniciar procesamiento.");
        }

        if (Status == RequirementStatus.Processing)
        {
            throw new InvalidOperationException(
                "El requerimiento ya se encuentra en procesamiento.");
        }

        EnsureValidUpdateDate(updatedAtUtc);

        Status = RequirementStatus.Processing;
        UpdatedAtUtc = updatedAtUtc;
    }

    public bool HasProcessingStarted() => _processingAttempts.Count > 0;

    public bool CanEditDocuments => Status == RequirementStatus.Pending
        && IsCurrent
        && !HasProcessingStarted();

    public bool CanCancel => IsCurrent
        && Status == RequirementStatus.Pending
        && !HasProcessingStarted();

    public bool CanReplace => IsCurrent
        && Status is RequirementStatus.Processed or RequirementStatus.Failed;

    public bool IsCurrent => IsActive
        && Status is not RequirementStatus.Cancelled
        && Status is not RequirementStatus.Superseded
        && SupersededByRequirementId is null;

    public void EnsureDocumentsMutable()
    {
        if (!CanEditDocuments)
        {
            throw new InvalidOperationException(
                "Los documentos del requerimiento ya no son editables.");
        }
    }

    public void Cancel(DateTimeOffset updatedAtUtc)
    {
        if (!CanCancel)
        {
            throw new InvalidOperationException(
                "El requerimiento no puede cancelarse.");
        }

        EnsureValidUpdateDate(updatedAtUtc);
        Status = RequirementStatus.Cancelled;
        IsActive = false;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void SupersedeBy(Guid replacementRequirementId, DateTimeOffset updatedAtUtc)
    {
        if (replacementRequirementId == Guid.Empty)
        {
            throw new ArgumentException(
                "El requerimiento reemplazo es obligatorio.",
                nameof(replacementRequirementId));
        }

        if (!CanReplace)
        {
            throw new InvalidOperationException(
                "El requerimiento no puede ser reemplazado.");
        }

        EnsureValidUpdateDate(updatedAtUtc);
        Status = RequirementStatus.Superseded;
        IsActive = false;
        SupersededByRequirementId = replacementRequirementId;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void MarkAsReplacementOf(Guid replacedRequirementId)
    {
        if (replacedRequirementId == Guid.Empty)
        {
            throw new ArgumentException(
                "El requerimiento reemplazado es obligatorio.",
                nameof(replacedRequirementId));
        }

        if (replacedRequirementId == Id)
        {
            throw new ArgumentException(
                "Un requerimiento no puede reemplazarse a si mismo.",
                nameof(replacedRequirementId));
        }

        SupersedesRequirementId = replacedRequirementId;
    }

    public void MarkProcessed(DateTimeOffset updatedAtUtc)
    {
        if (Status != RequirementStatus.Processing)
        {
            throw new InvalidOperationException(
                "El requerimiento no se encuentra en procesamiento.");
        }

        EnsureValidUpdateDate(updatedAtUtc);

        Status = RequirementStatus.Processed;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void MarkFailed(DateTimeOffset updatedAtUtc)
    {
        if (Status != RequirementStatus.Processing)
        {
            throw new InvalidOperationException(
                "El requerimiento no se encuentra en procesamiento.");
        }

        EnsureValidUpdateDate(updatedAtUtc);

        Status = RequirementStatus.Failed;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void MarkProcessingCancelled(DateTimeOffset updatedAtUtc)
    {
        if (Status != RequirementStatus.Processing)
        {
            throw new InvalidOperationException(
                "El requerimiento no se encuentra en procesamiento.");
        }

        EnsureValidUpdateDate(updatedAtUtc);

        Status = RequirementStatus.Pending;
        UpdatedAtUtc = updatedAtUtc;
    }

    private void EnsureValidUpdateDate(DateTimeOffset updatedAtUtc)
    {
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actualizacion no puede ser anterior a la ultima actualizacion.",
                nameof(updatedAtUtc));
        }
    }

    internal static void EnsureUtc(
        DateTimeOffset value,
        string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "La fecha debe expresarse en UTC.",
                parameterName);
        }
    }

    internal static string NormalizeRequired(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "El valor es obligatorio.",
                parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                "El valor supera la longitud maxima.",
                parameterName);
        }

        return normalized;
    }
}

public sealed class RequirementFile
{
    private RequirementFile() { }

    public Guid Id { get; private set; }
    public Guid RequirementId { get; private set; }
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Requirement Requirement { get; private set; } = null!;

    public static RequirementFile Create(
        Guid requirementId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string storageKey,
        DateTimeOffset createdAtUtc)
    {
        if (requirementId == Guid.Empty)
        {
            throw new ArgumentException(
                "El requerimiento es obligatorio.",
                nameof(requirementId));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentException(
                "El tamano del archivo debe ser mayor que cero.",
                nameof(sizeBytes));
        }

        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementFile
        {
            Id = Guid.NewGuid(),
            RequirementId = requirementId,
            OriginalFileName = Requirement.NormalizeRequired(
                originalFileName,
                nameof(originalFileName),
                255),
            ContentType = Requirement.NormalizeRequired(
                contentType,
                nameof(contentType),
                100).ToLowerInvariant(),
            SizeBytes = sizeBytes,
            StorageKey = Requirement.NormalizeRequired(
                storageKey,
                nameof(storageKey),
                500),
            CreatedAtUtc = createdAtUtc
        };
    }
}

public sealed class RequirementProcessingAttempt
{
    private RequirementProcessingAttempt() { }

    public Guid Id { get; private set; }
    public Guid RequirementId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DocumentProcessingState ProcessingState { get; private set; }
    public DocumentProcessingOutcome? Outcome { get; private set; }
    public string? ErrorCode { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public Requirement Requirement { get; private set; } = null!;
    public User RequestedByUser { get; private set; } = null!;
    public RequirementExtractionResult? ExtractionResult { get; private set; }

    public static RequirementProcessingAttempt Create(
        Guid requirementId,
        Guid requestedByUserId,
        Guid correlationId,
        DateTimeOffset createdAtUtc)
    {
        if (requirementId == Guid.Empty)
        {
            throw new ArgumentException(
                "El requerimiento es obligatorio.",
                nameof(requirementId));
        }

        if (requestedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario solicitante es obligatorio.",
                nameof(requestedByUserId));
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException(
                "El identificador de correlacion es obligatorio.",
                nameof(correlationId));
        }

        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementProcessingAttempt
        {
            Id = Guid.NewGuid(),
            RequirementId = requirementId,
            RequestedByUserId = requestedByUserId,
            CorrelationId = correlationId,
            CreatedAtUtc = createdAtUtc,
            ProcessingState = DocumentProcessingState.Pending
        };
    }

    public void Start(DateTimeOffset startedAtUtc)
    {
        if (ProcessingState != DocumentProcessingState.Pending)
        {
            throw new InvalidOperationException(
                "El intento de procesamiento no se encuentra pendiente.");
        }

        Requirement.EnsureUtc(startedAtUtc, nameof(startedAtUtc));
        if (startedAtUtc < CreatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de inicio no puede ser anterior a la fecha de creacion.",
                nameof(startedAtUtc));
        }

        ProcessingState = DocumentProcessingState.Processing;
        StartedAtUtc = startedAtUtc;
    }

    public void Complete(
        DocumentProcessingOutcome outcome,
        DateTimeOffset completedAtUtc)
    {
        if (outcome is not DocumentProcessingOutcome.Completed
            and not DocumentProcessingOutcome.RequiresReview)
        {
            throw new ArgumentException(
                "El resultado de procesamiento no es valido para completar el intento.",
                nameof(outcome));
        }

        EnsureProcessing();
        EnsureValidCompletionDate(completedAtUtc);

        ProcessingState = DocumentProcessingState.Finished;
        Outcome = outcome;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = null;
    }

    public void Fail(
        string errorCode,
        DateTimeOffset completedAtUtc)
    {
        EnsureProcessing();
        EnsureValidCompletionDate(completedAtUtc);

        var normalizedErrorCode = Requirement.NormalizeRequired(
            errorCode,
            nameof(errorCode),
            64);

        ProcessingState = DocumentProcessingState.Finished;
        Outcome = DocumentProcessingOutcome.Failed;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = normalizedErrorCode;
    }

    public void Cancel(DateTimeOffset completedAtUtc)
    {
        EnsureProcessing();
        EnsureValidCompletionDate(completedAtUtc);

        ProcessingState = DocumentProcessingState.Finished;
        Outcome = DocumentProcessingOutcome.Cancelled;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = null;
    }

    private void EnsureProcessing()
    {
        if (ProcessingState != DocumentProcessingState.Processing
            || StartedAtUtc is null)
        {
            throw new InvalidOperationException(
                "El intento de procesamiento no se encuentra en procesamiento.");
        }
    }

    private void EnsureValidCompletionDate(DateTimeOffset completedAtUtc)
    {
        Requirement.EnsureUtc(completedAtUtc, nameof(completedAtUtc));
        if (StartedAtUtc is not { } startedAtUtc
            || completedAtUtc < startedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de finalizacion no puede ser anterior a la fecha de inicio.",
                nameof(completedAtUtc));
        }
    }
}

public sealed class RequirementExtractionResult
{
    private RequirementExtractionResult() { }

    private readonly List<RequirementExtractedItem> _items = [];

    public Guid Id { get; private set; }
    public Guid RequirementProcessingAttemptId { get; private set; }
    public string SchemaVersion { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public int ItemCount { get; private set; }
    public int ItemsRequiringReview { get; private set; }
    public int IssueCount { get; private set; }
    public int ConflictCount { get; private set; }
    public string ProcessingMethod { get; private set; } = string.Empty;
    public int DurationMs { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public RequirementProcessingAttempt ProcessingAttempt { get; private set; } = null!;
    public IReadOnlyCollection<RequirementExtractedItem> Items => _items;

    public static RequirementExtractionResult Create(
        Guid requirementProcessingAttemptId,
        string schemaVersion,
        string provider,
        string payloadJson,
        int itemCount,
        int itemsRequiringReview,
        int issueCount,
        int conflictCount,
        string processingMethod,
        int durationMs,
        DateTimeOffset createdAtUtc)
    {
        if (requirementProcessingAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "El intento de procesamiento es obligatorio.",
                nameof(requirementProcessingAttemptId));
        }

        if (itemCount < 0
            || itemsRequiringReview < 0
            || issueCount < 0
            || conflictCount < 0)
        {
            throw new ArgumentException(
                "Los contadores de extraccion no pueden ser negativos.");
        }

        if (itemsRequiringReview > itemCount)
        {
            throw new ArgumentException(
                "Los items que requieren revision no pueden superar el total de items.",
                nameof(itemsRequiringReview));
        }

        if (durationMs < 0)
        {
            throw new ArgumentException(
                "La duracion de procesamiento no puede ser negativa.",
                nameof(durationMs));
        }

        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementExtractionResult
        {
            Id = Guid.NewGuid(),
            RequirementProcessingAttemptId = requirementProcessingAttemptId,
            SchemaVersion = Requirement.NormalizeRequired(
                schemaVersion,
                nameof(schemaVersion),
                20),
            Provider = Requirement.NormalizeRequired(
                provider,
                nameof(provider),
                30),
            PayloadJson = Requirement.NormalizeRequired(
                payloadJson,
                nameof(payloadJson),
                10_000_000),
            ItemCount = itemCount,
            ItemsRequiringReview = itemsRequiringReview,
            IssueCount = issueCount,
            ConflictCount = conflictCount,
            ProcessingMethod = Requirement.NormalizeRequired(
                processingMethod,
                nameof(processingMethod),
                100),
            DurationMs = durationMs,
            CreatedAtUtc = createdAtUtc
        };
    }
}

public sealed class RequirementExtractedItem
{
    private RequirementExtractedItem() { }

    private readonly List<RequirementExtractedItemEvidence> _evidence = [];
    private readonly List<RequirementExtractedItemSegment> _segments = [];

    public Guid Id { get; private set; }
    public Guid RequirementExtractionResultId { get; private set; }
    public string? Ai2ElementId { get; private set; }
    public int Sequence { get; private set; }
    public string? Reference { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public StructuredElementType ElementType { get; private set; }
    public int? Quantity { get; private set; }
    public int? WidthMillimeters { get; private set; }
    public int? HeightMillimeters { get; private set; }
    public decimal? AreaSquareMeters { get; private set; }
    public decimal? Confidence { get; private set; }
    public RequirementExtractionValueStatus ExtractionStatus { get; private set; }
    public bool RequiresReview { get; private set; }
    public string[] ReviewReasons { get; private set; } = [];
    public string? FunctionalType { get; private set; }
    public string? Operation { get; private set; }
    public int? PanelCount { get; private set; }
    public int? MovablePanelCount { get; private set; }
    public int? FixedPanelCount { get; private set; }
    public string? Arrangement { get; private set; }
    public string? Modulation { get; private set; }
    public string? OpeningDirection { get; private set; }
    public string[] SpecialFeatures { get; private set; } = [];
    public string? GeometryType { get; private set; }
    public string? AssemblyType { get; private set; }
    public string? RequestedSystemRaw { get; private set; }
    public string? RequestedProfileRaw { get; private set; }
    public string? GlassRawSpecification { get; private set; }
    public string? GlassTypeRaw { get; private set; }
    public string? GlassTypeNormalized { get; private set; }
    public decimal? GlassThicknessMm { get; private set; }
    public string? GlassColorRaw { get; private set; }
    public string? GlassColorNormalized { get; private set; }
    public string? GlassTreatmentRaw { get; private set; }
    public string? GlassTreatmentNormalized { get; private set; }
    public string? GlassComposition { get; private set; }
    public string? GlassCoating { get; private set; }
    public string? GlassTransparency { get; private set; }
    public bool? GlassRequiresReview { get; private set; }
    public string? FinishRawDescription { get; private set; }
    public string? FinishNormalizedType { get; private set; }
    public string? FinishColorRaw { get; private set; }
    public string? FinishColorNormalized { get; private set; }
    public string? FinishTextureRaw { get; private set; }
    public string? FinishTextureNormalized { get; private set; }
    public string? FinishExplicitCode { get; private set; }
    public bool? FinishRequiresReview { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public RequirementExtractionResult ExtractionResult { get; private set; } = null!;
    public IReadOnlyCollection<RequirementExtractedItemEvidence> Evidence => _evidence;
    public IReadOnlyCollection<RequirementExtractedItemSegment> Segments => _segments;

    public static RequirementExtractedItem Create(
        Guid requirementExtractionResultId,
        string? ai2ElementId,
        int sequence,
        string? reference,
        string description,
        StructuredElementType elementType,
        int? quantity,
        int? widthMillimeters,
        int? heightMillimeters,
        decimal? areaSquareMeters,
        decimal? confidence,
        RequirementExtractionValueStatus extractionStatus,
        bool requiresReview,
        IEnumerable<string> reviewReasons,
        string? functionalType,
        string? operation,
        int? panelCount,
        int? movablePanelCount,
        int? fixedPanelCount,
        string? arrangement,
        string? modulation,
        string? openingDirection,
        IEnumerable<string>? specialFeatures,
        string? geometryType,
        string? requestedSystemRaw,
        string? requestedProfileRaw,
        string? glassRawSpecification,
        string? glassTypeRaw,
        string? glassTypeNormalized,
        decimal? glassThicknessMm,
        string? glassColorRaw,
        string? glassColorNormalized,
        string? glassTreatmentRaw,
        string? glassTreatmentNormalized,
        string? glassComposition,
        string? glassCoating,
        string? glassTransparency,
        bool? glassRequiresReview,
        string? finishRawDescription,
        string? finishNormalizedType,
        string? finishColorRaw,
        string? finishColorNormalized,
        string? finishTextureRaw,
        string? finishTextureNormalized,
        string? finishExplicitCode,
        bool? finishRequiresReview,
        DateTimeOffset createdAtUtc,
        string? assemblyType = null)
    {
        if (requirementExtractionResultId == Guid.Empty)
        {
            throw new ArgumentException(
                "La extraccion es obligatoria.",
                nameof(requirementExtractionResultId));
        }

        if (sequence <= 0)
        {
            throw new ArgumentException(
                "La secuencia debe ser mayor que cero.",
                nameof(sequence));
        }

        EnsurePositive(quantity, nameof(quantity));
        EnsurePositive(widthMillimeters, nameof(widthMillimeters));
        EnsurePositive(heightMillimeters, nameof(heightMillimeters));
        EnsurePositive(areaSquareMeters, nameof(areaSquareMeters));
        EnsureConfidence(confidence, nameof(confidence));
        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementExtractedItem
        {
            Id = Guid.NewGuid(),
            RequirementExtractionResultId = requirementExtractionResultId,
            Ai2ElementId = NormalizeOptional(ai2ElementId, 100),
            Sequence = sequence,
            Reference = NormalizeOptional(reference, 100),
            Description = Requirement.NormalizeRequired(
                description,
                nameof(description),
                500),
            ElementType = elementType,
            Quantity = quantity,
            WidthMillimeters = widthMillimeters,
            HeightMillimeters = heightMillimeters,
            AreaSquareMeters = areaSquareMeters,
            Confidence = confidence,
            ExtractionStatus = extractionStatus,
            RequiresReview = requiresReview,
            ReviewReasons = NormalizeArray(reviewReasons, 100),
            FunctionalType = NormalizeOptional(functionalType, 100),
            Operation = NormalizeOptional(operation, 100),
            PanelCount = panelCount,
            MovablePanelCount = movablePanelCount,
            FixedPanelCount = fixedPanelCount,
            Arrangement = NormalizeOptional(arrangement, 100),
            Modulation = NormalizeOptional(modulation, 100),
            OpeningDirection = NormalizeOptional(openingDirection, 100),
            SpecialFeatures = NormalizeArray(specialFeatures ?? [], 100),
            GeometryType = NormalizeOptional(geometryType, 100),
            AssemblyType = NormalizeOptional(assemblyType, 100),
            RequestedSystemRaw = NormalizeOptional(requestedSystemRaw, 200),
            RequestedProfileRaw = NormalizeOptional(requestedProfileRaw, 200),
            GlassRawSpecification = NormalizeOptional(glassRawSpecification, 500),
            GlassTypeRaw = NormalizeOptional(glassTypeRaw, 100),
            GlassTypeNormalized = NormalizeOptional(glassTypeNormalized, 100),
            GlassThicknessMm = glassThicknessMm,
            GlassColorRaw = NormalizeOptional(glassColorRaw, 100),
            GlassColorNormalized = NormalizeOptional(glassColorNormalized, 100),
            GlassTreatmentRaw = NormalizeOptional(glassTreatmentRaw, 100),
            GlassTreatmentNormalized = NormalizeOptional(glassTreatmentNormalized, 100),
            GlassComposition = NormalizeOptional(glassComposition, 100),
            GlassCoating = NormalizeOptional(glassCoating, 100),
            GlassTransparency = NormalizeOptional(glassTransparency, 100),
            GlassRequiresReview = glassRequiresReview,
            FinishRawDescription = NormalizeOptional(finishRawDescription, 500),
            FinishNormalizedType = NormalizeOptional(finishNormalizedType, 100),
            FinishColorRaw = NormalizeOptional(finishColorRaw, 100),
            FinishColorNormalized = NormalizeOptional(finishColorNormalized, 100),
            FinishTextureRaw = NormalizeOptional(finishTextureRaw, 100),
            FinishTextureNormalized = NormalizeOptional(finishTextureNormalized, 100),
            FinishExplicitCode = NormalizeOptional(finishExplicitCode, 100),
            FinishRequiresReview = finishRequiresReview,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void AddSegment(RequirementExtractedItemSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.RequirementExtractedItemId != Id)
        {
            throw new ArgumentException(
                "El segmento no pertenece al item extraido.",
                nameof(segment));
        }

        _segments.Add(segment);
    }

    public void AddEvidence(RequirementExtractedItemEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.RequirementExtractedItemId != Id)
        {
            throw new ArgumentException(
                "La evidencia no pertenece al item extraido.",
                nameof(evidence));
        }

        _evidence.Add(evidence);
    }

    internal static void EnsurePositive<T>(T? value, string parameterName)
        where T : struct, IComparable<T>
    {
        if (value.HasValue && value.Value.CompareTo(default) <= 0)
        {
            throw new ArgumentException(
                "El valor debe ser mayor que cero.",
                parameterName);
        }
    }

    internal static void EnsureConfidence(decimal? value, string parameterName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentException(
                "La confianza debe estar entre cero y uno.",
                parameterName);
        }
    }

    internal static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                "El valor supera la longitud maxima.");
        }

        return normalized;
    }

    internal static string[] NormalizeArray(
        IEnumerable<string> values,
        int maximumLength)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => NormalizeOptional(value, maximumLength)!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}


public sealed class RequirementExtractedItemSegment
{
    private RequirementExtractedItemSegment() { }

    public Guid Id { get; private set; }
    public Guid RequirementExtractedItemId { get; private set; }
    public int Sequence { get; private set; }
    public string? Role { get; private set; }
    public int? WidthMillimeters { get; private set; }
    public int? HeightMillimeters { get; private set; }
    public int? Quantity { get; private set; }
    public string? Operation { get; private set; }
    public string? GeometryType { get; private set; }
    public string? EvidenceText { get; private set; }
    public string? SourceId { get; private set; }
    public EvidenceSourceType? SourceType { get; private set; }
    public int? PageNumber { get; private set; }
    public string? SheetName { get; private set; }
    public string? CellRange { get; private set; }
    public decimal? Confidence { get; private set; }
    public RequirementExtractionValueStatus ExtractionStatus { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public RequirementExtractedItem Item { get; private set; } = null!;

    public static RequirementExtractedItemSegment Create(
        Guid requirementExtractedItemId,
        int sequence,
        string? role,
        int? widthMillimeters,
        int? heightMillimeters,
        int? quantity,
        string? operation,
        string? geometryType,
        string? evidenceText,
        string? sourceId,
        EvidenceSourceType? sourceType,
        int? pageNumber,
        string? sheetName,
        string? cellRange,
        decimal? confidence,
        RequirementExtractionValueStatus extractionStatus,
        DateTimeOffset createdAtUtc)
    {
        if (requirementExtractedItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "El item extraido es obligatorio.",
                nameof(requirementExtractedItemId));
        }

        if (sequence <= 0)
        {
            throw new ArgumentException(
                "La secuencia debe ser mayor que cero.",
                nameof(sequence));
        }

        RequirementExtractedItem.EnsurePositive(widthMillimeters, nameof(widthMillimeters));
        RequirementExtractedItem.EnsurePositive(heightMillimeters, nameof(heightMillimeters));
        RequirementExtractedItem.EnsurePositive(quantity, nameof(quantity));
        RequirementExtractedItem.EnsureConfidence(confidence, nameof(confidence));
        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementExtractedItemSegment
        {
            Id = Guid.NewGuid(),
            RequirementExtractedItemId = requirementExtractedItemId,
            Sequence = sequence,
            Role = RequirementExtractedItem.NormalizeOptional(role, 100),
            WidthMillimeters = widthMillimeters,
            HeightMillimeters = heightMillimeters,
            Quantity = quantity,
            Operation = RequirementExtractedItem.NormalizeOptional(operation, 100),
            GeometryType = RequirementExtractedItem.NormalizeOptional(geometryType, 100),
            EvidenceText = RequirementExtractedItem.NormalizeOptional(evidenceText, 500),
            SourceId = RequirementExtractedItem.NormalizeOptional(sourceId, 100),
            SourceType = sourceType,
            PageNumber = pageNumber,
            SheetName = RequirementExtractedItem.NormalizeOptional(sheetName, 100),
            CellRange = RequirementExtractedItem.NormalizeOptional(cellRange, 50),
            Confidence = confidence,
            ExtractionStatus = extractionStatus,
            CreatedAtUtc = createdAtUtc
        };
    }
}
public sealed class RequirementExtractedItemEvidence
{
    private RequirementExtractedItemEvidence() { }

    public Guid Id { get; private set; }
    public Guid RequirementExtractedItemId { get; private set; }
    public int? PageNumber { get; private set; }
    public EvidenceSourceType SourceType { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public string? SheetName { get; private set; }
    public string? CellRange { get; private set; }
    public string? SourceId { get; private set; }
    public decimal? Confidence { get; private set; }
    public RequirementExtractionValueStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public RequirementExtractedItem Item { get; private set; } = null!;

    public static RequirementExtractedItemEvidence Create(
        Guid requirementExtractedItemId,
        int? pageNumber,
        EvidenceSourceType sourceType,
        string text,
        string? sheetName,
        string? cellRange,
        string? sourceId,
        decimal? confidence,
        RequirementExtractionValueStatus status,
        DateTimeOffset createdAtUtc)
    {
        if (requirementExtractedItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "El item extraido es obligatorio.",
                nameof(requirementExtractedItemId));
        }

        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        if (confidence is < 0m or > 1m)
        {
            throw new ArgumentException(
                "La confianza debe estar entre cero y uno.",
                nameof(confidence));
        }

        ValidateLocation(sourceType, pageNumber, sheetName, cellRange);

        return new RequirementExtractedItemEvidence
        {
            Id = Guid.NewGuid(),
            RequirementExtractedItemId = requirementExtractedItemId,
            PageNumber = pageNumber,
            SourceType = sourceType,
            Text = Requirement.NormalizeRequired(text, nameof(text), 500),
            SheetName = RequirementExtractedItem.NormalizeOptional(sheetName, 100),
            CellRange = RequirementExtractedItem.NormalizeOptional(cellRange, 50),
            SourceId = RequirementExtractedItem.NormalizeOptional(sourceId, 100),
            Confidence = confidence,
            Status = status,
            CreatedAtUtc = createdAtUtc
        };
    }

    private static void ValidateLocation(
        EvidenceSourceType sourceType,
        int? pageNumber,
        string? sheetName,
        string? cellRange)
    {
        var normalizedSheetName = RequirementExtractedItem.NormalizeOptional(
            sheetName,
            100);
        var normalizedCellRange = RequirementExtractedItem.NormalizeOptional(
            cellRange,
            50);

        switch (sourceType)
        {
            case EvidenceSourceType.Native:
            case EvidenceSourceType.Ocr:
                if (pageNumber is null or <= 0
                    || normalizedSheetName is not null
                    || normalizedCellRange is not null)
                {
                    throw new ArgumentException(
                        "La evidencia PDF debe tener pagina positiva y no debe tener hoja/celda.");
                }
                break;

            case EvidenceSourceType.Xlsx:
                if (pageNumber is not null
                    || normalizedSheetName is null
                    || normalizedCellRange is null)
                {
                    throw new ArgumentException(
                        "La evidencia XLSX debe tener hoja y rango de celda, sin pagina.");
                }
                break;

            default:
                throw new ArgumentException(
                    "El tipo de evidencia no es soportado.",
                    nameof(sourceType));
        }
    }
}

public sealed class RequirementTechnicalProposal
{
    private RequirementTechnicalProposal() { }

    private readonly List<RequirementTechnicalProposalItem> _items = [];

    public Guid Id { get; private set; }
    public Guid RequirementId { get; private set; }
    public Guid RequirementExtractionResultId { get; private set; }
    public Guid RequirementProcessingAttemptId { get; private set; }
    public RequirementTechnicalProposalStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public long CommercialRevision { get; private set; }
    public DateTimeOffset? CommercialConfirmedAtUtc { get; private set; }
    public Guid? CommercialConfirmedByUserId { get; private set; }
    public Requirement Requirement { get; private set; } = null!;
    public RequirementExtractionResult ExtractionResult { get; private set; } = null!;
    public RequirementProcessingAttempt ProcessingAttempt { get; private set; } = null!;
    public IReadOnlyCollection<RequirementTechnicalProposalItem> Items => _items;
    public IReadOnlyCollection<RequirementTechnicalProposalItem> IncludedItems =>
        _items.Where(item => item.IsIncluded).ToArray();
    public RequirementTechnicalProposalCommercialConfirmationState
        CommercialConfirmationState =>
            CommercialConfirmedAtUtc is null || CommercialConfirmedByUserId is null
                ? RequirementTechnicalProposalCommercialConfirmationState
                    .PendingConfirmation
                : RequirementTechnicalProposalCommercialConfirmationState.Confirmed;

    public bool IsCommerciallyConfirmed =>
        CommercialConfirmationState
            == RequirementTechnicalProposalCommercialConfirmationState.Confirmed;

    public static RequirementTechnicalProposal Create(
        Guid requirementId,
        Guid requirementExtractionResultId,
        Guid requirementProcessingAttemptId,
        bool requiresReview,
        DateTimeOffset createdAtUtc)
    {
        if (requirementId == Guid.Empty)
        {
            throw new ArgumentException(
                "El requerimiento es obligatorio.",
                nameof(requirementId));
        }

        if (requirementExtractionResultId == Guid.Empty)
        {
            throw new ArgumentException(
                "La extraccion es obligatoria.",
                nameof(requirementExtractionResultId));
        }

        if (requirementProcessingAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "El intento de procesamiento es obligatorio.",
                nameof(requirementProcessingAttemptId));
        }

        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementTechnicalProposal
        {
            Id = Guid.NewGuid(),
            RequirementId = requirementId,
            RequirementExtractionResultId = requirementExtractionResultId,
            RequirementProcessingAttemptId = requirementProcessingAttemptId,
            Status = requiresReview
                ? RequirementTechnicalProposalStatus.RequiresReview
                : RequirementTechnicalProposalStatus.Completed,
            CreatedAtUtc = createdAtUtc,
            CommercialRevision = 1
        };
    }

    public void AddItem(RequirementTechnicalProposalItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.TechnicalProposalId != Id)
        {
            throw new ArgumentException(
                "El item no pertenece a la propuesta.",
                nameof(item));
        }

        _items.Add(item);
        if (item.RequiresReview)
        {
            Status = RequirementTechnicalProposalStatus.RequiresReview;
        }
    }

    public void ConfirmCommercialSelection(
        Guid confirmedByUserId,
        DateTimeOffset confirmedAtUtc)
    {
        if (confirmedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario que confirma es obligatorio.",
                nameof(confirmedByUserId));
        }

        Requirement.EnsureUtc(confirmedAtUtc, nameof(confirmedAtUtc));

        var includedItems = IncludedItems;
        if (includedItems.Count == 0)
        {
            throw new InvalidOperationException(
                "La propuesta tecnica no tiene items incluidos para confirmar.");
        }

        if (includedItems.Any(item => !item.HasCompleteCommercialConfiguration()))
        {
            throw new InvalidOperationException(
                "La propuesta tecnica tiene items sin configuracion completa.");
        }

        foreach (var item in includedItems.Where(item => !item.HasSelectedConfiguration()))
        {
            item.Select(
                item.SuggestedSystemId,
                item.SuggestedGlassTypeId,
                item.SuggestedFinishTypeId,
                confirmedByUserId,
                confirmedAtUtc);
        }

        CommercialConfirmedAtUtc = confirmedAtUtc;
        CommercialConfirmedByUserId = confirmedByUserId;
    }

    public void InvalidateCommercialConfirmation()
    {
        CommercialConfirmedAtUtc = null;
        CommercialConfirmedByUserId = null;
    }

    public void MarkCommerciallyChanged()
    {
        checked
        {
            CommercialRevision++;
        }
    }
}

public sealed class RequirementTechnicalProposalItem
{
    private RequirementTechnicalProposalItem() { }

    private readonly List<RequirementTechnicalProposalSystemAlternative>
        _systemAlternatives = [];
    private readonly List<RequirementTechnicalProposalGlassAlternative>
        _glassAlternatives = [];
    private readonly List<RequirementTechnicalProposalFinishAlternative>
        _finishAlternatives = [];
    private readonly List<RequirementTechnicalProposalHistoricalExample>
        _historicalExamples = [];

    public Guid Id { get; private set; }
    public Guid TechnicalProposalId { get; private set; }
    public Guid RequirementExtractedItemId { get; private set; }
    public Guid? SuggestedSystemId { get; private set; }
    public Guid? SuggestedGlassTypeId { get; private set; }
    public Guid? SuggestedFinishTypeId { get; private set; }
    public Guid? SelectedSystemId { get; private set; }
    public Guid? SelectedGlassTypeId { get; private set; }
    public Guid? SelectedFinishTypeId { get; private set; }
    public DateTimeOffset? SelectedAtUtc { get; private set; }
    public Guid? SelectedByUserId { get; private set; }
    public int? ManualQuantityOverride { get; private set; }
    public int? ManualWidthMillimetersOverride { get; private set; }
    public int? ManualHeightMillimetersOverride { get; private set; }
    public TechnicalProposalItemInclusionState InclusionState { get; private set; }
    public DateTimeOffset? ExcludedAtUtc { get; private set; }
    public Guid? ExcludedByUserId { get; private set; }
    public string? ExclusionReason { get; private set; }
    public decimal OverallConfidence { get; private set; }
    public decimal SystemConfidence { get; private set; }
    public decimal GlassConfidence { get; private set; }
    public decimal FinishConfidence { get; private set; }
    public bool RequiresReview { get; private set; }
    public bool IsTechnicallyComplete { get; private set; }
    public bool IsPriceable { get; private set; }
    public string[] ReviewReasons { get; private set; } = [];
    public string[] SystemResolutionReasons { get; private set; } = [];
    public string[] GlassResolutionReasons { get; private set; } = [];
    public string[] FinishResolutionReasons { get; private set; } = [];
    public int HistoricalSupportCount { get; private set; }
    public decimal? HistoricalBestSimilarity { get; private set; }
    public decimal? HistoricalAverageSimilarity { get; private set; }
    public string HistoricalSimilarityStatus { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public RequirementTechnicalProposal TechnicalProposal { get; private set; } = null!;
    public RequirementExtractedItem ExtractedItem { get; private set; } = null!;
    public IReadOnlyCollection<RequirementTechnicalProposalSystemAlternative>
        SystemAlternatives => _systemAlternatives;
    public IReadOnlyCollection<RequirementTechnicalProposalGlassAlternative>
        GlassAlternatives => _glassAlternatives;
    public IReadOnlyCollection<RequirementTechnicalProposalFinishAlternative>
        FinishAlternatives => _finishAlternatives;
    public IReadOnlyCollection<RequirementTechnicalProposalHistoricalExample>
        HistoricalExamples => _historicalExamples;
    public bool IsIncluded =>
        InclusionState == TechnicalProposalItemInclusionState.Included;
    public int? EffectiveQuantity =>
        ManualQuantityOverride ?? ExtractedItem?.Quantity;
    public int? EffectiveWidthMillimeters =>
        ManualWidthMillimetersOverride ?? ExtractedItem?.WidthMillimeters;
    public int? EffectiveHeightMillimeters =>
        ManualHeightMillimetersOverride ?? ExtractedItem?.HeightMillimeters;

    public static RequirementTechnicalProposalItem Create(
        Guid technicalProposalId,
        Guid requirementExtractedItemId,
        Guid? suggestedSystemId,
        Guid? suggestedGlassTypeId,
        Guid? suggestedFinishTypeId,
        decimal overallConfidence,
        decimal systemConfidence,
        decimal glassConfidence,
        decimal finishConfidence,
        bool requiresReview,
        bool isTechnicallyComplete,
        bool isPriceable,
        IEnumerable<string> reviewReasons,
        IEnumerable<string> systemResolutionReasons,
        IEnumerable<string> glassResolutionReasons,
        IEnumerable<string> finishResolutionReasons,
        int historicalSupportCount,
        decimal? historicalBestSimilarity,
        decimal? historicalAverageSimilarity,
        string historicalSimilarityStatus,
        DateTimeOffset createdAtUtc)
    {
        if (technicalProposalId == Guid.Empty)
        {
            throw new ArgumentException(
                "La propuesta tecnica es obligatoria.",
                nameof(technicalProposalId));
        }

        if (requirementExtractedItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "El item extraido es obligatorio.",
                nameof(requirementExtractedItemId));
        }

        EnsureConfidence(overallConfidence, nameof(overallConfidence));
        EnsureConfidence(systemConfidence, nameof(systemConfidence));
        EnsureConfidence(glassConfidence, nameof(glassConfidence));
        EnsureConfidence(finishConfidence, nameof(finishConfidence));
        if (historicalSupportCount < 0)
        {
            throw new ArgumentException(
                "El soporte historico no puede ser negativo.",
                nameof(historicalSupportCount));
        }

        EnsureConfidence(historicalBestSimilarity, nameof(historicalBestSimilarity));
        EnsureConfidence(historicalAverageSimilarity, nameof(historicalAverageSimilarity));
        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementTechnicalProposalItem
        {
            Id = Guid.NewGuid(),
            TechnicalProposalId = technicalProposalId,
            RequirementExtractedItemId = requirementExtractedItemId,
            SuggestedSystemId = EmptyToNull(suggestedSystemId),
            SuggestedGlassTypeId = EmptyToNull(suggestedGlassTypeId),
            SuggestedFinishTypeId = EmptyToNull(suggestedFinishTypeId),
            OverallConfidence = overallConfidence,
            SystemConfidence = systemConfidence,
            GlassConfidence = glassConfidence,
            FinishConfidence = finishConfidence,
            RequiresReview = requiresReview,
            IsTechnicallyComplete = isTechnicallyComplete,
            IsPriceable = isPriceable,
            ReviewReasons = RequirementExtractedItem.NormalizeArray(reviewReasons, 100),
            SystemResolutionReasons = RequirementExtractedItem.NormalizeArray(
                systemResolutionReasons,
                100),
            GlassResolutionReasons = RequirementExtractedItem.NormalizeArray(
                glassResolutionReasons,
                100),
            FinishResolutionReasons = RequirementExtractedItem.NormalizeArray(
                finishResolutionReasons,
                100),
            HistoricalSupportCount = historicalSupportCount,
            HistoricalBestSimilarity = historicalBestSimilarity,
            HistoricalAverageSimilarity = historicalAverageSimilarity,
            HistoricalSimilarityStatus = Requirement.NormalizeRequired(
                historicalSimilarityStatus,
                nameof(historicalSimilarityStatus),
                50),
            InclusionState = TechnicalProposalItemInclusionState.Included,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void AddSystemAlternative(
        RequirementTechnicalProposalSystemAlternative alternative) =>
        AddAlternative(alternative, _systemAlternatives);

    public void AddGlassAlternative(
        RequirementTechnicalProposalGlassAlternative alternative) =>
        AddAlternative(alternative, _glassAlternatives);

    public void AddFinishAlternative(
        RequirementTechnicalProposalFinishAlternative alternative) =>
        AddAlternative(alternative, _finishAlternatives);

    public void AddHistoricalExample(
        RequirementTechnicalProposalHistoricalExample example) =>
        AddAlternative(example, _historicalExamples);

    public void Select(
        Guid? selectedSystemId,
        Guid? selectedGlassTypeId,
        Guid? selectedFinishTypeId,
        Guid selectedByUserId,
        DateTimeOffset selectedAtUtc)
    {
        if (selectedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario que selecciona es obligatorio.",
                nameof(selectedByUserId));
        }

        Requirement.EnsureUtc(selectedAtUtc, nameof(selectedAtUtc));

        SelectedSystemId = EmptyToNull(selectedSystemId);
        SelectedGlassTypeId = EmptyToNull(selectedGlassTypeId);
        SelectedFinishTypeId = EmptyToNull(selectedFinishTypeId);
        SelectedByUserId = selectedByUserId;
        SelectedAtUtc = selectedAtUtc;
    }

    public void ApplyManualDataOverride(
        int? quantity,
        int? widthMillimeters,
        int? heightMillimeters)
    {
        RequirementExtractedItem.EnsurePositive(quantity, nameof(quantity));
        RequirementExtractedItem.EnsurePositive(
            widthMillimeters,
            nameof(widthMillimeters));
        RequirementExtractedItem.EnsurePositive(
            heightMillimeters,
            nameof(heightMillimeters));

        ManualQuantityOverride = quantity ?? ManualQuantityOverride;
        ManualWidthMillimetersOverride =
            widthMillimeters ?? ManualWidthMillimetersOverride;
        ManualHeightMillimetersOverride =
            heightMillimeters ?? ManualHeightMillimetersOverride;
    }

    public bool Exclude(
        Guid excludedByUserId,
        DateTimeOffset excludedAtUtc,
        string? reason)
    {
        if (excludedByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario que excluye es obligatorio.",
                nameof(excludedByUserId));
        }

        Requirement.EnsureUtc(excludedAtUtc, nameof(excludedAtUtc));
        if (InclusionState == TechnicalProposalItemInclusionState.Excluded)
        {
            return false;
        }

        InclusionState = TechnicalProposalItemInclusionState.Excluded;
        ExcludedAtUtc = excludedAtUtc;
        ExcludedByUserId = excludedByUserId;
        ExclusionReason = RequirementExtractedItem.NormalizeOptional(reason, 500);
        return true;
    }

    public bool Reactivate()
    {
        if (InclusionState == TechnicalProposalItemInclusionState.Included)
        {
            return false;
        }

        InclusionState = TechnicalProposalItemInclusionState.Included;
        ExcludedAtUtc = null;
        ExcludedByUserId = null;
        ExclusionReason = null;
        return true;
    }

    public bool HasSelectedConfiguration() =>
        SelectedAtUtc is not null
        && SelectedByUserId is not null;

    public bool HasCompleteCommercialConfiguration()
    {
        if (HasSelectedConfiguration())
        {
            return SelectedSystemId is not null
                && SelectedGlassTypeId is not null
                && SelectedFinishTypeId is not null;
        }

        return SuggestedSystemId is not null
            && SuggestedGlassTypeId is not null
            && SuggestedFinishTypeId is not null;
    }

    private void AddAlternative<T>(T value, List<T> target)
        where T : IRequirementTechnicalProposalChild
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.ProposalItemId != Id)
        {
            throw new ArgumentException(
                "El detalle no pertenece al item de propuesta.",
                nameof(value));
        }

        target.Add(value);
    }

    private static Guid? EmptyToNull(Guid? value) =>
        value == Guid.Empty ? null : value;

    internal static void EnsureConfidence(decimal value, string parameterName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentException(
                "La confianza debe estar entre cero y uno.",
                parameterName);
        }
    }

    internal static void EnsureConfidence(decimal? value, string parameterName)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentException(
                "La confianza debe estar entre cero y uno.",
                parameterName);
        }
    }
}

internal interface IRequirementTechnicalProposalChild
{
    Guid ProposalItemId { get; }
}

public sealed class RequirementTechnicalProposalSystemAlternative
    : IRequirementTechnicalProposalChild
{
    private RequirementTechnicalProposalSystemAlternative() { }

    public Guid Id { get; private set; }
    public Guid ProposalItemId { get; private set; }
    public Guid ProductSystemId { get; private set; }
    public int Rank { get; private set; }
    public decimal Confidence { get; private set; }
    public string[] Reasons { get; private set; } = [];
    public RequirementTechnicalProposalItem ProposalItem { get; private set; } = null!;

    public static RequirementTechnicalProposalSystemAlternative Create(
        Guid proposalItemId,
        Guid productSystemId,
        int rank,
        decimal confidence,
        IEnumerable<string> reasons) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProposalItemId = ValidateId(proposalItemId, nameof(proposalItemId)),
            ProductSystemId = ValidateId(productSystemId, nameof(productSystemId)),
            Rank = ValidateRank(rank),
            Confidence = ValidateConfidence(confidence),
            Reasons = RequirementExtractedItem.NormalizeArray(reasons, 100)
        };

    private static Guid ValidateId(Guid value, string parameterName) =>
        value == Guid.Empty
            ? throw new ArgumentException("El identificador es obligatorio.", parameterName)
            : value;

    private static int ValidateRank(int value) =>
        value <= 0
            ? throw new ArgumentException("El rank debe ser mayor que cero.", nameof(value))
            : value;

    private static decimal ValidateConfidence(decimal value) =>
        value is < 0m or > 1m
            ? throw new ArgumentException("La confianza debe estar entre cero y uno.", nameof(value))
            : value;
}

public sealed class RequirementTechnicalProposalGlassAlternative
    : IRequirementTechnicalProposalChild
{
    private RequirementTechnicalProposalGlassAlternative() { }

    public Guid Id { get; private set; }
    public Guid ProposalItemId { get; private set; }
    public Guid GlassTypeId { get; private set; }
    public int Rank { get; private set; }
    public decimal Confidence { get; private set; }
    public string[] Reasons { get; private set; } = [];
    public RequirementTechnicalProposalItem ProposalItem { get; private set; } = null!;

    public static RequirementTechnicalProposalGlassAlternative Create(
        Guid proposalItemId,
        Guid glassTypeId,
        int rank,
        decimal confidence,
        IEnumerable<string> reasons) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProposalItemId = RequirementTechnicalProposalSystemAlternative.Create(
                proposalItemId,
                glassTypeId,
                rank,
                confidence,
                reasons).ProposalItemId,
            GlassTypeId = glassTypeId,
            Rank = rank,
            Confidence = confidence,
            Reasons = RequirementExtractedItem.NormalizeArray(reasons, 100)
        };
}

public sealed class RequirementTechnicalProposalFinishAlternative
    : IRequirementTechnicalProposalChild
{
    private RequirementTechnicalProposalFinishAlternative() { }

    public Guid Id { get; private set; }
    public Guid ProposalItemId { get; private set; }
    public Guid FinishTypeId { get; private set; }
    public int Rank { get; private set; }
    public decimal Confidence { get; private set; }
    public string[] Reasons { get; private set; } = [];
    public RequirementTechnicalProposalItem ProposalItem { get; private set; } = null!;

    public static RequirementTechnicalProposalFinishAlternative Create(
        Guid proposalItemId,
        Guid finishTypeId,
        int rank,
        decimal confidence,
        IEnumerable<string> reasons) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProposalItemId = RequirementTechnicalProposalSystemAlternative.Create(
                proposalItemId,
                finishTypeId,
                rank,
                confidence,
                reasons).ProposalItemId,
            FinishTypeId = finishTypeId,
            Rank = rank,
            Confidence = confidence,
            Reasons = RequirementExtractedItem.NormalizeArray(reasons, 100)
        };
}

public sealed class RequirementTechnicalProposalHistoricalExample
    : IRequirementTechnicalProposalChild
{
    private RequirementTechnicalProposalHistoricalExample() { }

    public Guid Id { get; private set; }
    public Guid ProposalItemId { get; private set; }
    public string CandidateId { get; private set; } = string.Empty;
    public string QuoteId { get; private set; } = string.Empty;
    public string? HistoricalReference { get; private set; }
    public decimal SimilarityScore { get; private set; }
    public string[] MatchedFeatures { get; private set; } = [];
    public string[] Differences { get; private set; } = [];
    public string TechnicalExplanation { get; private set; } = string.Empty;
    public RequirementTechnicalProposalItem ProposalItem { get; private set; } = null!;

    public static RequirementTechnicalProposalHistoricalExample Create(
        Guid proposalItemId,
        string candidateId,
        string quoteId,
        string? historicalReference,
        decimal similarityScore,
        IEnumerable<string> matchedFeatures,
        IEnumerable<string> differences,
        string technicalExplanation) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProposalItemId = proposalItemId == Guid.Empty
                ? throw new ArgumentException(
                    "El item de propuesta es obligatorio.",
                    nameof(proposalItemId))
                : proposalItemId,
            CandidateId = Requirement.NormalizeRequired(candidateId, nameof(candidateId), 200),
            QuoteId = Requirement.NormalizeRequired(quoteId, nameof(quoteId), 200),
            HistoricalReference = RequirementExtractedItem.NormalizeOptional(
                historicalReference,
                100),
            SimilarityScore = similarityScore is < 0m or > 1m
                ? throw new ArgumentException(
                    "La similitud debe estar entre cero y uno.",
                    nameof(similarityScore))
                : similarityScore,
            MatchedFeatures = RequirementExtractedItem.NormalizeArray(matchedFeatures, 100),
            Differences = RequirementExtractedItem.NormalizeArray(differences, 200),
            TechnicalExplanation = Requirement.NormalizeRequired(
                technicalExplanation,
                nameof(technicalExplanation),
                1000)
        };
}

public sealed class RequirementPricingSnapshot
{
    private RequirementPricingSnapshot() { }

    private readonly List<RequirementPricingItemSnapshot> _items = [];

    public Guid Id { get; private set; }
    public Guid RequirementId { get; private set; }
    public Guid TechnicalProposalId { get; private set; }
    public long TechnicalProposalCommercialRevision { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string PricingBasis { get; private set; } = string.Empty;
    public decimal? OriginalGrandTotal { get; private set; }
    public decimal? CurrentGrandTotal { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Requirement Requirement { get; private set; } = null!;
    public RequirementTechnicalProposal TechnicalProposal { get; private set; } = null!;
    public IReadOnlyCollection<RequirementPricingItemSnapshot> Items => _items;
    public decimal? DeltaGrandTotal =>
        OriginalGrandTotal is null || CurrentGrandTotal is null
            ? null
            : CurrentGrandTotal - OriginalGrandTotal;

    public static RequirementPricingSnapshot Create(
        Guid requirementId,
        Guid technicalProposalId,
        long technicalProposalCommercialRevision,
        string currency,
        string pricingBasis,
        decimal? originalGrandTotal,
        decimal? currentGrandTotal,
        DateTimeOffset createdAtUtc)
    {
        if (requirementId == Guid.Empty)
        {
            throw new ArgumentException(
                "El requerimiento es obligatorio.",
                nameof(requirementId));
        }

        if (technicalProposalId == Guid.Empty)
        {
            throw new ArgumentException(
                "La propuesta tecnica es obligatoria.",
                nameof(technicalProposalId));
        }

        EnsureCommercialRevision(technicalProposalCommercialRevision);
        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementPricingSnapshot
        {
            Id = Guid.NewGuid(),
            RequirementId = requirementId,
            TechnicalProposalId = technicalProposalId,
            TechnicalProposalCommercialRevision = technicalProposalCommercialRevision,
            Currency = Requirement.NormalizeRequired(currency, nameof(currency), 10),
            PricingBasis = Requirement.NormalizeRequired(
                pricingBasis,
                nameof(pricingBasis),
                80),
            OriginalGrandTotal = EnsureMoney(originalGrandTotal, nameof(originalGrandTotal)),
            CurrentGrandTotal = EnsureMoney(currentGrandTotal, nameof(currentGrandTotal)),
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
    }

    public void Reinitialize(
        Guid technicalProposalId,
        long technicalProposalCommercialRevision,
        string currency,
        string pricingBasis,
        decimal? originalGrandTotal,
        decimal? currentGrandTotal,
        DateTimeOffset updatedAtUtc)
    {
        if (technicalProposalId == Guid.Empty)
        {
            throw new ArgumentException(
                "La propuesta tecnica es obligatoria.",
                nameof(technicalProposalId));
        }

        EnsureCommercialRevision(technicalProposalCommercialRevision);
        Requirement.EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actualizacion no puede ser anterior a la ultima actualizacion.",
                nameof(updatedAtUtc));
        }

        TechnicalProposalId = technicalProposalId;
        TechnicalProposalCommercialRevision = technicalProposalCommercialRevision;
        Currency = Requirement.NormalizeRequired(currency, nameof(currency), 10);
        PricingBasis = Requirement.NormalizeRequired(
            pricingBasis,
            nameof(pricingBasis),
            80);
        OriginalGrandTotal = EnsureMoney(originalGrandTotal, nameof(originalGrandTotal));
        CurrentGrandTotal = EnsureMoney(currentGrandTotal, nameof(currentGrandTotal));
        UpdatedAtUtc = updatedAtUtc;
        _items.Clear();
    }

    public void AddItem(RequirementPricingItemSnapshot item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.PricingSnapshotId != Id)
        {
            throw new ArgumentException(
                "El item no pertenece al snapshot de pricing.",
                nameof(item));
        }

        _items.Add(item);
    }

    public void UpdateCurrentGrandTotal(
        decimal? currentGrandTotal,
        DateTimeOffset updatedAtUtc)
    {
        Requirement.EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actualizacion no puede ser anterior a la ultima actualizacion.",
                nameof(updatedAtUtc));
        }

        CurrentGrandTotal = EnsureMoney(currentGrandTotal, nameof(currentGrandTotal));
        UpdatedAtUtc = updatedAtUtc;
    }

    public void RecalculateCurrentGrandTotal(DateTimeOffset updatedAtUtc)
    {
        var currentValues = _items
            .Select(item => item.CurrentLineExpected)
            .ToArray();
        decimal? currentGrandTotal = currentValues.Any(value => value is null)
            ? null
            : currentValues.Sum(value => value!.Value);

        UpdateCurrentGrandTotal(currentGrandTotal, updatedAtUtc);
    }

    public void MarkForCommercialRevision(
        long technicalProposalCommercialRevision,
        DateTimeOffset updatedAtUtc)
    {
        EnsureCommercialRevision(technicalProposalCommercialRevision);
        Requirement.EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actualizacion no puede ser anterior a la ultima actualizacion.",
                nameof(updatedAtUtc));
        }

        TechnicalProposalCommercialRevision = technicalProposalCommercialRevision;
        UpdatedAtUtc = updatedAtUtc;
    }

    internal static decimal? EnsureMoney(decimal? value, string parameterName)
    {
        if (value is < 0m)
        {
            throw new ArgumentException(
                "El valor monetario no puede ser negativo.",
                parameterName);
        }

        return value;
    }

    private static void EnsureCommercialRevision(long value)
    {
        if (value < 1)
        {
            throw new ArgumentException(
                "La revision comercial debe ser mayor o igual a uno.",
                nameof(value));
        }
    }
}

public sealed class RequirementPricingItemSnapshot
{
    private RequirementPricingItemSnapshot() { }

    public Guid Id { get; private set; }
    public Guid PricingSnapshotId { get; private set; }
    public Guid TechnicalProposalItemId { get; private set; }
    public Guid? OriginalSystemId { get; private set; }
    public Guid? OriginalGlassTypeId { get; private set; }
    public Guid? OriginalFinishTypeId { get; private set; }
    public Guid? CurrentSystemId { get; private set; }
    public Guid? CurrentGlassTypeId { get; private set; }
    public Guid? CurrentFinishTypeId { get; private set; }
    public string OriginalStatus { get; private set; } = string.Empty;
    public string CurrentStatus { get; private set; } = string.Empty;
    public decimal? OriginalUnitMinimum { get; private set; }
    public decimal? OriginalUnitExpected { get; private set; }
    public decimal? OriginalUnitMaximum { get; private set; }
    public decimal? OriginalLineMinimum { get; private set; }
    public decimal? OriginalLineExpected { get; private set; }
    public decimal? OriginalLineMaximum { get; private set; }
    public decimal? CurrentUnitMinimum { get; private set; }
    public decimal? CurrentUnitExpected { get; private set; }
    public decimal? CurrentUnitMaximum { get; private set; }
    public decimal? CurrentLineMinimum { get; private set; }
    public decimal? CurrentLineExpected { get; private set; }
    public decimal? CurrentLineMaximum { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public RequirementPricingSnapshot PricingSnapshot { get; private set; } = null!;
    public RequirementTechnicalProposalItem TechnicalProposalItem { get; private set; } = null!;
    public decimal? DeltaUnitExpected =>
        OriginalUnitExpected is null || CurrentUnitExpected is null
            ? null
            : CurrentUnitExpected - OriginalUnitExpected;
    public decimal? DeltaLineExpected =>
        OriginalLineExpected is null || CurrentLineExpected is null
            ? null
            : CurrentLineExpected - OriginalLineExpected;

    public static RequirementPricingItemSnapshot Create(
        Guid pricingSnapshotId,
        Guid technicalProposalItemId,
        Guid? originalSystemId,
        Guid? originalGlassTypeId,
        Guid? originalFinishTypeId,
        string status,
        decimal? unitMinimum,
        decimal? unitExpected,
        decimal? unitMaximum,
        decimal? lineMinimum,
        decimal? lineExpected,
        decimal? lineMaximum,
        DateTimeOffset createdAtUtc)
    {
        if (pricingSnapshotId == Guid.Empty)
        {
            throw new ArgumentException(
                "El snapshot de pricing es obligatorio.",
                nameof(pricingSnapshotId));
        }

        if (technicalProposalItemId == Guid.Empty)
        {
            throw new ArgumentException(
                "El item tecnico es obligatorio.",
                nameof(technicalProposalItemId));
        }

        Requirement.EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        return new RequirementPricingItemSnapshot
        {
            Id = Guid.NewGuid(),
            PricingSnapshotId = pricingSnapshotId,
            TechnicalProposalItemId = technicalProposalItemId,
            OriginalSystemId = EmptyToNull(originalSystemId),
            OriginalGlassTypeId = EmptyToNull(originalGlassTypeId),
            OriginalFinishTypeId = EmptyToNull(originalFinishTypeId),
            CurrentSystemId = EmptyToNull(originalSystemId),
            CurrentGlassTypeId = EmptyToNull(originalGlassTypeId),
            CurrentFinishTypeId = EmptyToNull(originalFinishTypeId),
            OriginalStatus = Requirement.NormalizeRequired(status, nameof(status), 40),
            CurrentStatus = Requirement.NormalizeRequired(status, nameof(status), 40),
            OriginalUnitMinimum = RequirementPricingSnapshot.EnsureMoney(
                unitMinimum,
                nameof(unitMinimum)),
            OriginalUnitExpected = RequirementPricingSnapshot.EnsureMoney(
                unitExpected,
                nameof(unitExpected)),
            OriginalUnitMaximum = RequirementPricingSnapshot.EnsureMoney(
                unitMaximum,
                nameof(unitMaximum)),
            OriginalLineMinimum = RequirementPricingSnapshot.EnsureMoney(
                lineMinimum,
                nameof(lineMinimum)),
            OriginalLineExpected = RequirementPricingSnapshot.EnsureMoney(
                lineExpected,
                nameof(lineExpected)),
            OriginalLineMaximum = RequirementPricingSnapshot.EnsureMoney(
                lineMaximum,
                nameof(lineMaximum)),
            CurrentUnitMinimum = RequirementPricingSnapshot.EnsureMoney(
                unitMinimum,
                nameof(unitMinimum)),
            CurrentUnitExpected = RequirementPricingSnapshot.EnsureMoney(
                unitExpected,
                nameof(unitExpected)),
            CurrentUnitMaximum = RequirementPricingSnapshot.EnsureMoney(
                unitMaximum,
                nameof(unitMaximum)),
            CurrentLineMinimum = RequirementPricingSnapshot.EnsureMoney(
                lineMinimum,
                nameof(lineMinimum)),
            CurrentLineExpected = RequirementPricingSnapshot.EnsureMoney(
                lineExpected,
                nameof(lineExpected)),
            CurrentLineMaximum = RequirementPricingSnapshot.EnsureMoney(
                lineMaximum,
                nameof(lineMaximum)),
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
    }

    public void UpdateCurrent(
        Guid? currentSystemId,
        Guid? currentGlassTypeId,
        Guid? currentFinishTypeId,
        string status,
        decimal? unitMinimum,
        decimal? unitExpected,
        decimal? unitMaximum,
        decimal? lineMinimum,
        decimal? lineExpected,
        decimal? lineMaximum,
        DateTimeOffset updatedAtUtc)
    {
        Requirement.EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actualizacion no puede ser anterior a la ultima actualizacion.",
                nameof(updatedAtUtc));
        }

        CurrentSystemId = EmptyToNull(currentSystemId);
        CurrentGlassTypeId = EmptyToNull(currentGlassTypeId);
        CurrentFinishTypeId = EmptyToNull(currentFinishTypeId);
        CurrentStatus = Requirement.NormalizeRequired(status, nameof(status), 40);
        CurrentUnitMinimum = RequirementPricingSnapshot.EnsureMoney(
            unitMinimum,
            nameof(unitMinimum));
        CurrentUnitExpected = RequirementPricingSnapshot.EnsureMoney(
            unitExpected,
            nameof(unitExpected));
        CurrentUnitMaximum = RequirementPricingSnapshot.EnsureMoney(
            unitMaximum,
            nameof(unitMaximum));
        CurrentLineMinimum = RequirementPricingSnapshot.EnsureMoney(
            lineMinimum,
            nameof(lineMinimum));
        CurrentLineExpected = RequirementPricingSnapshot.EnsureMoney(
            lineExpected,
            nameof(lineExpected));
        CurrentLineMaximum = RequirementPricingSnapshot.EnsureMoney(
            lineMaximum,
            nameof(lineMaximum));
        UpdatedAtUtc = updatedAtUtc;
    }

    private static Guid? EmptyToNull(Guid? value) =>
        value == Guid.Empty ? null : value;
}
