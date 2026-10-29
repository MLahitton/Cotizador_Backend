using System.Collections;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

internal static class PublicationInputValidation
{
    // Temporary identities are used only to exercise existing domain factories, never serialized.
    private static readonly Guid ValidationId = new("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset ValidationTime = DateTimeOffset.UnixEpoch;
    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }

    internal static void Validate(PublicationInput input)
    {
        Require(input.ContractVersion == PreparedJobPublication.Version, "Unsupported publication version.");
        Require(input.Extraction is not null && input.Proposal is not null, "Extraction and proposal are required.");
        Require(input.Manifest is not null && input.Manifest.Count > 0, "Manifest is required.");
        Require(Enum.IsDefined(input.CommercialLine) && Enum.IsDefined(input.Provider), "Invalid context enum.");
        ValidateValues(input);
        Require(input.Manifest!.Select(x => x.FileId).Distinct().Count() == input.Manifest.Count, "Duplicate document identity.");
        Require(input.PrimaryDocumentId != Guid.Empty && input.Manifest.Any(f => f.FileId == input.PrimaryDocumentId), "Primary document is not in manifest.");
        foreach (var file in input.Manifest)
        {
            Require(file.FileId != Guid.Empty, "Empty document identity.");
            var validated = RequirementFile.Create(ValidationId, file.OriginalFileName, file.ContentType,
                file.SizeBytes, file.StorageKey, file.CreatedAtUtc);
            Require(validated.ContentType == file.ContentType, "Manifest must contain stored content type.");
        }
        var extraction = input.Extraction!;
        Require(extraction.Payload.ValueKind == JsonValueKind.Object, "Canonical payload must be an object.");
        Require(extraction.Items is not null, "Extraction items are required.");
        Require(extraction.ItemCount == extraction.Items!.Count, "Item count differs from graph.");
        ValidatePayload(input);
        _ = RequirementExtractionResult.Create(ValidationId, extraction.SchemaVersion, input.Provider.ToString(),
            extraction.Payload.GetRawText(), extraction.ItemCount, extraction.ItemsRequiringReview,
            extraction.IssueCount, extraction.ConflictCount, extraction.ProcessingMethod, extraction.DurationMs, ValidationTime);
        Unique(extraction.Items.Select(i => i.Sequence), "extracted item");
        foreach (var item in extraction.Items) ValidateItem(item);
        var proposal = input.Proposal!;
        Require(proposal.Items is not null && proposal.Items.Count == extraction.Items.Count, "Proposal must cover every extracted item.");
        Unique(proposal.Items!.Select(i => i.Sequence), "proposal item");
        Unique(proposal.Items.Select(i => i.ExtractedItemSequence), "extracted reference");
        foreach (var item in proposal.Items)
        {
            var source = extraction.Items.SingleOrDefault(i => i.Sequence == item.ExtractedItemSequence);
            Require(source is not null, "Missing extracted item reference.");
            Require(item.Sequence == source!.Sequence && item.Reference == source.Reference
                && item.Description == source.Description && item.ElementType == source.ElementType
                && item.BaseQuantity == source.Quantity && item.BaseWidthMillimeters == source.WidthMillimeters
                && item.BaseHeightMillimeters == source.HeightMillimeters, "Proposal base differs from extraction.");
            Require(!source.RequiresReview || item.RequiresReview, "Proposal lost extracted review requirement.");
            ValidateProposalItem(item);
        }
        var status = proposal.Items.Any(i => i.RequiresReview)
            ? RequirementTechnicalProposalStatus.RequiresReview : RequirementTechnicalProposalStatus.Completed;
        Require(proposal.Status == status, "Proposal status differs from initial builder semantics.");
        Require(input.ResponseOutcome is DocumentProcessingOutcome.Completed or DocumentProcessingOutcome.RequiresReview,
            "Response is not a successful processing result.");
        var outcome = input.ResponseOutcome == DocumentProcessingOutcome.RequiresReview || status == RequirementTechnicalProposalStatus.RequiresReview
            ? DocumentProcessingOutcome.RequiresReview : DocumentProcessingOutcome.Completed;
        Require(input.Outcome == outcome, "Outcome contradicts extraction/proposal review.");
        // The source counter predates filtering of invalid evidence, which can add review flags.
        Require(extraction.ItemsRequiringReview <= extraction.Items.Count(i => i.RequiresReview), "Source review count exceeds prepared review items.");
    }

    private static void ValidatePayload(PublicationInput input)
    {
        var e = input.Extraction;
        var payload = e.Payload;
        Require(payload.TryGetProperty("schemaVersion", out var schema) && schema.GetString() == e.SchemaVersion,
            "Payload schema does not match extraction.");
        Require(payload.TryGetProperty("documentId", out var document) && document.TryGetGuid(out var documentId)
            && documentId == input.PrimaryDocumentId, "Payload primary document mismatch.");
        Require(payload.TryGetProperty("processingAttemptId", out var attempt) && attempt.TryGetGuid(out var attemptId)
            && attemptId != Guid.Empty, "Payload attempt identity is required; authorization belongs to publication.");
        Require(payload.TryGetProperty("status", out var status) && status.GetString() ==
            (input.ResponseOutcome == DocumentProcessingOutcome.RequiresReview ? "REQUIRES_REVIEW" : "COMPLETED"), "Payload outcome mismatch.");
        Require(payload.TryGetProperty("structuredExtraction", out var structured) && structured.ValueKind == JsonValueKind.Object,
            "Canonical structured extraction is required.");
        Require(structured.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            && items.GetArrayLength() == e.ItemCount, "Payload item count mismatch.");
        var sequences = items.EnumerateArray().Select(i => i.GetProperty("sequence").GetInt32()).ToArray();
        Unique(sequences, "payload item");
        Require(sequences.Order().SequenceEqual(e.Items.Select(i => i.Sequence).Order()), "Payload item references mismatch.");
        Require(items.EnumerateArray().Count(i => i.GetProperty("requiresReview").GetBoolean()) == e.ItemsRequiringReview,
            "Source review count mismatch.");
        Require(structured.GetProperty("issues").GetArrayLength() == e.IssueCount
            && structured.GetProperty("conflicts").GetArrayLength() == e.ConflictCount, "Payload issue/conflict counts mismatch.");
        var summary = structured.GetProperty("summary");
        Require(summary.GetProperty("itemCount").GetInt32() == e.ItemCount
            && summary.GetProperty("itemsRequiringReview").GetInt32() == e.ItemsRequiringReview, "Payload summary mismatch.");
        var metadata = structured.GetProperty("processingMetadata");
        Require(metadata.GetProperty("method").GetString() == e.ProcessingMethod
            && metadata.GetProperty("durationMs").GetInt32() == e.DurationMs, "Payload processing metadata mismatch.");
    }

    private static void ValidateItem(PublicationExtractedItem i)
    {
        _ = RequirementExtractedItem.Create(ValidationId, i.Ai2ElementId, i.Sequence, i.Reference, i.Description,
            i.ElementType, i.Quantity, i.WidthMillimeters, i.HeightMillimeters, i.AreaSquareMeters, i.Confidence,
            i.ExtractionStatus, i.RequiresReview, i.ReviewReasons, i.FunctionalType, i.Operation, i.PanelCount,
            i.MovablePanelCount, i.FixedPanelCount, i.Arrangement, i.Modulation, i.OpeningDirection, i.SpecialFeatures,
            i.GeometryType, i.RequestedSystemRaw, i.RequestedProfileRaw, i.GlassRawSpecification, i.GlassTypeRaw,
            i.GlassTypeNormalized, i.GlassThicknessMm, i.GlassColorRaw, i.GlassColorNormalized, i.GlassTreatmentRaw,
            i.GlassTreatmentNormalized, i.GlassComposition, i.GlassCoating, i.GlassTransparency, i.GlassRequiresReview,
            i.FinishRawDescription, i.FinishNormalizedType, i.FinishColorRaw, i.FinishColorNormalized,
            i.FinishTextureRaw, i.FinishTextureNormalized, i.FinishExplicitCode, i.FinishRequiresReview,
            ValidationTime, i.AssemblyType, i.OccurrenceContext, i.QuantityExtractionStatus, i.QuantityExtractionConfidence);
        Require(i.Evidence is not null && i.Segments is not null, "Item children are required.");
        foreach (var e in i.Evidence!)
            _ = RequirementExtractedItemEvidence.Create(ValidationId, e.PageNumber, e.SourceType, e.Text,
                e.SheetName, e.CellRange, e.SourceId, e.Confidence, e.Status, ValidationTime);
        Unique(i.Segments!.Select(s => s.Sequence), "segment");
        foreach (var s in i.Segments)
            _ = RequirementExtractedItemSegment.Create(ValidationId, s.Sequence, s.Role, s.WidthMillimeters,
                s.HeightMillimeters, s.Quantity, s.Operation, s.GeometryType, s.EvidenceText, s.SourceId,
                s.SourceType, s.PageNumber, s.SheetName, s.CellRange, s.Confidence, s.ExtractionStatus, ValidationTime);
    }

    private static void ValidateProposalItem(PublicationProposalItem i)
    {
        Require(i.SuggestedSystemId != Guid.Empty && i.SuggestedGlassTypeId != Guid.Empty && i.SuggestedFinishTypeId != Guid.Empty,
            "Empty catalog identity.");
        _ = RequirementTechnicalProposalItem.Create(ValidationId, ValidationId, i.SuggestedSystemId,
            i.SuggestedGlassTypeId, i.SuggestedFinishTypeId, i.Sequence, i.Reference, i.Description, i.ElementType,
            i.BaseQuantity, i.BaseWidthMillimeters, i.BaseHeightMillimeters, i.OverallConfidence, i.SystemConfidence,
            i.GlassConfidence, i.FinishConfidence, i.RequiresReview, i.IsTechnicallyComplete, i.IsPriceable,
            i.ReviewReasons, i.SystemResolutionReasons, i.GlassResolutionReasons, i.FinishResolutionReasons,
            i.HistoricalSupportCount, i.HistoricalBestSimilarity, i.HistoricalAverageSimilarity, i.HistoricalSimilarityStatus, ValidationTime);
        foreach (var alternatives in new[] { i.SystemAlternatives, i.GlassAlternatives, i.FinishAlternatives })
        {
            Require(alternatives is not null, "Alternatives are required.");
            Unique(alternatives.Select(a => a.Rank), "alternative rank");
            foreach (var a in alternatives)
                _ = RequirementTechnicalProposalSystemAlternative.Create(ValidationId, a.CatalogId, a.Rank, a.Confidence, a.Reasons);
        }
        Require(i.HistoricalExamples is not null, "Historical examples are required.");
        foreach (var h in i.HistoricalExamples!)
            _ = RequirementTechnicalProposalHistoricalExample.Create(ValidationId, h.CandidateId, h.QuoteId,
                h.HistoricalReference, h.SimilarityScore, h.MatchedFeatures, h.Differences, h.TechnicalExplanation);
    }

    private static void Unique(IEnumerable<int> keys, string kind)
    {
        var values = keys.ToArray();
        Require(values.All(k => k > 0) && values.Distinct().Count() == values.Length, $"Invalid or duplicate {kind} key.");
    }

    // The input represents already prepared factory values. Reject normalization instead of silently
    // trimming/deduplicating them; opaque payload strings are deliberately not traversed here.
    private static void ValidateValues(object? value)
    {
        if (value is null || value is JsonElement) return;
        if (value is string text)
        {
            Require(text.Length > 0 && text == text.Trim(), "Prepared text is not a factory-normalized value.");
            return;
        }
        if (value is Enum enumeration) { Require(Enum.IsDefined(enumeration.GetType(), enumeration), "Invalid enum value."); return; }
        if (value is IEnumerable sequence)
        {
            var items = sequence.Cast<object?>().ToArray();
            Require(items.All(x => x is not null), "Null collection element.");
            if (value is IEnumerable<string> strings)
                Require(strings.Distinct(StringComparer.Ordinal).Count() == items.Length, "Duplicate prepared string.");
            foreach (var item in items) ValidateValues(item);
            return;
        }
        if (value.GetType().Namespace != typeof(PublicationInput).Namespace) return;
        foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var child = property.GetValue(value);
            if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(string))
                Require(child is not null, "Null collection.");
            ValidateValues(child);
        }
    }
}
