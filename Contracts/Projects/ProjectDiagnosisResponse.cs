namespace Contracts.Projects;

public sealed record ProjectDiagnosisResponse(string Status, ProjectDiagnosisDetailResponse? Diagnosis);

public sealed record ProjectDiagnosisDetailResponse(
    string Source, string SubmissionId, string SchemaVersion, string InterpretationVersion,
    IReadOnlyList<ProjectDiagnosisEntryResponse> General,
    IReadOnlyList<ProjectDiagnosisEntryResponse> Priorities,
    IReadOnlyList<ProjectDiagnosisEntryResponse> Preferences,
    IReadOnlyList<ProjectDiagnosisEntryResponse> LivingContext,
    IReadOnlyList<ProjectDiagnosisEntryResponse> Needs,
    IReadOnlyList<ProjectDiagnosisEntryResponse> Decision,
    IReadOnlyList<ProjectDiagnosisEntryResponse> SupplementalNotes);

public sealed record ProjectDiagnosisEntryResponse(
    string QuestionId, string DisplayLabel, string Availability,
    string? Code, IReadOnlyList<string>? Codes, decimal? NumericValue, string? TextValue,
    string? DisplayValue, IReadOnlyList<string>? DisplayValues);
