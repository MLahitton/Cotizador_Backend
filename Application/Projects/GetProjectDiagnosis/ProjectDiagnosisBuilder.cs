using System.Globalization;
using System.Text.Json;

namespace Application.Projects.GetProjectDiagnosis;

public static class ProjectDiagnosisBuilder
{
    public static ProjectDiagnosisResult Build(ProjectDiagnosisSource source)
    {
        if (source.SchemaVersion != ProjectDiagnosisSchema.SchemaVersion)
            return new("UNSUPPORTED_SCHEMA");
        try
        {
            using var document = JsonDocument.Parse(source.AnswersJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject()
                .GroupBy(x => x.Name, StringComparer.Ordinal).Any(x => x.Count() > 1))
                return new("INVALID_SOURCE_DATA");

            IReadOnlyList<ProjectDiagnosisEntry> Section(string section) => ProjectDiagnosisSchema.Questions
                .Where(q => q.Section == section).Select(q => Read(q, root)).ToArray();
            return new("AVAILABLE", new(source.Source, source.SubmissionId, source.SchemaVersion,
                ProjectDiagnosisSchema.InterpretationVersion, Section("general"), Section("priorities"),
                Section("preferences"), Section("livingContext"), Section("needs"), Section("decision"),
                Section("supplementalNotes")));
        }
        catch (JsonException) { return new("INVALID_SOURCE_DATA"); }
    }

    private static ProjectDiagnosisEntry Read(DiagnosisQuestion question, JsonElement root)
    {
        var entry = new ProjectDiagnosisEntry(question.Id, question.Label, "NOT_PROVIDED");
        if (ConditionApplies(question.Condition, root) == false)
            return entry with { Availability = "NOT_APPLICABLE" };
        if (!root.TryGetProperty(question.Id, out var value) || value.ValueKind == JsonValueKind.Null
            || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
            || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0))
            return entry;

        if (question.Kind == DiagnosisValueKind.Text && value.ValueKind == JsonValueKind.String)
            return entry with { Availability = "AVAILABLE", TextValue = value.GetString(), DisplayValue = value.GetString() };
        if (question.Kind == DiagnosisValueKind.Scale && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number))
            return entry with { Availability = IsScale(number) ? "AVAILABLE" : "INVALID_VALUE",
                NumericValue = number, DisplayValue = IsScale(number)
                    ? number.ToString("0", CultureInfo.InvariantCulture) + " de 5" : "Valor inválido" };
        if (question.Kind == DiagnosisValueKind.Choice && value.ValueKind == JsonValueKind.String)
        {
            var code = value.GetString()!;
            var option = question.Options.FirstOrDefault(x => x.Code == code);
            return entry with { Availability = option is null ? "UNKNOWN_CODE" : "AVAILABLE",
                Code = code, DisplayValue = option?.Label ?? "Valor no reconocido" };
        }
        if (question.Kind == DiagnosisValueKind.MultiChoice && value.ValueKind == JsonValueKind.Array
            && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String))
        {
            var codes = value.EnumerateArray().Select(x => x.GetString()!).ToArray();
            var invalid = codes.Distinct(StringComparer.Ordinal).Count() != codes.Length
                || codes.Any(string.IsNullOrWhiteSpace)
                || (question.MaxSelections is { } max && codes.Length > max)
                || (question.ExclusiveCode is { } exclusive && codes.Contains(exclusive) && codes.Length > 1);
            var ordered = codes.OrderBy(code =>
                {
                    var index = question.Options.ToList().FindIndex(x => x.Code == code);
                    return index < 0 ? int.MaxValue : index;
                }).ThenBy(code => code, StringComparer.Ordinal).ToArray();
            var labels = ordered.Select(code => question.Options.FirstOrDefault(x => x.Code == code)?.Label).ToArray();
            return entry with { Availability = invalid ? "INVALID_VALUE" : labels.Any(x => x is null) ? "UNKNOWN_CODE" : "AVAILABLE",
                Codes = ordered, DisplayValues = labels.Select(x => x ?? "Valor no reconocido").ToArray() };
        }
        return entry with { Availability = "INVALID_VALUE", DisplayValue = "Valor inválido" };
    }

    private static bool IsScale(decimal number) => number is >= 1 and <= 5 && decimal.Truncate(number) == number;

    private static bool? ConditionApplies(DiagnosisCondition? condition, JsonElement root)
    {
        if (condition is null) return true;
        if (!root.TryGetProperty(condition.ParentId, out var parent)) return null;
        if (condition.Minimum is { } minimum)
            return parent.ValueKind == JsonValueKind.Number && parent.TryGetDecimal(out var number) && IsScale(number)
                ? number >= minimum : null;
        if (parent.ValueKind != JsonValueKind.String) return null;
        var parentQuestion = ProjectDiagnosisSchema.Questions.Single(q => q.Id == condition.ParentId);
        var code = parent.GetString();
        return parentQuestion.Options.Any(x => x.Code == code) ? code == condition.EqualsCode : null;
    }
}
