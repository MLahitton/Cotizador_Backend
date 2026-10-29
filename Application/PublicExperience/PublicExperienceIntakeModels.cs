using System.Text.Json;
using Domain.PublicExperience;

namespace Application.PublicExperience;

public sealed record PublicExperiencePayload(
    string SubmissionId, string SchemaVersion, string Source,
    PublicExperienceClient Client, PublicExperienceProject Project,
    JsonElement Answers, List<PublicExperienceFileMetadata> Files);

public sealed record PublicExperienceClient(
    string FullName, string Whatsapp, string AdvisorCode, string AdvisorDisplayName);

public sealed record PublicExperienceProject(
    string Name, string City, string? Subdivision, string? LotOrApartment, string? Notes);

public sealed record PublicExperienceFileMetadata(
    string FileName, string ContentType, long SizeBytes);

public sealed record PublicExperienceUpload(
    string FileName, string ContentType, long SizeBytes, Func<Stream> OpenRead);

public sealed record PublicExperienceIntakeResult(
    int StatusCode, string? SubmissionId = null, string? Status = null,
    bool IsReplay = false, string? Error = null, string? ErrorCode = null);

public interface IPublicExperienceSubmissionRepository
{
    Task<PublicExperienceSubmission?> FindAsync(string submissionId, CancellationToken cancellationToken);
    // Atomic insert including file metadata; false means the unique public ID already exists.
    Task<bool> TryInsertAsync(PublicExperienceSubmission submission, CancellationToken cancellationToken);
}
