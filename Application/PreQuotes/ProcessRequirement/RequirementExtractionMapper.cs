using Application.Common.Abstractions.DocumentProcessing;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.ProcessRequirement;

// Shared legacy transformation. No persistence or provider calls.
public sealed class RequirementExtractionMapper(ILogger logger)
{
    private const string InvalidEvidenceLocationReason = "INVALID_EVIDENCE_LOCATION";
    public IReadOnlyList<ExtractedItemWithEvidence> CreateExtractedItems(
        Guid requirementId,
        Guid extractionResultId,
        IReadOnlyList<StructuredItemData> items,
        DateTimeOffset createdAtUtc)
    {
        return items.Select(item =>
        {
            var rawEvidenceSources = item.Glass is null
                ? item.Evidence
                : item.Evidence.Concat(item.Glass.Evidence);
            var evidenceSources = rawEvidenceSources
                .GroupBy(value => new
                {
                    value.PageNumber,
                    value.SourceType,
                    value.Text,
                    value.SheetName,
                    value.CellRange,
                    value.SourceId
                })
                .Select(group => group.First())
                .ToArray();
            var validEvidenceSources = evidenceSources
                .Where(value => IsValidEvidenceLocation(value))
                .ToArray();
            var invalidEvidenceSources = evidenceSources
                .Where(value => !IsValidEvidenceLocation(value))
                .ToArray();
            var reviewReasons = item.ReviewReasons.Select(reason =>
                reason.ToString()).ToList();
            if (invalidEvidenceSources.Length > 0)
            {
                reviewReasons.Add(InvalidEvidenceLocationReason);
            }

            var extracted = RequirementExtractedItem.Create(
                extractionResultId,
                item.Ai2ElementId,
                item.Sequence,
                item.Reference,
                item.Description,
                item.ElementType,
                item.Quantity,
                item.WidthMillimeters,
                item.HeightMillimeters,
                item.AreaSquareMeters,
                item.Confidence,
                MapExtractionStatus(item.ExtractionStatus),
                item.RequiresReview || invalidEvidenceSources.Length > 0,
                reviewReasons,
                item.FunctionalType,
                item.Operation,
                item.PanelCount,
                item.MovablePanelCount,
                item.FixedPanelCount,
                item.Arrangement,
                item.Modulation,
                item.OpeningDirection,
                item.SpecialFeatures,
                item.GeometryType,
                item.RequestedSystemRaw,
                item.RequestedProfileRaw,
                item.Glass?.RawSpecification,
                item.GlassTypeRaw,
                item.GlassTypeNormalized,
                item.GlassThicknessMm,
                item.GlassColorRaw,
                item.GlassColorNormalized,
                item.GlassTreatmentRaw,
                item.GlassTreatmentNormalized,
                item.GlassComposition,
                item.GlassCoating,
                item.GlassTransparency,
                item.Glass?.RequiresReview,
                item.FinishRawDescription,
                item.FinishNormalizedType,
                item.FinishColorRaw,
                item.FinishColorNormalized,
                item.FinishTextureRaw,
                item.FinishTextureNormalized,
                item.FinishExplicitCode,
                item.FinishRequiresReview,
                createdAtUtc,
                item.AssemblyType,
                item.OccurrenceContext,
                item.QuantityStatus is { } quantityStatus ? MapExtractionStatus(quantityStatus) : null,
                item.QuantityConfidence);
            foreach (var invalidEvidence in invalidEvidenceSources)
            {
                LogInvalidEvidence(
                    requirementId,
                    extracted.Id,
                    item.Reference,
                    item.Sequence,
                    invalidEvidence);
            }

            var persistableEvidence = validEvidenceSources.Select(value =>
                RequirementExtractedItemEvidence.Create(
                    extracted.Id,
                    value.PageNumber,
                    value.SourceType,
                    value.Text,
                    value.SheetName,
                    value.CellRange,
                    value.SourceId,
                    value.Confidence,
                    MapExtractionStatus(value.Status),
                    createdAtUtc)).ToArray();
            foreach (var evidence in persistableEvidence)
            {
                extracted.AddEvidence(evidence);
            }

            var segments = item.Segments?.Select((segment, index) =>
                RequirementExtractedItemSegment.Create(
                    extracted.Id,
                    segment.Sequence > 0 ? segment.Sequence : index + 1,
                    segment.Role,
                    segment.WidthMillimeters,
                    segment.HeightMillimeters,
                    segment.Quantity,
                    segment.Operation,
                    segment.GeometryType,
                    segment.Evidence.FirstOrDefault()?.Text,
                    segment.Evidence.FirstOrDefault()?.SourceId,
                    segment.Evidence.FirstOrDefault()?.SourceType,
                    segment.Evidence.FirstOrDefault()?.PageNumber,
                    segment.Evidence.FirstOrDefault()?.SheetName,
                    segment.Evidence.FirstOrDefault()?.CellRange,
                    segment.Evidence.FirstOrDefault()?.Confidence,
                    MapExtractionStatus(segment.Evidence.FirstOrDefault()?.Status
                        ?? CanonicalExtractionValueStatus.Explicit),
                    createdAtUtc)).ToArray() ?? [];

            foreach (var segment in segments)
            {
                extracted.AddSegment(segment);
            }

            return new ExtractedItemWithEvidence(extracted, persistableEvidence, segments);
        }).ToArray();
    }

    private static bool IsValidEvidenceLocation(SourceEvidenceData evidence)
    {
        var hasSheet = !string.IsNullOrWhiteSpace(evidence.SheetName);
        var hasCellRange = !string.IsNullOrWhiteSpace(evidence.CellRange);

        return evidence.SourceType switch
        {
            EvidenceSourceType.Native or EvidenceSourceType.Ocr =>
                evidence.PageNumber is > 0
                && !hasSheet
                && !hasCellRange,
            EvidenceSourceType.Xlsx =>
                evidence.PageNumber is null
                && hasSheet
                && hasCellRange,
            _ => false
        };
    }

    private void LogInvalidEvidence(
        Guid requirementId,
        Guid extractedItemId,
        string? reference,
        int itemSequence,
        SourceEvidenceData evidence)
    {
        logger.LogWarning(
            "[NEWPIPE-EVIDENCE-INVALID] RequirementId={RequirementId} ExtractedItemId={ExtractedItemId} Reference={Reference} ItemSequence={ItemSequence} SourceType={SourceType} SourceId={SourceId} PageNumber={PageNumber} SheetName={SheetName} CellRange={CellRange}",
            requirementId,
            extractedItemId,
            reference,
            itemSequence,
            evidence.SourceType,
            evidence.SourceId,
            evidence.PageNumber,
            evidence.SheetName,
            evidence.CellRange);
    }

    private static RequirementExtractionValueStatus MapExtractionStatus(
        CanonicalExtractionValueStatus status)
    {
        return status switch
        {
            CanonicalExtractionValueStatus.Explicit =>
                RequirementExtractionValueStatus.Explicit,
            CanonicalExtractionValueStatus.Inferred =>
                RequirementExtractionValueStatus.Inferred,
            CanonicalExtractionValueStatus.Ambiguous =>
                RequirementExtractionValueStatus.Ambiguous,
            CanonicalExtractionValueStatus.NotApplicable =>
                RequirementExtractionValueStatus.NotApplicable,
            _ => RequirementExtractionValueStatus.Unknown
        };
    }

    public sealed record ExtractedItemWithEvidence(
        RequirementExtractedItem Item,
        IReadOnlyList<RequirementExtractedItemEvidence> Evidence,
        IReadOnlyList<RequirementExtractedItemSegment> Segments);
}

