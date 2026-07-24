using System.Text.Json;

namespace Domain.PreQuotes;

public sealed class DocumentExtractionResult
{
    private DocumentExtractionResult()
    {
    }

    private DocumentExtractionResult(
        Guid id,
        Guid documentProcessingAttemptId,
        string schemaVersion,
        PdfClassification classification,
        bool requiresOcr,
        int pageCount,
        string processingMethod,
        int durationMs,
        string payloadJson,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        DocumentProcessingAttemptId = documentProcessingAttemptId;
        SchemaVersion = schemaVersion;
        Classification = classification;
        RequiresOcr = requiresOcr;
        PageCount = pageCount;
        ProcessingMethod = processingMethod;
        DurationMs = durationMs;
        PayloadJson = payloadJson;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid DocumentProcessingAttemptId { get; private set; }

    public string SchemaVersion { get; private set; } = string.Empty;

    public PdfClassification Classification { get; private set; }

    public bool RequiresOcr { get; private set; }

    public int PageCount { get; private set; }

    public string ProcessingMethod { get; private set; } = string.Empty;

    public int DurationMs { get; private set; }

    public string PayloadJson { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DocumentProcessingAttempt ProcessingAttempt { get; private set; } =
        null!;

    public static DocumentExtractionResult Create(
        Guid documentProcessingAttemptId,
        string schemaVersion,
        PdfClassification classification,
        bool requiresOcr,
        int pageCount,
        string processingMethod,
        int durationMs,
        string payloadJson,
        DateTimeOffset createdAtUtc)
    {
        if (documentProcessingAttemptId == Guid.Empty)
        {
            throw new ArgumentException(
                "El intento de procesamiento es obligatorio.",
                nameof(documentProcessingAttemptId));
        }

        var normalizedSchemaVersion = NormalizeRequired(
            schemaVersion,
            20,
            "La versión del esquema",
            nameof(schemaVersion));

        if (!Enum.IsDefined(typeof(PdfClassification), classification))
        {
            throw new ArgumentException(
                "La clasificación del PDF no es válida.",
                nameof(classification));
        }

        var expectedRequiresOcr =
            classification is PdfClassification.PdfScanned
                or PdfClassification.PdfMixed;

        if (requiresOcr != expectedRequiresOcr)
        {
            throw new ArgumentException(
                "La necesidad de OCR no es coherente con la clasificación del PDF.",
                nameof(requiresOcr));
        }

        if (pageCount < 1)
        {
            throw new ArgumentException(
                "La cantidad de páginas debe ser al menos uno.",
                nameof(pageCount));
        }

        var normalizedProcessingMethod = NormalizeRequired(
            processingMethod,
            100,
            "El método de procesamiento",
            nameof(processingMethod));

        if (durationMs < 0)
        {
            throw new ArgumentException(
                "La duración del procesamiento no puede ser negativa.",
                nameof(durationMs));
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            throw new ArgumentException(
                "El contenido JSON es obligatorio.",
                nameof(payloadJson));
        }

        var normalizedPayloadJson = payloadJson.Trim();

        try
        {
            using var jsonDocument = JsonDocument.Parse(normalizedPayloadJson);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "El contenido JSON no es válido.",
                nameof(payloadJson),
                exception);
        }

        return new DocumentExtractionResult(
            Guid.NewGuid(),
            documentProcessingAttemptId,
            normalizedSchemaVersion,
            classification,
            requiresOcr,
            pageCount,
            normalizedProcessingMethod,
            durationMs,
            normalizedPayloadJson,
            createdAtUtc);
    }

    private static string NormalizeRequired(
        string value,
        int maximumLength,
        string fieldName,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{fieldName} es obligatorio.",
                parameterName);
        }

        var normalizedValue = value.Trim();

        if (normalizedValue.Length > maximumLength)
        {
            throw new ArgumentException(
                $"{fieldName} no puede superar {maximumLength} caracteres.",
                parameterName);
        }

        return normalizedValue;
    }
}
