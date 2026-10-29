using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.PreQuotes;

namespace Application.PreQuotes.ProcessRequirement;

public sealed class PreparedJobPublication
{
    public const string Version = "sng-job-publication-v1";
    public const string Version2 = RequirementProcessingJob.PublicationV2;
    public string ContractVersion { get; }
    public RequirementExecutionEvidence? Evidence { get; }
    public string CanonicalJson { get; }
    public string ContentHash { get; }

    private PreparedJobPublication(string canonicalJson, RequirementExecutionEvidence? evidence = null)
    {
        Evidence = evidence;
        ContractVersion = evidence is null ? Version : Version2;
        CanonicalJson = canonicalJson;
        ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }

    public static PreparedJobPublication Create(PublicationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return FromJson(JsonSerializer.Serialize(input, PublicationCanonicalJson.Options));
    }

    public static PreparedJobPublication FromJson(string json)
    {
        var canonical = PublicationCanonicalJson.Canonicalize(json);
        var input = JsonSerializer.Deserialize<PublicationInput>(canonical, PublicationCanonicalJson.Options)
            ?? throw new ArgumentException("Publication is required.");
        PublicationInputValidation.Validate(input);
        // Re-serialization gives typed numbers one representation; raw payload numbers remain verbatim.
        return new(PublicationCanonicalJson.Canonicalize(JsonSerializer.Serialize(input, PublicationCanonicalJson.Options)));
    }

    // Callers receive their own graph, never a reference into the frozen publication.
    public PublicationInput ReadInput()
    {
        if (Evidence is null) return JsonSerializer.Deserialize<PublicationInput>(CanonicalJson, PublicationCanonicalJson.Options)!;
        var node = JsonNode.Parse(CanonicalJson)!.AsObject();
        node.Remove("evidence");
        node["ContractVersion"] = Version;
        return node.Deserialize<PublicationInput>(PublicationCanonicalJson.Options)!;
    }

    internal static PreparedJobPublication FromVerifiedResult(PreparedJobPublication graph, VerifiedExecutionDocumentResult result)
    {
        if (graph.ContractVersion != Version) throw new ArgumentException("A v1 business graph is required.");
        var evidence = PublicationExecutionEvidence.Rebuild(result.Evidence);
        if (evidence.ManifestHash != result.Evidence.ManifestHash) throw new ArgumentException("Invalid evidence.");
        var input = graph.ReadInput();
        var response = result.ReadResponse();
        var manifest = evidence.Files.Select(f => new PublicationFile(f.FileId, f.OriginalFileName, f.ContentType,
            f.DeclaredLengthBytes, f.StorageKey, f.FileCreatedAtUtc));
        if (response.ProcessingAttemptId != evidence.ProcessingAttemptId || response.DocumentId != evidence.PrimaryDocumentId
            || input.PrimaryDocumentId != evidence.PrimaryDocumentId || !input.Manifest.SequenceEqual(manifest)
            || PublicationCanonicalJson.Canonicalize(input.Extraction.Payload.GetRawText()) != PublicationCanonicalJson.Canonicalize(response.PayloadJson))
            throw new ArgumentException("Publication differs from the verified result.");
        var node = JsonNode.Parse(graph.CanonicalJson)!.AsObject();
        node["ContractVersion"] = Version2;
        node["evidence"] = JsonNode.Parse(PublicationExecutionEvidence.CanonicalJson(evidence));
        return new(PublicationCanonicalJson.Canonicalize(node.ToJsonString()), evidence);
    }
}
