using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.Projects;
using Domain.Clients;
using Domain.Projects;
using Domain.PublicExperience;
using Microsoft.Extensions.Options;

namespace Application.PublicExperience;

public sealed class PublicExperienceOptions
{
    public const string SectionName = "PublicExperience";
    public string? OwnerEmail { get; set; }
}

public static class PublicExperienceProcessingErrors
{
    public const string OwnerNotAvailable = "PUBLIC_EXPERIENCE_OWNER_NOT_AVAILABLE";
    public const string LocationInvalid = "PROJECT_LOCATION_INVALID";
    public const string ProcessingFailed = "PUBLIC_EXPERIENCE_PROCESSING_FAILED";
    public const string ProjectCodeConflict = "PROJECT_CODE_CONFLICT";
    public const string UnsupportedSource = "PUBLIC_EXPERIENCE_SOURCE_NOT_SUPPORTED";
}

public sealed record PublicExperienceProcessingResult(
    string SubmissionId, string Status, Guid? ClientId, Guid? ProjectId,
    string? ErrorCode, bool IsReplay = false);

public interface IPublicExperienceProcessingRepository
{
    // Callback and all repository writes share the locked row's database transaction.
    Task<PublicExperienceProcessingResult?> ProcessLockedAsync(
        string submissionId, Func<PublicExperienceSubmission, CancellationToken, Task> process,
        CancellationToken cancellationToken);
}

public sealed class ProcessPublicExperienceSubmissionService(
    IPublicExperienceProcessingRepository submissions,
    IIdentityRepository identities,
    IClientRepository clients,
    IProjectRepository projects,
    IOptions<PublicExperienceOptions> options,
    TimeProvider timeProvider)
{
    public Task<PublicExperienceProcessingResult?> ExecuteAsync(
        string submissionId, CancellationToken cancellationToken) =>
        submissions.ProcessLockedAsync(submissionId, ProcessAsync, cancellationToken);

    private async Task ProcessAsync(PublicExperienceSubmission submission, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        submission.BeginProcessing(now);
        if (submission.Source != "SNG_WEB")
        {
            submission.FailProcessing(PublicExperienceProcessingErrors.UnsupportedSource, now);
            return;
        }
        if (submission.Files.Count == 0)
        {
            submission.FailProcessing(PublicExperienceRequirementPreparationErrors.DocumentsRequired, now);
            return;
        }
        var email = options.Value.OwnerEmail?.Trim().ToLowerInvariant();
        var owner = string.IsNullOrWhiteSpace(email) ? null
            : await identities.FindUserByNormalizedEmailForUpdateAsync(email, cancellationToken);
        if (owner is null || !owner.IsActive)
        {
            submission.FailProcessing(PublicExperienceProcessingErrors.OwnerNotAvailable, now);
            return;
        }
        var location = string.Join(" \u00b7 ",
            new[] { submission.ProjectCity, submission.ProjectSubdivision, submission.ProjectLotOrApartment }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        if (location.Length > 250)
        {
            submission.FailProcessing(PublicExperienceProcessingErrors.LocationInvalid, now);
            return;
        }
        var client = Client.Create(ClientType.Person, submission.ClientFullName,
            null, null, null, null, submission.ClientWhatsapp, null, null, owner.Id, now);
        var code = $"WEB-{submission.Id:N}"[..28].ToUpperInvariant();
        var project = Project.Create(client.Id, code, submission.ProjectName,
            submission.ProjectNotes, location, owner.Id, now);
        clients.Add(client);
        projects.Add(project);
        submission.CompleteProcessing(client.Id, project.Id, now);
        // The processing repository saves all three entities and commits atomically.
    }
}
