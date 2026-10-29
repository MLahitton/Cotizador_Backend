# 2E.2C.2a: prepared publication and receipt

Scope: immutable content contract, deterministic SHA-256 and nullable job receipt.
There is no preparer, publisher, provider call, DI registration or productive consumer.
The public factory validates and freezes input; it does not authorize or persist it.

## Source evidence

- `ProcessRequirementCoreService.CompleteAttemptAsync` always creates an extraction
  and a proposal, including zero items. `CreateExtractedItems` filters invalid
  evidence and may add review flags after the source summary was calculated.
- `BuildRequirementTechnicalProposalService.BuildAsync` returns a graph but reads
  catalogs/history; `ExecuteAsync` also persists. Neither is called here.
- `Requirement.cs` factories define the initial scalar fields and invariants.
  `Requirement*Configuration` files define persisted properties and uniqueness.
- Extraction is unique per attempt; proposal is unique per extraction and attempt.
  Commercial edits can change a proposal later. Those constraints do not prove
  content equality, and the receipt must not be reconstructed from the edited graph.

## Contract and ownership

`PublicationInput` and child records are construction/transport inputs, not frozen
snapshots. `PreparedJobPublication.Create` serializes them, validates a private copy
and stores only immutable `CanonicalJson` and internally calculated `ContentHash`.
`ReadInput` returns a new independent graph on every call. No caller collection,
JsonDocument, EF entity, stream or callback is retained. Unknown DTO JSON properties
(including attempted commercial selections) are rejected by `FromJson`.

`sng-job-publication-v1` covers the initial graph, context, manifest, response outcome
and final outcome. Changing coverage, local-key semantics or serialization requires
a new version. Provider uses the existing `DocumentProcessingProvider` enum; schema
version is informational with the existing factory's length restriction, not an
invented provider-version allowlist. The canonical response must have the common
document/attempt/status/structuredExtraction envelope used by the existing adapter.
Envelope identity, counts, summary and processing metadata are checked against the
snapshot. The attempt in the payload is only structurally validated here; matching
it to an authorized execution remains the publisher's responsibility.

Local keys: extracted items use positive unique Sequence; proposal items use their
Sequence and explicit ExtractedItemSequence (one-to-one, same sequence as the current
builder). Segments use parent item key + Sequence. Alternatives use parent proposal
item key + category + Rank. Evidence and historical examples use parent key + array
position. No generated entity GUID participates in these keys. Arrays retain their
declared order, even when an independent sequence/rank key exists.

## Complete persisted-field matrix

I = included in snapshot/hash; G = generated identity/time or initial factory constant;
O = operational/execution context; C = subsequent commercial data, not accepted.
Navigation/computed properties are not database columns and are not extra input.
`PublicationModelCoverageTests` checks every scalar property in the actual EF model
against this classification so adding a persisted field cannot silently omit it.

| Entity | I | G / O / C |
|---|---|---|
| RequirementFile (manifest only, no writes) | Id as FileId, OriginalFileName, ContentType, SizeBytes, StorageKey, CreatedAtUtc | O: RequirementId, checked against execution in 2b |
| RequirementExtractionResult | SchemaVersion, Provider (root enum), PayloadJson (Extraction.Payload), ItemCount, ItemsRequiringReview, IssueCount, ConflictCount, ProcessingMethod, DurationMs | G: Id, CreatedAtUtc; O: RequirementProcessingAttemptId |
| RequirementExtractedItem | Ai2ElementId, Sequence, Reference, OccurrenceContext, Description, ElementType, Quantity, WidthMillimeters, HeightMillimeters, AreaSquareMeters, Confidence, ExtractionStatus, QuantityExtractionStatus, QuantityExtractionConfidence, RequiresReview, ReviewReasons, FunctionalType, Operation, PanelCount, MovablePanelCount, FixedPanelCount, Arrangement, Modulation, OpeningDirection, SpecialFeatures, GeometryType, AssemblyType, RequestedSystemRaw, RequestedProfileRaw, GlassRawSpecification, GlassTypeRaw, GlassTypeNormalized, GlassThicknessMm, GlassColorRaw, GlassColorNormalized, GlassTreatmentRaw, GlassTreatmentNormalized, GlassComposition, GlassCoating, GlassTransparency, GlassRequiresReview, FinishRawDescription, FinishNormalizedType, FinishColorRaw, FinishColorNormalized, FinishTextureRaw, FinishTextureNormalized, FinishExplicitCode, FinishRequiresReview | G: Id, CreatedAtUtc, RequirementExtractionResultId resolved from nesting |
| RequirementExtractedItemEvidence | PageNumber, SourceType, Text, SheetName, CellRange, SourceId, Confidence, Status | G: Id, CreatedAtUtc, RequirementExtractedItemId resolved from nesting |
| RequirementExtractedItemSegment | Sequence, Role, WidthMillimeters, HeightMillimeters, Quantity, Operation, GeometryType, EvidenceText, SourceId, SourceType, PageNumber, SheetName, CellRange, Confidence, ExtractionStatus | G: Id, CreatedAtUtc, RequirementExtractedItemId resolved from nesting |
| RequirementTechnicalProposal | Status | G: Id, CreatedAtUtc, RequirementExtractionResultId; O: RequirementId, RequirementProcessingAttemptId; C: CommercialRevision (factory starts at 1), CommercialConfirmedAtUtc, CommercialConfirmedByUserId |
| RequirementTechnicalProposalItem | Sequence, Reference, Description, ElementType, BaseQuantity, BaseWidthMillimeters, BaseHeightMillimeters, SuggestedSystemId, SuggestedGlassTypeId, SuggestedFinishTypeId, OverallConfidence, SystemConfidence, GlassConfidence, FinishConfidence, RequiresReview, IsTechnicallyComplete, IsPriceable, ReviewReasons, SystemResolutionReasons, GlassResolutionReasons, FinishResolutionReasons, HistoricalSupportCount, HistoricalBestSimilarity, HistoricalAverageSimilarity, HistoricalSimilarityStatus | G: Id, CreatedAtUtc, TechnicalProposalId, RequirementExtractedItemId (from local key), Source=AiExtracted, InclusionState=Included; C: ManualNote, ManualLocationOverride, ManualObservation, SelectedSystemId, SelectedGlassTypeId, SelectedFinishTypeId, SelectedAtUtc, SelectedByUserId, ManualQuantityOverride, ManualWidthMillimetersOverride, ManualHeightMillimetersOverride, ExcludedAtUtc, ExcludedByUserId, ExclusionReason |
| System / Glass / Finish alternative | ProductSystemId / GlassTypeId / FinishTypeId as CatalogId, Rank, Confidence, Reasons | G: Id, ProposalItemId from nesting |
| Historical example | CandidateId, QuoteId, HistoricalReference, SimilarityScore, MatchedFeatures, Differences, TechnicalExplanation | G: Id, ProposalItemId from nesting |

All child collections above are I. No pricing snapshot, Experience draft/selection,
Answers, original document bytes or commercial decision is created or changed.
Operational JobId, ExecutionRequestId, RequirementId, ProcessingAttemptId, actor,
leases and closure timestamps are not separate hash inputs. Opaque canonical payload
content is preserved in full, including identities already embedded by the adapter;
no fields are secretly removed from that evidence. Thus equality is deliberately
strict about differences inside the payload, not a cross-execution deduplication rule.
File CreatedAtUtc is existing source metadata, not a new insertion timestamp.
Provider DurationMs is included; it is not discarded as nondeterministic noise.

## Manifest and principal document

The core calls `RequirementRepository.ListFilesByRequirementIdAsync`, which orders by
CreatedAtUtc then Id. `CreateAi2Request` sets DocumentId to the first file's Id.
This is explicit existing query ordering, not upload-order evidence. The snapshot
retains the supplied manifest order and carries PrimaryDocumentId explicitly,
requiring membership and matching the canonical payload's documentId. It does not
sort by name/GUID or invent another principal-selection rule. 2b must preserve the
actual processing request's manifest and principal when adapting its response.

Available content/version signals are FileId, storage key, size and creation time,
plus name/MIME. RequirementFile has no stored content digest or revision here.
These metadata are not proof of byte equality. No bytes are read and no documentary
versioning mechanism is introduced.

## Validation and serialization

- Existing factories validate positive dimensions/quantities, confidence ranges,
  text lengths, evidence locations, summary limits and historical fields. Temporary
  factory identities used during validation are never retained or hashed.
- Prepared strings must already be factory-normalized. Trimming, dropping blanks
  or deduplicating string arrays is rejected, not silently performed. Payload text
  is opaque and is never trimmed or normalized. No Unicode normalization occurs.
- Item counts match payload and graph; issue/conflict counts match payload arrays;
  source review count matches payload items and cannot exceed prepared review flags.
  Equality with final flags is not required: evidence filtering can add review.
- Proposal covers every extracted item, preserves extracted base fields and review;
  its Status is RequiresReview iff any initial proposal item requires review.
  Outcome is RequiresReview iff ResponseOutcome or proposal requires review, otherwise
  Completed. An empty graph is valid; a response needing review can have zero items.
- Properties are ordinally sorted, recursively. Arrays preserve order. Duplicate
  JSON properties are rejected at every depth; formatting whitespace is ignored.
- UTF-8 without BOM, System.Text.Json string escaping; decoded text is unchanged.
  JSON numbers retain raw lexical spelling (1 != 1.0, also exponent/negative zero).
  Typed decimals use G29 with InvariantCulture; integer/enum JSON values are invariant
  integers. Existing file timestamps must be UTC and use the serializer's ISO format.
- SHA-256 is uppercase 64-character hexadecimal over CanonicalJson's UTF-8 bytes.
  No supplied hash is accepted by the snapshot factory. Hash equality is not an
  authorization mechanism or a replacement for coherent terminal evidence.

## Receipt and migration

`SucceedWithPublicationReceipt` validates version/hash and the existing success
lifecycle before mutation. It cannot fill a historical terminal retroactively.
`Succeed` is retained for compatibility: a Succeeded without receipt proves no
content equality. Domain success still requires a linked attempt, count=1, no
cancellation and a valid UTC lifecycle date. It does not validate DB lease or actor.

`20261022042216_AddJobPublicationReceipt` adds two nullable varchar columns (100/64)
and one CHECK: both null, or both non-null with non-whitespace version, uppercase
SHA-256 shape and Succeeded state. No hash uniqueness, backfill, job insertion or
unrelated index/constraint change. The receipt's domain version is nonempty metadata;
only the snapshot factory enforces the current contract version. No migration is
to be applied to the usual/shared database as part of this subblock.

## Reserved for 2E.2C.2b

Preparation adapter, actual graph construction, publication/recognition transactions,
authorization, locks, lease guards and persistence are absent. Publishing and historical
recognition require different policies: historical reads must use current read
authorization, not automatically inherit activity prerequisites for a new execution.
Contrast this explicitly with start/termination services before implementing it.
Canonical payload parsing here is structural validation, not re-running extraction,
the mapper, a selector or commercial rules; the adapter must supply their real output.

Receipt equality must be combined with exact execution identity and terminal evidence.
An uncertain commit requires reconciliation, never automatic provider retry. The
existing ten-second reconciliation cancellation budget is not a proven total response
deadline. No automatic processing is authorized by this block.
