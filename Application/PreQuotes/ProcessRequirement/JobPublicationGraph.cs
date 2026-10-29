using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

// Explicit initial-graph mapping; no catalog decisions and no database access.
public static class JobPublicationGraph
{
    public static PublicationExtractedItem Snapshot(RequirementExtractedItem i) => new(
        Sequence: i.Sequence,
        Description: i.Description,
        ElementType: i.ElementType,
        ReviewReasons: i.ReviewReasons,
        SpecialFeatures: i.SpecialFeatures,
        Evidence: i.Evidence.Select(Snapshot).ToArray(),
        Segments: i.Segments.Select(Snapshot).ToArray(),
        Ai2ElementId: i.Ai2ElementId,
        Reference: i.Reference,
        OccurrenceContext: i.OccurrenceContext,
        Quantity: i.Quantity,
        WidthMillimeters: i.WidthMillimeters,
        HeightMillimeters: i.HeightMillimeters,
        AreaSquareMeters: i.AreaSquareMeters,
        Confidence: i.Confidence,
        ExtractionStatus: i.ExtractionStatus,
        QuantityExtractionStatus: i.QuantityExtractionStatus,
        QuantityExtractionConfidence: i.QuantityExtractionConfidence,
        RequiresReview: i.RequiresReview,
        FunctionalType: i.FunctionalType,
        Operation: i.Operation,
        PanelCount: i.PanelCount,
        MovablePanelCount: i.MovablePanelCount,
        FixedPanelCount: i.FixedPanelCount,
        Arrangement: i.Arrangement,
        Modulation: i.Modulation,
        OpeningDirection: i.OpeningDirection,
        GeometryType: i.GeometryType,
        AssemblyType: i.AssemblyType,
        RequestedSystemRaw: i.RequestedSystemRaw,
        RequestedProfileRaw: i.RequestedProfileRaw,
        GlassRawSpecification: i.GlassRawSpecification,
        GlassTypeRaw: i.GlassTypeRaw,
        GlassTypeNormalized: i.GlassTypeNormalized,
        GlassThicknessMm: i.GlassThicknessMm,
        GlassColorRaw: i.GlassColorRaw,
        GlassColorNormalized: i.GlassColorNormalized,
        GlassTreatmentRaw: i.GlassTreatmentRaw,
        GlassTreatmentNormalized: i.GlassTreatmentNormalized,
        GlassComposition: i.GlassComposition,
        GlassCoating: i.GlassCoating,
        GlassTransparency: i.GlassTransparency,
        GlassRequiresReview: i.GlassRequiresReview,
        FinishRawDescription: i.FinishRawDescription,
        FinishNormalizedType: i.FinishNormalizedType,
        FinishColorRaw: i.FinishColorRaw,
        FinishColorNormalized: i.FinishColorNormalized,
        FinishTextureRaw: i.FinishTextureRaw,
        FinishTextureNormalized: i.FinishTextureNormalized,
        FinishExplicitCode: i.FinishExplicitCode,
        FinishRequiresReview: i.FinishRequiresReview);
    public static PublicationEvidence Snapshot(RequirementExtractedItemEvidence i) => new(
        PageNumber: i.PageNumber,
        SourceType: i.SourceType,
        Text: i.Text,
        SheetName: i.SheetName,
        CellRange: i.CellRange,
        SourceId: i.SourceId,
        Confidence: i.Confidence,
        Status: i.Status);
    public static PublicationSegment Snapshot(RequirementExtractedItemSegment i) => new(
        Sequence: i.Sequence,
        Role: i.Role,
        WidthMillimeters: i.WidthMillimeters,
        HeightMillimeters: i.HeightMillimeters,
        Quantity: i.Quantity,
        Operation: i.Operation,
        GeometryType: i.GeometryType,
        EvidenceText: i.EvidenceText,
        SourceId: i.SourceId,
        SourceType: i.SourceType,
        PageNumber: i.PageNumber,
        SheetName: i.SheetName,
        CellRange: i.CellRange,
        Confidence: i.Confidence,
        ExtractionStatus: i.ExtractionStatus);
    public static PublicationProposalItem Snapshot(RequirementTechnicalProposalItem i) => new(
        Sequence: i.Sequence,
        ExtractedItemSequence: i.Sequence,
        Reference: i.Reference,
        Description: i.Description,
        ElementType: i.ElementType,
        BaseQuantity: i.BaseQuantity,
        BaseWidthMillimeters: i.BaseWidthMillimeters,
        BaseHeightMillimeters: i.BaseHeightMillimeters,
        SuggestedSystemId: i.SuggestedSystemId,
        SuggestedGlassTypeId: i.SuggestedGlassTypeId,
        SuggestedFinishTypeId: i.SuggestedFinishTypeId,
        OverallConfidence: i.OverallConfidence,
        SystemConfidence: i.SystemConfidence,
        GlassConfidence: i.GlassConfidence,
        FinishConfidence: i.FinishConfidence,
        RequiresReview: i.RequiresReview,
        IsTechnicallyComplete: i.IsTechnicallyComplete,
        IsPriceable: i.IsPriceable,
        ReviewReasons: i.ReviewReasons,
        SystemResolutionReasons: i.SystemResolutionReasons,
        GlassResolutionReasons: i.GlassResolutionReasons,
        FinishResolutionReasons: i.FinishResolutionReasons,
        HistoricalSupportCount: i.HistoricalSupportCount,
        HistoricalBestSimilarity: i.HistoricalBestSimilarity,
        HistoricalAverageSimilarity: i.HistoricalAverageSimilarity,
        HistoricalSimilarityStatus: i.HistoricalSimilarityStatus,
        SystemAlternatives: i.SystemAlternatives.Select(a => new PublicationAlternative(a.ProductSystemId, a.Rank, a.Confidence, a.Reasons)).ToArray(),
        GlassAlternatives: i.GlassAlternatives.Select(a => new PublicationAlternative(a.GlassTypeId, a.Rank, a.Confidence, a.Reasons)).ToArray(),
        FinishAlternatives: i.FinishAlternatives.Select(a => new PublicationAlternative(a.FinishTypeId, a.Rank, a.Confidence, a.Reasons)).ToArray(),
        HistoricalExamples: i.HistoricalExamples.Select(Snapshot).ToArray());
    public static PublicationHistoricalExample Snapshot(RequirementTechnicalProposalHistoricalExample i) => new(
        CandidateId: i.CandidateId,
        QuoteId: i.QuoteId,
        HistoricalReference: i.HistoricalReference,
        SimilarityScore: i.SimilarityScore,
        MatchedFeatures: i.MatchedFeatures,
        Differences: i.Differences,
        TechnicalExplanation: i.TechnicalExplanation);

    public static RequirementExtractedItem Materialize(PublicationExtractedItem i, Guid extractionId, DateTimeOffset now)
    {
        var entity = RequirementExtractedItem.Create(extractionId, i.Ai2ElementId, i.Sequence, i.Reference, i.Description,
            i.ElementType, i.Quantity, i.WidthMillimeters, i.HeightMillimeters, i.AreaSquareMeters, i.Confidence,
            i.ExtractionStatus, i.RequiresReview, i.ReviewReasons, i.FunctionalType, i.Operation, i.PanelCount,
            i.MovablePanelCount, i.FixedPanelCount, i.Arrangement, i.Modulation, i.OpeningDirection, i.SpecialFeatures,
            i.GeometryType, i.RequestedSystemRaw, i.RequestedProfileRaw, i.GlassRawSpecification, i.GlassTypeRaw,
            i.GlassTypeNormalized, i.GlassThicknessMm, i.GlassColorRaw, i.GlassColorNormalized, i.GlassTreatmentRaw,
            i.GlassTreatmentNormalized, i.GlassComposition, i.GlassCoating, i.GlassTransparency, i.GlassRequiresReview,
            i.FinishRawDescription, i.FinishNormalizedType, i.FinishColorRaw, i.FinishColorNormalized,
            i.FinishTextureRaw, i.FinishTextureNormalized, i.FinishExplicitCode, i.FinishRequiresReview,
            now, i.AssemblyType, i.OccurrenceContext, i.QuantityExtractionStatus, i.QuantityExtractionConfidence);

        foreach (var e in i.Evidence)
            entity.AddEvidence(RequirementExtractedItemEvidence.Create(entity.Id, e.PageNumber, e.SourceType, e.Text,
                e.SheetName, e.CellRange, e.SourceId, e.Confidence, e.Status, now));
        foreach (var s in i.Segments)
            entity.AddSegment(RequirementExtractedItemSegment.Create(entity.Id, s.Sequence, s.Role, s.WidthMillimeters,
                s.HeightMillimeters, s.Quantity, s.Operation, s.GeometryType, s.EvidenceText, s.SourceId,
                s.SourceType, s.PageNumber, s.SheetName, s.CellRange, s.Confidence, s.ExtractionStatus, now));
        return entity;
    }

    public static RequirementTechnicalProposalItem Materialize(PublicationProposalItem i, Guid proposalId, Guid extractedId, DateTimeOffset now)
    {
        var entity = RequirementTechnicalProposalItem.Create(proposalId, extractedId, i.SuggestedSystemId,
            i.SuggestedGlassTypeId, i.SuggestedFinishTypeId, i.Sequence, i.Reference, i.Description, i.ElementType,
            i.BaseQuantity, i.BaseWidthMillimeters, i.BaseHeightMillimeters, i.OverallConfidence, i.SystemConfidence,
            i.GlassConfidence, i.FinishConfidence, i.RequiresReview, i.IsTechnicallyComplete, i.IsPriceable,
            i.ReviewReasons, i.SystemResolutionReasons, i.GlassResolutionReasons, i.FinishResolutionReasons,
            i.HistoricalSupportCount, i.HistoricalBestSimilarity, i.HistoricalAverageSimilarity, i.HistoricalSimilarityStatus, now);

        foreach (var a in i.SystemAlternatives)
            entity.AddSystemAlternative(RequirementTechnicalProposalSystemAlternative.Create(entity.Id, a.CatalogId, a.Rank, a.Confidence, a.Reasons));
        foreach (var a in i.GlassAlternatives)
            entity.AddGlassAlternative(RequirementTechnicalProposalGlassAlternative.Create(entity.Id, a.CatalogId, a.Rank, a.Confidence, a.Reasons));
        foreach (var a in i.FinishAlternatives)
            entity.AddFinishAlternative(RequirementTechnicalProposalFinishAlternative.Create(entity.Id, a.CatalogId, a.Rank, a.Confidence, a.Reasons));
        foreach (var h in i.HistoricalExamples)
            entity.AddHistoricalExample(RequirementTechnicalProposalHistoricalExample.Create(entity.Id, h.CandidateId, h.QuoteId,
                h.HistoricalReference, h.SimilarityScore, h.MatchedFeatures, h.Differences, h.TechnicalExplanation));
        return entity;
    }
}

