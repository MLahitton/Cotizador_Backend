using Domain.Identity;

namespace Domain.PreQuotes;

public sealed class PreQuoteDocument
{
    private PreQuoteDocument()
    {
    }

    private PreQuoteDocument(
        Guid id,
        Guid preQuoteId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string storageKey,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        PreQuoteId = preQuoteId;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid PreQuoteId { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public PreQuote PreQuote { get; private set; } = null!;

    public User CreatedByUser { get; private set; } = null!;

    public static PreQuoteDocument Create(
        Guid preQuoteId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        string storageKey,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (preQuoteId == Guid.Empty)
        {
            throw new ArgumentException(
                "La precotización es obligatoria.",
                nameof(preQuoteId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario creador es obligatorio.",
                nameof(createdByUserId));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentException(
                "El tamaño del archivo debe ser mayor que cero.",
                nameof(sizeBytes));
        }

        return new PreQuoteDocument(
            Guid.NewGuid(),
            preQuoteId,
            NormalizeRequired(
                originalFileName,
                nameof(originalFileName)),
            NormalizeRequired(
                contentType,
                nameof(contentType))
                .ToLowerInvariant(),
            sizeBytes,
            NormalizeRequired(
                storageKey,
                nameof(storageKey)),
            createdByUserId,
            createdAtUtc);
    }

    private static string NormalizeRequired(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "El valor es obligatorio.",
                parameterName);
        }

        return value.Trim();
    }
}
