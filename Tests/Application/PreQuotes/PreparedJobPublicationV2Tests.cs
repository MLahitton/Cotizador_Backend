using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json;
using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Domain.PreQuotes;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class PreparedJobPublicationV2Tests
{
    // Structural vector only. Runtime provenance is tested through the real D.2c.5a consumer in PostgreSQL tests.
    private static PreparedJobPublication Snapshot(RequirementExecutionEvidence evidence, string? mismatch = null)
    {
        var input = PreparedJobPublicationTests.Input(true);
        var f = evidence.Files[0];
        var payload = JsonNode.Parse(input.Extraction.Payload.GetRawText())!;
        payload["documentId"] = f.FileId;
        payload["processingAttemptId"] = evidence.ProcessingAttemptId;
        input = input with { PrimaryDocumentId = f.FileId,
            Manifest = [new(f.FileId, f.OriginalFileName, f.ContentType, f.DeclaredLengthBytes, f.StorageKey, f.FileCreatedAtUtc)],
            Extraction = input.Extraction with { Payload = JsonSerializer.SerializeToElement(payload) } };
        var response = new DocumentProcessingResponseData("3.0", mismatch == "document" ? Guid.NewGuid() : f.FileId,
            mismatch == "attempt" ? Guid.NewGuid() : evidence.ProcessingAttemptId,
            DocumentProcessingOutcome.Completed, null!, [], [], null!, input.Extraction.Payload.GetRawText());
        if (mismatch == "manifest")
            input = input with { Manifest = [input.Manifest[0] with { OriginalFileName = "other.pdf" }] };
        var bound = (VerifiedExecutionDocumentResult)Activator.CreateInstance(typeof(VerifiedExecutionDocumentResult),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [evidence, response], null)!;
        return (PreparedJobPublication)typeof(PreparedJobPublication).GetMethod("FromVerifiedResult", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [PreparedJobPublication.Create(input), bound])!;
    }

    [Fact]
    public void V2_LiteralEnvelope_IndependentDigest_AndNoPublicDeserialization()
    {
        const string canonical = """
{"CommercialLine":2,"ContractVersion":"sng-job-publication-v2","Extraction":{"ConflictCount":0,"DurationMs":123,"IssueCount":0,"ItemCount":0,"Items":[],"ItemsRequiringReview":0,"Payload":{"a":[1,2],"documentId":"00000000-0000-0000-0000-000000000007","processingAttemptId":"00000000-0000-0000-0000-000000000005","schemaVersion":"3.0","status":"COMPLETED","structuredExtraction":{"conflicts":[],"issues":[],"items":[],"processingMetadata":{"durationMs":123,"method":"ai2-v1"},"summary":{"itemCount":0,"itemsRequiringReview":0}},"z":1.0},"ProcessingMethod":"ai2-v1","SchemaVersion":"3.0"},"Manifest":[{"ContentType":"application/pdf","CreatedAtUtc":"2026-10-23T12:00:00.123456\u002B00:00","FileId":"00000000-0000-0000-0000-000000000007","OriginalFileName":"a.pdf","SizeBytes":3,"StorageKey":"documents/a"}],"Outcome":1,"PrimaryDocumentId":"00000000-0000-0000-0000-000000000007","Proposal":{"Items":[],"Status":1},"Provider":2,"ResponseOutcome":1,"evidence":{"ActorUserId":"00000000-0000-0000-0000-000000000006","ExecutionRequestId":"00000000-0000-0000-0000-000000000004","FileCount":1,"Files":[{"ContentType":"application/pdf","DeclaredLengthBytes":3,"EvidenceId":"00000000-0000-0000-0000-000000000001","ExpectedLengthBytes":3,"ExpectedSha256":"BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD","FileCreatedAtUtc":"2026-10-23T12:00:00.123456Z","FileId":"00000000-0000-0000-0000-000000000007","ObservedLengthBytes":3,"ObservedSha256":"BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD","Ordinal":0,"OriginalFileName":"a.pdf","StorageKey":"documents/a","StoredEvidenceAtUtc":"2026-10-23T12:00:00.123456Z","StoredEvidenceVersion":1}],"Id":"00000000-0000-0000-0000-000000000001","JobId":"00000000-0000-0000-0000-000000000003","LeaseGeneration":1,"ManifestHash":"40EA9D6F43D9F189485C06557375AD5C76C4FC84317E8D881C5666F196889BC2","ManifestVersion":1,"PrimaryDocumentId":"00000000-0000-0000-0000-000000000007","ProcessingAttemptId":"00000000-0000-0000-0000-000000000005","RegisteredAtUtc":"2026-10-23T12:00:00.123456Z","RequirementId":"00000000-0000-0000-0000-000000000002","VerifiedAtUtc":"2026-10-23T12:00:00.123456Z"}}
""";
        var value = Snapshot(RequirementExecutionEvidenceTests.Build());
        Assert.Equal(canonical, value.CanonicalJson);
        Assert.Equal("CDB22112FDC00A08FBB5E3A72471513C5893F4FF9643B569DC6DD7A862D79215", value.ContentHash);
        Assert.Throws<JsonException>(() => PreparedJobPublication.FromJson(value.CanonicalJson));
        Assert.Empty(typeof(VerifiedExecutionDocumentResult).GetConstructors());
    }

    [Theory]
    [InlineData("id")] [InlineData("verified")] [InlineData("registered")] [InlineData("file")] [InlineData("actor")]
    public void V2_EvidenceIdentityAndTimes_AreCovered(string change)
    {
        var original = Snapshot(RequirementExecutionEvidenceTests.Build());
        var modified = change switch {
            "id" => RequirementExecutionEvidenceTests.Build(id: RequirementExecutionEvidenceTests.Id(8)),
            "verified" => RequirementExecutionEvidenceTests.Build(verified: RequirementExecutionEvidenceTests.At.AddSeconds(1)),
            "registered" => RequirementExecutionEvidenceTests.Build(registered: RequirementExecutionEvidenceTests.At.AddSeconds(1)),
            "file" => RequirementExecutionEvidenceTests.Build([RequirementExecutionEvidenceTests.File(name: "different.pdf")]),
            _ => RequirementExecutionEvidenceTests.Build(actor: RequirementExecutionEvidenceTests.Id(9)) };
        Assert.NotEqual(original.ContentHash, Snapshot(modified).ContentHash);
        Assert.Equal(original.ContentHash, Snapshot(RequirementExecutionEvidenceTests.Build()).ContentHash);
    }

    [Fact]
    public void V2_RequiredVersion_IsExplicitAndCannotDowngrade()
    {
        var job = RequirementProcessingJob.CreateForVerifiedPublication(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.ManualInitial, DateTimeOffset.UnixEpoch);
        Assert.Equal(PreparedJobPublication.Version2, job.RequiredPublicationVersion);
        job.AssignExecution("executor", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));
        job.LinkStartedAttempt(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => job.Succeed(DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => job.SucceedWithPublicationReceipt(PreparedJobPublication.Version, new string('A', 64), DateTimeOffset.UnixEpoch));
        Assert.Equal(RequirementProcessingJobState.Running, job.State);
        job.SucceedWithPublicationReceipt(PreparedJobPublication.Version2, new string('A', 64), DateTimeOffset.UnixEpoch);
        Assert.Equal(RequirementProcessingJobState.Succeeded, job.State);
    }

    [Theory]
    [InlineData("document")] [InlineData("attempt")] [InlineData("manifest")]
    public void V2_IncompatibleResultAndGraph_AreRejected(string mismatch)
    {
        var error = Assert.Throws<TargetInvocationException>(() => Snapshot(RequirementExecutionEvidenceTests.Build(), mismatch));
        Assert.IsType<ArgumentException>(error.InnerException);
    }
}
