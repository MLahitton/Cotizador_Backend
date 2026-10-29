namespace Domain.PublicExperience;

public sealed class PublicExperienceIdeaPhoto
{
    private PublicExperienceIdeaPhoto() { }
    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public string StoredSha256 { get; private set; } = null!;
    public long StoredLengthBytes { get; private set; }
    public DateTimeOffset StoredEvidenceAtUtc { get; private set; }
    public short StoredEvidenceVersion { get; private set; }
    public short IdentityVersion { get; private set; }

    public static PublicExperienceIdeaPhoto Create(Guid id, Guid submissionId, string fileName,
        string contentType, string storageKey, string sha256, long length, DateTimeOffset evidenceAtUtc)
    {
        if (id == Guid.Empty || submissionId == Guid.Empty
            || string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255
            || fileName.Any(c => char.IsControl(c) || "/\\:<>|\"?*".Contains(c))
            || contentType is not ("image/jpeg" or "image/png")
            || (contentType == "image/png" ? !string.Equals(Path.GetExtension(fileName), ".png", StringComparison.OrdinalIgnoreCase)
                : Path.GetExtension(fileName).ToLowerInvariant() is not (".jpg" or ".jpeg"))
            || string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500
            || sha256 is null || sha256.Length != 64
            || sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F'))
            || length <= 0 || length > 20L * 1024 * 1024 || evidenceAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid idea photo incorporation evidence.");
        return new PublicExperienceIdeaPhoto
        {
            Id = id, SubmissionId = submissionId, OriginalFileName = fileName,
            ContentType = contentType, StorageKey = storageKey, StoredSha256 = sha256,
            StoredLengthBytes = length, StoredEvidenceAtUtc = evidenceAtUtc,
            StoredEvidenceVersion = 1, IdentityVersion = 1
        };
    }
}
