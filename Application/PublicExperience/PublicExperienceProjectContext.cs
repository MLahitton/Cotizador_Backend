using System.Text.Json;

namespace Application.PublicExperience;

public interface IPublicExperienceProjectContextRepository
{
    Task<PublicExperienceProjectContext?> GetProjectContextByProjectIdAsync(
        Guid projectId, CancellationToken cancellationToken);
}

public sealed record PublicExperienceProjectContext(
    string? ProjectType, string? ProjectTypeOther, string? InstallationWindow)
{
    public static PublicExperienceProjectContext FromAnswers(string answersJson)
    {
        using var document = JsonDocument.Parse(answersJson);
        string? Read(string key) => document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var projectType = Read("tipo");
        return new(projectType, projectType == "otro" ? Read("tipo_otro") : null,
            Read("ventana_ejecucion"));
    }
}
