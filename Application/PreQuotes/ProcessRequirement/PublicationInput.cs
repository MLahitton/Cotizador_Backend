using System.Text.Json;
using Domain.PreQuotes;
using Application.Common.Abstractions.DocumentProcessing;

namespace Application.PreQuotes.ProcessRequirement;

// Construction inputs only. PreparedJobPublication owns a validated, frozen copy.
public sealed record PublicationInput(string ContractVersion, IReadOnlyList<PublicationFile> Manifest,
    Guid PrimaryDocumentId, RequirementCommercialLine CommercialLine, DocumentProcessingProvider Provider,
    DocumentProcessingOutcome ResponseOutcome, DocumentProcessingOutcome Outcome,
    PublicationExtraction Extraction, PublicationProposal Proposal);

public sealed record PublicationFile(Guid FileId, string OriginalFileName, string ContentType,
    long SizeBytes, string StorageKey, DateTimeOffset CreatedAtUtc);

public sealed record PublicationExtraction(string SchemaVersion, JsonElement Payload, int ItemCount,
    int ItemsRequiringReview, int IssueCount, int ConflictCount, string ProcessingMethod, int DurationMs,
    IReadOnlyList<PublicationExtractedItem> Items);

public sealed record PublicationExtractedItem(
    int Sequence, string Description, StructuredElementType ElementType,
    IReadOnlyList<string> ReviewReasons, IReadOnlyList<string> SpecialFeatures,
    IReadOnlyList<PublicationEvidence> Evidence, IReadOnlyList<PublicationSegment> Segments,
    string? Ai2ElementId = null, string? Reference = null, string? OccurrenceContext = null,
    int? Quantity = null, int? WidthMillimeters = null, int? HeightMillimeters = null,
    decimal? AreaSquareMeters = null, decimal? Confidence = null,
    RequirementExtractionValueStatus ExtractionStatus = RequirementExtractionValueStatus.Unknown,
    RequirementExtractionValueStatus? QuantityExtractionStatus = null,
    decimal? QuantityExtractionConfidence = null, bool RequiresReview = false,
    string? FunctionalType = null, string? Operation = null, int? PanelCount = null,
    int? MovablePanelCount = null, int? FixedPanelCount = null, string? Arrangement = null,
    string? Modulation = null, string? OpeningDirection = null, string? GeometryType = null,
    string? AssemblyType = null, string? RequestedSystemRaw = null, string? RequestedProfileRaw = null,
    string? GlassRawSpecification = null, string? GlassTypeRaw = null, string? GlassTypeNormalized = null,
    decimal? GlassThicknessMm = null, string? GlassColorRaw = null, string? GlassColorNormalized = null,
    string? GlassTreatmentRaw = null, string? GlassTreatmentNormalized = null,
    string? GlassComposition = null, string? GlassCoating = null, string? GlassTransparency = null,
    bool? GlassRequiresReview = null, string? FinishRawDescription = null, string? FinishNormalizedType = null,
    string? FinishColorRaw = null, string? FinishColorNormalized = null, string? FinishTextureRaw = null,
    string? FinishTextureNormalized = null, string? FinishExplicitCode = null, bool? FinishRequiresReview = null);

public sealed record PublicationEvidence(int? PageNumber, EvidenceSourceType SourceType, string Text,
    string? SheetName, string? CellRange, string? SourceId, decimal? Confidence,
    RequirementExtractionValueStatus Status);

public sealed record PublicationSegment(int Sequence, string? Role, int? WidthMillimeters,
    int? HeightMillimeters, int? Quantity, string? Operation, string? GeometryType,
    string? EvidenceText, string? SourceId, EvidenceSourceType? SourceType, int? PageNumber,
    string? SheetName, string? CellRange, decimal? Confidence, RequirementExtractionValueStatus ExtractionStatus);

public sealed record PublicationProposal(RequirementTechnicalProposalStatus Status,
    IReadOnlyList<PublicationProposalItem> Items);

public sealed record PublicationProposalItem(int Sequence, int ExtractedItemSequence,
    string? Reference, string Description, StructuredElementType ElementType,
    int? BaseQuantity, int? BaseWidthMillimeters, int? BaseHeightMillimeters,
    Guid? SuggestedSystemId, Guid? SuggestedGlassTypeId, Guid? SuggestedFinishTypeId,
    decimal OverallConfidence, decimal SystemConfidence, decimal GlassConfidence, decimal FinishConfidence,
    bool RequiresReview, bool IsTechnicallyComplete, bool IsPriceable,
    IReadOnlyList<string> ReviewReasons, IReadOnlyList<string> SystemResolutionReasons,
    IReadOnlyList<string> GlassResolutionReasons, IReadOnlyList<string> FinishResolutionReasons,
    int HistoricalSupportCount, decimal? HistoricalBestSimilarity, decimal? HistoricalAverageSimilarity,
    string HistoricalSimilarityStatus, IReadOnlyList<PublicationAlternative> SystemAlternatives,
    IReadOnlyList<PublicationAlternative> GlassAlternatives, IReadOnlyList<PublicationAlternative> FinishAlternatives,
    IReadOnlyList<PublicationHistoricalExample> HistoricalExamples);

public sealed record PublicationAlternative(Guid CatalogId, int Rank, decimal Confidence, IReadOnlyList<string> Reasons);
public sealed record PublicationHistoricalExample(string CandidateId, string QuoteId, string? HistoricalReference,
    decimal SimilarityScore, IReadOnlyList<string> MatchedFeatures, IReadOnlyList<string> Differences,
    string TechnicalExplanation);
