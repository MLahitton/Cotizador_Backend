using Application.Projects.GetProjectDiagnosis;
using Domain.PreQuotes;

namespace Application.PreQuotes.RequirementExperience;

public static class RequirementExperiencePreferences
{
    public static async Task<IReadOnlyList<RequirementExperienceAnswerResponse>> ReadInitialAsync(
        IProjectDiagnosisSourceRepository? sources, Guid projectId, CancellationToken cancellationToken)
    {
        if (sources is null) return [];
        var rows = await sources.FindByProjectIdAsync(projectId, cancellationToken);
        return FromSources(rows);
    }

    public static IReadOnlyList<RequirementExperienceAnswerResponse> FromSources(
        IReadOnlyList<ProjectDiagnosisSource> sources)
    {
        if (sources.Count != 1 || sources[0].Source != "SNG_WEB") return [];
        var diagnosis = ProjectDiagnosisBuilder.Build(sources[0]).Diagnosis;
        if (diagnosis is null) return [];
        var answers = new List<RequirementExperienceAnswerResponse>();
        foreach (var (key, benefit) in new[] { ("acustico", "ACOUSTIC"), ("termico", "THERMAL"), ("seguridad", "SECURITY"), ("estetica", "AESTHETICS") })
        {
            var entry = diagnosis.Priorities.SingleOrDefault(x => x.QuestionId == key);
            if (entry is { Availability: "AVAILABLE", NumericValue: { } number })
                answers.Add(new(benefit, benefit + (number <= 2 ? "_LOW" : number == 3 ? "_MEDIUM" : "_HIGH")));
        }
        var uv = diagnosis.Priorities.SingleOrDefault(x => x.QuestionId == "uv");
        if (uv is { Availability: "AVAILABLE", Code: "si" or "ns" or "no" })
            answers.Add(new("UV", uv.Code == "no" ? "UV_NO" : "UV_YES"));
        return answers;
    }

    public static RequirementExperienceItemDraftResponse Resolve(Guid itemId,
        RequirementItemExperienceDraft? saved, IReadOnlyList<RequirementExperienceAnswerResponse> initial)
    {
        if (saved is not null) return GetRequirementExperienceDraftsService.MapDraft(itemId, saved);
        if (initial.Count == 0) return GetRequirementExperienceDraftsService.EmptyDraft(itemId);
        return GetRequirementExperienceDraftsService.EmptyDraft(itemId) with
        {
            CatalogVersion = RequirementExperienceCatalog.V4Version,
            Answers = initial,
            Level1Resolution = GetRequirementExperienceDraftsService.MapLevel1Resolution(
                RequirementExperienceCatalog.V4Version, initial),
            PreferenceOrigin = "EXPERIENCE"
        };
    }
}
