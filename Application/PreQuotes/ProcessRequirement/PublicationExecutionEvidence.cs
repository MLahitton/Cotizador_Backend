using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

// The envelope carries the complete persisted evidence, not just its stored digest.
public static class PublicationExecutionEvidence
{
    public static string CanonicalJson(RequirementExecutionEvidence evidence)
    {
        var rebuilt = Rebuild(evidence);
        if (rebuilt.ManifestHash != evidence.ManifestHash)
            throw new ArgumentException("Invalid evidence manifest hash.");
        var node = JsonSerializer.SerializeToNode(rebuilt)!.AsObject();
        SetDate(node, "VerifiedAtUtc", rebuilt.VerifiedAtUtc);
        SetDate(node, "RegisteredAtUtc", rebuilt.RegisteredAtUtc);
        foreach (var item in node["Files"]!.AsArray())
        {
            var file = item!.AsObject();
            SetDate(file, "FileCreatedAtUtc", file["FileCreatedAtUtc"]!.GetValue<DateTimeOffset>());
            SetDate(file, "StoredEvidenceAtUtc", file["StoredEvidenceAtUtc"]!.GetValue<DateTimeOffset>());
        }
        return PublicationCanonicalJson.Canonicalize(node.ToJsonString());
    }

    private static void SetDate(JsonObject node, string name, DateTimeOffset value) =>
        node[name] = value.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);

    public static RequirementExecutionEvidence Rebuild(RequirementExecutionEvidence e) =>
        RequirementExecutionEvidence.Create(e.Id, e.RequirementId, e.JobId, e.ExecutionRequestId,
            e.ProcessingAttemptId, e.LeaseGeneration, e.ActorUserId, e.PrimaryDocumentId, e.ManifestVersion,
            e.VerifiedAtUtc, e.RegisteredAtUtc, e.FileCount, e.Files.Select(f =>
                RequirementExecutionDocument.Create(f.EvidenceId, f.FileId, f.Ordinal, f.OriginalFileName,
                    f.ContentType, f.StorageKey, f.FileCreatedAtUtc, f.DeclaredLengthBytes, f.ExpectedSha256,
                    f.ExpectedLengthBytes, f.StoredEvidenceAtUtc, f.StoredEvidenceVersion,
                    f.ObservedSha256, f.ObservedLengthBytes)));
}
