using Application.Common.Abstractions.PreQuotes;
using Contracts.PreQuotes;
using Domain.PreQuotes;

namespace Api.Controllers;

internal static class PreQuoteDocumentResponseMapper
{
    public static DocumentProcessingAttemptSummaryResponse? Map(
        DocumentProcessingAttemptSummaryReadModel? attempt) =>
        attempt is null
            ? null
            : new DocumentProcessingAttemptSummaryResponse(
                attempt.ProcessingAttemptId,
                Map(attempt.ProcessingState),
                attempt.Outcome is { } outcome ? Map(outcome) : null,
                attempt.ErrorCode,
                attempt.CreatedAtUtc,
                attempt.StartedAtUtc,
                attempt.CompletedAtUtc,
                attempt.ResultMetadata is null
                    ? null
                    : new DocumentExtractionResultMetadataResponse(
                        attempt.ResultMetadata.SchemaVersion,
                        Map(attempt.ResultMetadata.Classification),
                        attempt.ResultMetadata.RequiresOcr,
                        attempt.ResultMetadata.PageCount,
                        attempt.ResultMetadata.ProcessingMethod,
                        attempt.ResultMetadata.DurationMs));

    public static string Map(DocumentProcessingAvailability value) =>
        value switch
        {
            DocumentProcessingAvailability.NotProcessed => "NOT_PROCESSED",
            DocumentProcessingAvailability.Pending => "PENDING",
            DocumentProcessingAvailability.Processing => "PROCESSING",
            DocumentProcessingAvailability.Failed => "FAILED",
            DocumentProcessingAvailability.LegacyOnly => "LEGACY_ONLY",
            DocumentProcessingAvailability.AvailableCurrent =>
                "AVAILABLE_CURRENT",
            DocumentProcessingAvailability.AvailablePrevious =>
                "AVAILABLE_PREVIOUS",
            _ => throw new InvalidOperationException()
        };

    public static string Map(StructuredExtractionStatus value) =>
        value switch
        {
            StructuredExtractionStatus.Completed => "COMPLETED",
            StructuredExtractionStatus.RequiresReview => "REQUIRES_REVIEW",
            _ => throw new InvalidOperationException()
        };

    public static StructuredExtractionDetailsResponse Map(
        StructuredExtractionDetailsReadModel value) =>
        new(
            value.StructuredExtractionId,
            value.SourceProcessingAttemptId,
            value.IsFromLatestAttempt,
            Map(value.Status),
            new StructuredProjectResponse(
                value.Project.Name,
                value.Project.ClientName,
                value.Project.Location,
                value.Project.SourcePages,
                value.Project.Evidence.Select(Map).ToArray()),
            value.Requirements.Select(item => new StructuredRequirementResponse(
                item.Sequence, Map(item.Category), item.Value,
                item.Evidence.Select(Map).ToArray())).ToArray(),
            value.Items.Select(item => new StructuredItemResponse(
                item.Sequence, item.Reference, item.Description,
                Map(item.ElementType), item.RawMeasurements,
                item.WidthMillimeters, item.HeightMillimeters, item.Quantity,
                item.RequiresReview, item.ReviewReasons.Select(Map).ToArray(),
                item.SourcePages, item.Evidence.Select(Map).ToArray())).ToArray(),
            value.DocumentReferences.Select(item =>
                new StructuredDocumentReferenceResponse(
                    item.Sequence, item.Reference, item.Description,
                    item.Detail, item.Quantity, item.SourcePages,
                    item.Evidence.Select(Map).ToArray())).ToArray(),
            value.Issues.Select(item => new StructuredIssueResponse(
                item.Sequence, Map(item.Code), item.Message,
                item.ItemSequence, item.PageNumbers)).ToArray(),
            value.Conflicts.Select(item => new StructuredConflictResponse(
                item.Sequence, Map(item.Code), item.Message,
                item.ItemSequences, item.PageNumbers)).ToArray(),
            new StructuredSummaryResponse(
                value.Summary.ItemCount,
                value.Summary.DocumentReferenceCount,
                value.Summary.ItemsRequiringReview,
                value.Summary.KnownQuoteableUnitCount,
                value.Summary.IssueCount,
                value.Summary.ConflictCount),
            new StructuredProcessingMetadataResponse(
                value.ProcessingMetadata.Method,
                value.ProcessingMetadata.DurationMs),
            value.CreatedAtUtc);

    private static StructuredEvidenceResponse Map(
        StructuredEvidenceReadModel value) =>
        new(value.PageNumber, value.SourceType == EvidenceSourceType.Native
            ? "NATIVE" : "OCR", value.Text);
    private static string Map(DocumentProcessingState value) => value switch
    {
        DocumentProcessingState.Pending => "PENDING",
        DocumentProcessingState.Processing => "PROCESSING",
        DocumentProcessingState.Finished => "FINISHED",
        _ => throw new InvalidOperationException()
    };
    private static string Map(DocumentProcessingOutcome value) => value switch
    {
        DocumentProcessingOutcome.Completed => "COMPLETED",
        DocumentProcessingOutcome.RequiresReview => "REQUIRES_REVIEW",
        DocumentProcessingOutcome.Failed => "FAILED",
        _ => throw new InvalidOperationException()
    };
    private static string Map(PdfClassification value) => value switch
    {
        PdfClassification.PdfText => "PDF_TEXT",
        PdfClassification.PdfScanned => "PDF_SCANNED",
        PdfClassification.PdfMixed => "PDF_MIXED",
        _ => throw new InvalidOperationException()
    };
    private static string Map(StructuredElementType value) =>
        value.ToString().ToUpperInvariant();
    private static string Map(RequirementCategory value) => value switch
    {
        RequirementCategory.GlassSpecification => "GLASS_SPECIFICATION",
        RequirementCategory.ProfileSpecification => "PROFILE_SPECIFICATION",
        RequirementCategory.Finish => "FINISH",
        RequirementCategory.AccessoriesAndSealants =>
            "ACCESSORIES_AND_SEALANTS",
        RequirementCategory.GeneralNote => "GENERAL_NOTE",
        _ => throw new InvalidOperationException()
    };
    private static string Map(StructuredIssueCode value) => value switch
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
        _ => throw new InvalidOperationException()
    };
    private static string Map(StructuredConflictCode value) => value switch
    {
        StructuredConflictCode.ConflictingProjectName =>
            "CONFLICTING_PROJECT_NAME",
        StructuredConflictCode.ConflictingClientName =>
            "CONFLICTING_CLIENT_NAME",
        StructuredConflictCode.ConflictingLocation => "CONFLICTING_LOCATION",
        StructuredConflictCode.DuplicateItemReference =>
            "DUPLICATE_ITEM_REFERENCE",
        _ => throw new InvalidOperationException()
    };
}
