using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Domain.PreQuotes;

// Structural evidence only: construction does not verify bytes or execution authority.
public sealed class RequirementExecutionEvidence
{
    private RequirementExecutionEvidence() { }
    public Guid Id { get; private set; }
    public Guid RequirementId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid ExecutionRequestId { get; private set; }
    public Guid ProcessingAttemptId { get; private set; }
    public long LeaseGeneration { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Guid PrimaryDocumentId { get; private set; }
    public short ManifestVersion { get; private set; }
    public string ManifestHash { get; private set; } = null!;
    public DateTimeOffset VerifiedAtUtc { get; private set; }
    public DateTimeOffset RegisteredAtUtc { get; private set; }
    public int FileCount { get; private set; }
    private readonly List<RequirementExecutionDocument> files = [];
    public IReadOnlyList<RequirementExecutionDocument> Files => Array.AsReadOnly(files.OrderBy(f => f.Ordinal).ToArray());

    public static RequirementExecutionEvidence Create(Guid id, Guid requirementId, Guid jobId,
        Guid executionRequestId, Guid processingAttemptId, long leaseGeneration, Guid actorUserId,
        Guid primaryDocumentId, short manifestVersion, DateTimeOffset verifiedAtUtc,
        DateTimeOffset registeredAtUtc, int fileCount, IEnumerable<RequirementExecutionDocument> documents)
    {
        foreach (var value in new[] { id, requirementId, jobId, executionRequestId, processingAttemptId, actorUserId, primaryDocumentId })
            ExecutionEvidenceValidation.Id(value);
        if (leaseGeneration != 1 || manifestVersion != 1)
            throw new ArgumentException("Only generation 1 and manifest version 1 are supported.");
        ExecutionEvidenceValidation.Date(verifiedAtUtc);
        ExecutionEvidenceValidation.Date(registeredAtUtc);
        ArgumentNullException.ThrowIfNull(documents);
        var copy = documents.ToArray();
        if (copy.Length == 0 || fileCount != copy.Length || copy.Any(f => f is null)
            || copy.Select(f => f.FileId).Distinct().Count() != copy.Length)
            throw new ArgumentException("A complete, nonempty, unique document set is required.", nameof(documents));
        for (var i = 0; i < copy.Length; i++)
            if (copy[i].EvidenceId != id || copy[i].Ordinal != i)
                throw new ArgumentException("Document identity and contiguous input order are required.", nameof(documents));
        if (copy[0].FileId != primaryDocumentId)
            throw new ArgumentException("The primary document must have ordinal zero.", nameof(primaryDocumentId));
        var result = new RequirementExecutionEvidence
        {
            Id = id, RequirementId = requirementId, JobId = jobId, ExecutionRequestId = executionRequestId,
            ProcessingAttemptId = processingAttemptId, LeaseGeneration = leaseGeneration, ActorUserId = actorUserId,
            PrimaryDocumentId = primaryDocumentId, ManifestVersion = manifestVersion,
            VerifiedAtUtc = verifiedAtUtc, RegisteredAtUtc = registeredAtUtc, FileCount = fileCount
        };
        result.files.AddRange(copy);
        result.ManifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result.CanonicalManifest())));
        return result;
    }

    public string CanonicalManifest()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("actorUserId", ActorUserId.ToString("D"));
            writer.WriteString("executionRequestId", ExecutionRequestId.ToString("D"));
            writer.WriteStartArray("files");
            foreach (var file in Files)
            {
                writer.WriteStartObject();
                writer.WriteString("contentType", file.ContentType);
                writer.WriteNumber("declaredLengthBytes", file.DeclaredLengthBytes);
                writer.WriteNumber("expectedLengthBytes", file.ExpectedLengthBytes);
                writer.WriteString("expectedSha256", file.ExpectedSha256);
                writer.WriteString("fileCreatedAtUtc", ExecutionEvidenceValidation.Format(file.FileCreatedAtUtc));
                writer.WriteString("fileId", file.FileId.ToString("D"));
                writer.WriteNumber("observedLengthBytes", file.ObservedLengthBytes);
                writer.WriteString("observedSha256", file.ObservedSha256);
                writer.WriteNumber("ordinal", file.Ordinal);
                writer.WriteString("originalFileName", file.OriginalFileName);
                writer.WriteString("storageKey", file.StorageKey);
                writer.WriteString("storedEvidenceAtUtc", ExecutionEvidenceValidation.Format(file.StoredEvidenceAtUtc));
                writer.WriteNumber("storedEvidenceVersion", file.StoredEvidenceVersion);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("jobId", JobId.ToString("D"));
            writer.WriteNumber("leaseGeneration", LeaseGeneration);
            writer.WriteNumber("manifestVersion", ManifestVersion);
            writer.WriteString("primaryDocumentId", PrimaryDocumentId.ToString("D"));
            writer.WriteString("processingAttemptId", ProcessingAttemptId.ToString("D"));
            writer.WriteString("requirementId", RequirementId.ToString("D"));
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

public sealed class RequirementExecutionDocument
{
    private RequirementExecutionDocument() { }
    public Guid EvidenceId { get; private set; }
    public Guid FileId { get; private set; }
    public int Ordinal { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public DateTimeOffset FileCreatedAtUtc { get; private set; }
    public long DeclaredLengthBytes { get; private set; }
    public string ExpectedSha256 { get; private set; } = null!;
    public long ExpectedLengthBytes { get; private set; }
    public DateTimeOffset StoredEvidenceAtUtc { get; private set; }
    public short StoredEvidenceVersion { get; private set; }
    public string ObservedSha256 { get; private set; } = null!;
    public long ObservedLengthBytes { get; private set; }

    public static RequirementExecutionDocument Create(
        Guid evidenceId,
        Guid fileId,
        int ordinal,
        string originalFileName,
        string contentType,
        string storageKey,
        DateTimeOffset fileCreatedAtUtc,
        long declaredLengthBytes,
        string expectedSha256,
        long expectedLengthBytes,
        DateTimeOffset storedEvidenceAtUtc,
        short storedEvidenceVersion,
        string observedSha256,
        long observedLengthBytes)
    {
        ExecutionEvidenceValidation.Id(evidenceId);
        ExecutionEvidenceValidation.Id(fileId);
        if (ordinal < 0 || storedEvidenceVersion != 1 || declaredLengthBytes <= 0
            || declaredLengthBytes != expectedLengthBytes || expectedLengthBytes != observedLengthBytes)
            throw new ArgumentException("Invalid incorporation version, ordinal or lengths.");
        ExecutionEvidenceValidation.Hash(expectedSha256);
        ExecutionEvidenceValidation.Hash(observedSha256);
        if (expectedSha256 != observedSha256) throw new ArgumentException("Observed digest differs from incorporation.");
        ExecutionEvidenceValidation.Text(originalFileName, 255);
        ExecutionEvidenceValidation.Text(contentType, 100);
        ExecutionEvidenceValidation.Text(storageKey, 500);
        ExecutionEvidenceValidation.Date(fileCreatedAtUtc);
        ExecutionEvidenceValidation.Date(storedEvidenceAtUtc);
        return new()
        {
            EvidenceId = evidenceId,
            FileId = fileId,
            Ordinal = ordinal,
            OriginalFileName = originalFileName,
            ContentType = contentType,
            StorageKey = storageKey,
            FileCreatedAtUtc = fileCreatedAtUtc,
            DeclaredLengthBytes = declaredLengthBytes,
            ExpectedSha256 = expectedSha256,
            ExpectedLengthBytes = expectedLengthBytes,
            StoredEvidenceAtUtc = storedEvidenceAtUtc,
            StoredEvidenceVersion = storedEvidenceVersion,
            ObservedSha256 = observedSha256,
            ObservedLengthBytes = observedLengthBytes,
        };
    }
}

internal static class ExecutionEvidenceValidation
{
    internal static void Id(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("An identity is required.");
    }
    internal static void Hash(string value)
    {
        if (value is null || value.Length != 64 || value.Any(c => !(c is >= 'A' and <= 'F' or >= '0' and <= '9')))
            throw new ArgumentException("An uppercase ASCII SHA256 digest is required.");
    }
    internal static void Text(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
            throw new ArgumentException("Document metadata is missing or exceeds its limit.");
    }
    internal static void Date(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value.Ticks % 10 != 0)
            throw new ArgumentException("UTC with exact PostgreSQL microsecond precision is required.");
    }
    internal static string Format(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
}

