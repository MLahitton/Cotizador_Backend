namespace Application.Projects.GetProjectDiagnosis;

public sealed record ProjectDiagnosisSource(string SubmissionId, string Source, string SchemaVersion, string AnswersJson);

public interface IProjectDiagnosisSourceRepository
{
    // At most two rows distinguish a unique source from ambiguity.
    Task<IReadOnlyList<ProjectDiagnosisSource>> FindByProjectIdAsync(Guid projectId, CancellationToken cancellationToken);
}

public sealed class ProjectDiagnosisQueryException(Exception innerException)
    : Exception("No fue posible consultar el diagnostico del proyecto.", innerException);

public sealed record ProjectDiagnosisEntry(
    string QuestionId, string DisplayLabel, string Availability,
    string? Code = null, IReadOnlyList<string>? Codes = null,
    decimal? NumericValue = null, string? TextValue = null,
    string? DisplayValue = null, IReadOnlyList<string>? DisplayValues = null);

public sealed record ProjectDiagnosis(
    string Source, string SubmissionId, string SchemaVersion, string InterpretationVersion,
    IReadOnlyList<ProjectDiagnosisEntry> General,
    IReadOnlyList<ProjectDiagnosisEntry> Priorities,
    IReadOnlyList<ProjectDiagnosisEntry> Preferences,
    IReadOnlyList<ProjectDiagnosisEntry> LivingContext,
    IReadOnlyList<ProjectDiagnosisEntry> Needs,
    IReadOnlyList<ProjectDiagnosisEntry> Decision,
    IReadOnlyList<ProjectDiagnosisEntry> SupplementalNotes);

public sealed record ProjectDiagnosisResult(string Status, ProjectDiagnosis? Diagnosis = null);

public enum GetProjectDiagnosisFailure { None, InvalidRequest, Unauthorized, InactiveUser, NotFound, QueryError }

public sealed record GetProjectDiagnosisResult(GetProjectDiagnosisFailure Failure, ProjectDiagnosisResult? Result = null);
