using System.Text;
using System.Text.Json;

namespace CotizadorBackend.Tests.TestDoubles;

public static class DocumentProcessingPayloadFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string CreateSuccess(
        Guid documentId,
        Guid processingAttemptId,
        string classification = "PDF_TEXT",
        int pageCount = 1,
        IReadOnlyList<PayloadPage>? pages = null,
        IReadOnlyList<PayloadWarning>? warnings = null,
        string schemaVersion = "2.0",
        string? status = null,
        bool? requiresOcr = null,
        string fileName = "document.pdf",
        string contentType = "application/pdf",
        long sizeBytes = 4,
        string method = "pymupdf",
        int durationMs = 15,
        bool writeIndented = false)
    {
        var resolvedPages = pages?.ToArray()
            ?? CreatePages(classification, pageCount);
        var resolvedStatus = status ?? classification switch
        {
            "PDF_TEXT" => "COMPLETED",
            _ => "REQUIRES_REVIEW"
        };
        var resolvedRequiresOcr = requiresOcr
            ?? classification != "PDF_TEXT";
        var resolvedWarnings = warnings?.ToArray()
            ?? CreateWarnings(classification, resolvedPages);
        var payload = new SuccessPayload(
            schemaVersion,
            documentId,
            processingAttemptId,
            resolvedStatus,
            new DocumentPayload(
                fileName,
                contentType,
                sizeBytes,
                pageCount,
                classification,
                resolvedRequiresOcr),
            resolvedPages,
            resolvedWarnings,
            new MetadataPayload(method, durationMs),
            CreateStructuredExtraction(resolvedStatus));

        return JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions(SerializerOptions)
            {
                WriteIndented = writeIndented
            });
    }

    private static StructuredExtractionPayload CreateStructuredExtraction(
        string status)
    {
        var evidence = new[]
        {
            new EvidencePayload(1, "NATIVE", "Synthetic evidence")
        };
        var requiresReview = status == "REQUIRES_REVIEW";
        return new StructuredExtractionPayload(
            status,
            new ProjectPayload(
                "Synthetic project",
                "Synthetic client",
                "Bogota",
                [1],
                evidence),
            new RequirementsPayload(
                [new RequirementPayload("Tempered glass", evidence)],
                [],
                [],
                [],
                []),
            [
                new ItemPayload(
                    1,
                    "W-01",
                    "Synthetic window",
                    "WINDOW",
                    "1200 x 1000 mm",
                    1200,
                    1000,
                    2,
                    requiresReview,
                    requiresReview
                        ? ["OCR_REVIEW_REQUIRED"]
                        : [],
                    [1],
                    evidence)
            ],
            [
                new DocumentReferencePayload(
                    1,
                    "PLAN-01",
                    "Synthetic drawing",
                    "Reference only",
                    99,
                    [1],
                    evidence)
            ],
            requiresReview
                ? [new IssuePayload(
                    "OCR_REVIEW_REQUIRED",
                    "Review synthetic OCR evidence.",
                    1,
                    [1])]
                : [],
            [],
            new SummaryPayload(1, 1, requiresReview ? 1 : 0, 2),
            new MetadataPayload("rule_based_v1", 5));
    }

    public static string CreateError(
        string errorCode,
        string message,
        string schemaVersion = "1.0")
    {
        return JsonSerializer.Serialize(
            new ErrorPayload(schemaVersion, errorCode, message),
            SerializerOptions);
    }

    private static PayloadPage[] CreatePages(
        string classification,
        int pageCount)
    {
        var pages = new PayloadPage[pageCount];

        for (var index = 0; index < pageCount; index++)
        {
            var pageNumber = index + 1;
            var text = classification switch
            {
                "PDF_SCANNED" => string.Empty,
                "PDF_MIXED" when pageNumber % 2 == 0 => string.Empty,
                _ => $"Page {pageNumber}"
            };

            pages[index] = new PayloadPage(
                pageNumber,
                text,
                text.EnumerateRunes().Count(),
                !string.IsNullOrWhiteSpace(text));
        }

        return pages;
    }

    private static PayloadWarning[] CreateWarnings(
        string classification,
        IReadOnlyList<PayloadPage> pages)
    {
        return classification switch
        {
            "PDF_SCANNED" =>
            [
                new PayloadWarning(
                    "OCR_REQUIRED",
                    "The document does not contain extractable text.",
                    pages.Select(page => page.PageNumber).ToArray())
            ],
            "PDF_MIXED" =>
            [
                new PayloadWarning(
                    "PARTIAL_OCR_REQUIRED",
                    "Some pages do not contain extractable text and require OCR.",
                    pages
                        .Where(page => !page.HasExtractableText)
                        .Select(page => page.PageNumber)
                        .ToArray())
            ],
            _ => []
        };
    }

    private sealed record SuccessPayload(
        string SchemaVersion,
        Guid DocumentId,
        Guid ProcessingAttemptId,
        string Status,
        DocumentPayload Document,
        IReadOnlyList<PayloadPage> Pages,
        IReadOnlyList<PayloadWarning> Warnings,
        MetadataPayload ProcessingMetadata,
        StructuredExtractionPayload StructuredExtraction);

    private sealed record DocumentPayload(
        string FileName,
        string ContentType,
        long SizeBytes,
        int PageCount,
        string Classification,
        bool RequiresOcr);

    private sealed record MetadataPayload(
        string Method,
        int DurationMs);

    private sealed record ErrorPayload(
        string SchemaVersion,
        string ErrorCode,
        string Message);

    private sealed record StructuredExtractionPayload(
        string Status,
        ProjectPayload Project,
        RequirementsPayload Requirements,
        IReadOnlyList<ItemPayload> Items,
        IReadOnlyList<DocumentReferencePayload> DocumentReferences,
        IReadOnlyList<IssuePayload> Issues,
        IReadOnlyList<ConflictPayload> Conflicts,
        SummaryPayload Summary,
        MetadataPayload ProcessingMetadata);
    private sealed record ProjectPayload(
        string? Name, string? ClientName, string? Location,
        IReadOnlyList<int> SourcePages,
        IReadOnlyList<EvidencePayload> Evidence);
    private sealed record RequirementsPayload(
        IReadOnlyList<RequirementPayload> GlassSpecifications,
        IReadOnlyList<RequirementPayload> ProfileSpecifications,
        IReadOnlyList<RequirementPayload> Finishes,
        IReadOnlyList<RequirementPayload> AccessoriesAndSealants,
        IReadOnlyList<RequirementPayload> GeneralNotes);
    private sealed record RequirementPayload(
        string Value, IReadOnlyList<EvidencePayload> Evidence);
    private sealed record EvidencePayload(
        int PageNumber, string SourceType, string Text);
    private sealed record ItemPayload(
        int Sequence, string? Reference, string Description,
        string ElementType, string? RawMeasurements,
        int? WidthMillimeters, int? HeightMillimeters, int? Quantity,
        bool RequiresReview, IReadOnlyList<string> ReviewReasons,
        IReadOnlyList<int> SourcePages,
        IReadOnlyList<EvidencePayload> Evidence);
    private sealed record DocumentReferencePayload(
        int Sequence, string? Reference, string Description,
        string? Detail, int? Quantity, IReadOnlyList<int> SourcePages,
        IReadOnlyList<EvidencePayload> Evidence);
    private sealed record IssuePayload(
        string Code, string Message, int? ItemSequence,
        IReadOnlyList<int> PageNumbers);
    private sealed record ConflictPayload(
        string Code, string Message, IReadOnlyList<int> ItemSequences,
        IReadOnlyList<int> PageNumbers);
    private sealed record SummaryPayload(
        int ItemCount, int DocumentReferenceCount,
        int ItemsRequiringReview, int KnownQuoteableUnitCount);
}

public sealed record PayloadPage(
    int PageNumber,
    string Text,
    int CharacterCount,
    bool HasExtractableText);

public sealed record PayloadWarning(
    string Code,
    string Message,
    IReadOnlyList<int> PageNumbers);
