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
        string schemaVersion = "1.0",
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
            new MetadataPayload(method, durationMs));

        return JsonSerializer.Serialize(
            payload,
            new JsonSerializerOptions(SerializerOptions)
            {
                WriteIndented = writeIndented
            });
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
        MetadataPayload ProcessingMetadata);

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
