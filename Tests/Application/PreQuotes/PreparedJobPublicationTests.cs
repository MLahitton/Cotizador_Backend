using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class PreparedJobPublicationTests
{
    [Fact]
    public void FixedEmptyPublication_HasLiteralCanonicalBytesAndDigest()
    {
        // Literal assembled from the v1 contract; digest computed separately with PowerShell SHA256.
        const string canonical = """
            {"CommercialLine":2,"ContractVersion":"sng-job-publication-v1","Extraction":{"ConflictCount":0,"DurationMs":123,"IssueCount":0,"ItemCount":0,"Items":[],"ItemsRequiringReview":0,"Payload":{"a":[1,2],"documentId":"10000000-0000-0000-0000-000000000001","processingAttemptId":"20000000-0000-0000-0000-000000000001","schemaVersion":"3.0","status":"COMPLETED","structuredExtraction":{"conflicts":[],"issues":[],"items":[],"processingMetadata":{"durationMs":123,"method":"ai2-v1"},"summary":{"itemCount":0,"itemsRequiringReview":0}},"z":1.0},"ProcessingMethod":"ai2-v1","SchemaVersion":"3.0"},"Manifest":[{"ContentType":"application/pdf","CreatedAtUtc":"1970-01-01T00:00:00\u002B00:00","FileId":"10000000-0000-0000-0000-000000000001","OriginalFileName":"source.pdf","SizeBytes":42,"StorageKey":"requirements/source.pdf"}],"Outcome":1,"PrimaryDocumentId":"10000000-0000-0000-0000-000000000001","Proposal":{"Items":[],"Status":1},"Provider":2,"ResponseOutcome":1}
            """;
        const string digest = "57B2056869B3A94C62D746CBB617DAC5E2AE7AE2450940CE8987D62987CABC42";
        var prepared = PreparedJobPublication.Create(Input(empty: true));
        Assert.Equal(canonical, prepared.CanonicalJson);
        Assert.Equal(digest, prepared.ContentHash);
    }

    internal static PublicationInput Input(bool empty = false)
    {
        var file = new PublicationFile(new("10000000-0000-0000-0000-000000000001"), "source.pdf", "application/pdf",
            42, "requirements/source.pdf", DateTimeOffset.UnixEpoch);
        var extracted = new PublicationExtractedItem(1, "Window", StructuredElementType.Window,
            ["REVIEW"], ["FEATURE"], [new(1, EvidenceSourceType.Native, "source", null, null, "e1", .8m, RequirementExtractionValueStatus.Explicit)],
            [new(1, "PRIMARY", 100, 200, 1, null, null, "source", "e1", EvidenceSourceType.Native, 1, null, null, .8m, RequirementExtractionValueStatus.Explicit)],
            Quantity: 1, WidthMillimeters: 100, HeightMillimeters: 200, RequiresReview: true);
        var proposal = new PublicationProposalItem(1, 1, null, "Window", StructuredElementType.Window,
            1, 100, 200, file.FileId, null, null, .8m, .8m, .8m, .8m, true, false, false,
            ["REVIEW"], ["SYSTEM"], ["GLASS"], ["FINISH"], 1, .8m, .8m, "AVAILABLE",
            [new(file.FileId, 1, .8m, ["MATCH"])], [], [],
            [new("candidate", "quote", "reference", .8m, ["width"], ["height"], "explanation")]);
        var input = new PublicationInput(PreparedJobPublication.Version, [file], file.FileId, RequirementCommercialLine.Essential,
            DocumentProcessingProvider.Ai2, empty ? DocumentProcessingOutcome.Completed : DocumentProcessingOutcome.RequiresReview,
            empty ? DocumentProcessingOutcome.Completed : DocumentProcessingOutcome.RequiresReview,
            new("3.0", JsonDocument.Parse("{\"z\":1.0,\"a\":[1,2]}").RootElement.Clone(), empty ? 0 : 1,
                empty ? 0 : 1, 0, 0, "ai2-v1", 123, empty ? [] : [extracted]),
            new(empty ? RequirementTechnicalProposalStatus.Completed : RequirementTechnicalProposalStatus.RequiresReview, empty ? [] : [proposal]));
        return AlignPayload(input);
    }

    private static PublicationInput AlignPayload(PublicationInput input)
    {
        var payload = JsonNode.Parse(input.Extraction.Payload.GetRawText())!;
        payload["schemaVersion"] = input.Extraction.SchemaVersion;
        payload["documentId"] = input.PrimaryDocumentId;
        payload["processingAttemptId"] = "20000000-0000-0000-0000-000000000001";
        payload["status"] = input.ResponseOutcome == DocumentProcessingOutcome.RequiresReview ? "REQUIRES_REVIEW" : "COMPLETED";
        payload["structuredExtraction"] = JsonSerializer.SerializeToNode(new
        {
            items = input.Extraction.Items.Select(i => new { sequence = i.Sequence, requiresReview = i.RequiresReview }),
            issues = Array.Empty<object>(), conflicts = Array.Empty<object>(),
            summary = new { itemCount = input.Extraction.ItemCount, itemsRequiringReview = input.Extraction.ItemsRequiringReview },
            processingMetadata = new { method = input.Extraction.ProcessingMethod, durationMs = input.Extraction.DurationMs }
        });
        return input with { Extraction = input.Extraction with { Payload = JsonSerializer.SerializeToElement(payload) } };
    }

    [Fact]
    public void IndependentConstruction_Culture_AndCallerMutation_CannotChangeSnapshot()
    {
        var input = Input();
        var reasons = new List<string> { "REVIEW" };
        var items = new List<PublicationExtractedItem> { input.Extraction.Items[0] with { ReviewReasons = reasons } };
        input = input with { Extraction = input.Extraction with { Items = items } };
        var snapshot = PreparedJobPublication.Create(input);
        var expected = snapshot.ContentHash;
        reasons[0] = "changed";
        items.Clear();
        Assert.Equal(expected, snapshot.ContentHash);
        Assert.Equal("REVIEW", snapshot.ReadInput().Extraction.Items[0].ReviewReasons[0]);
        var copy = snapshot.ReadInput();
        ((IList<string>)copy.Extraction.Items[0].ReviewReasons)[0] = "mutated copy";
        Assert.Equal("REVIEW", snapshot.ReadInput().Extraction.Items[0].ReviewReasons[0]);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "en-US", "es-CO", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal(expected, PreparedJobPublication.Create(Input()).ContentHash);
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(" { \"z\" : 1.0, \"a\" : [1,2] }", "{\"a\":[1,2],\"z\":1.0}")]
    [InlineData("{\"b\":{\"y\":-0,\"x\":1e2},\"a\":true}", "{\"a\":true,\"b\":{\"x\":1e2,\"y\":-0}}")]
    public void CanonicalVectors_HaveExplicitExpectedBytes(string source, string expected)
    {
        Assert.Equal(expected, PublicationCanonicalJson.Canonicalize(source));
        var input = Input(true);
        PublicationInput WithVector(string vector)
        {
            var payload = JsonNode.Parse(input.Extraction.Payload.GetRawText())!;
            payload["vector"] = JsonNode.Parse(vector);
            return input with { Extraction = input.Extraction with { Payload = JsonSerializer.SerializeToElement(payload) } };
        }
        var a = PreparedJobPublication.Create(WithVector(source));
        var b = PreparedJobPublication.Create(WithVector(expected));
        Assert.Equal(a.CanonicalJson, b.CanonicalJson);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a.CanonicalJson))), a.ContentHash);
    }

    [Fact]
    public void NumericPolicy_PreservesRawLexemes_ButNormalizesTypedDecimalScale()
    {
        var input = Input();
        var a = PreparedJobPublication.Create(input);
        var typed = input with { Proposal = input.Proposal with { Items = [input.Proposal.Items[0] with { OverallConfidence = .8000m }] } };
        Assert.Equal(a.ContentHash, PreparedJobPublication.Create(typed).ContentHash);
        Assert.Contains("\"OverallConfidence\":0.8", a.CanonicalJson);
        var raw = input with { Extraction = input.Extraction with { Payload = JsonDocument.Parse(input.Extraction.Payload.GetRawText().Replace("\"z\":1.0", "\"z\":1")).RootElement.Clone() } };
        Assert.NotEqual(a.ContentHash, PreparedJobPublication.Create(raw).ContentHash);
    }

    [Theory]
    [InlineData("duration")]
    [InlineData("line")]
    [InlineData("manifest")]
    [InlineData("suggestion")]
    [InlineData("reason")]
    [InlineData("evidence")]
    [InlineData("history")]
    [InlineData("array")]
    [InlineData("outcome")]
    public void RelevantChanges_ChangeHash(string change)
    {
        var input = Input(change == "outcome");
        var changed = change switch
        {
            "duration" => input with { Extraction = input.Extraction with { DurationMs = 124 } },
            "line" => input with { CommercialLine = RequirementCommercialLine.Signature },
            "manifest" => input with { Manifest = [input.Manifest[0] with { SizeBytes = 43 }] },
            "suggestion" => input with { Proposal = input.Proposal with { Items = [input.Proposal.Items[0] with { SuggestedGlassTypeId = input.PrimaryDocumentId }] } },
            "reason" => input with { Proposal = input.Proposal with { Items = [input.Proposal.Items[0] with { ReviewReasons = ["OTHER"] }] } },
            "evidence" => input with { Extraction = input.Extraction with { Items = [input.Extraction.Items[0] with { Evidence = [input.Extraction.Items[0].Evidence[0] with { Text = "other" }] }] } },
            "history" => input with { Proposal = input.Proposal with { Items = [input.Proposal.Items[0] with { HistoricalExamples = [input.Proposal.Items[0].HistoricalExamples[0] with { CandidateId = "other" }] }] } },
            "array" => input with { Extraction = input.Extraction with { Payload = JsonDocument.Parse(input.Extraction.Payload.GetRawText().Replace("[1,2]", "[2,1]")).RootElement.Clone() } },
            _ => input with { ResponseOutcome = DocumentProcessingOutcome.RequiresReview, Outcome = DocumentProcessingOutcome.RequiresReview }
        };
        if (change is "duration" or "outcome") changed = AlignPayload(changed);
        Assert.NotEqual(PreparedJobPublication.Create(input).ContentHash, PreparedJobPublication.Create(changed).ContentHash);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("primary")]
    [InlineData("counter")]
    [InlineData("reference")]
    [InlineData("duplicate")]
    [InlineData("outcome")]
    [InlineData("status")]
    [InlineData("quantity")]
    [InlineData("location")]
    [InlineData("rank")]
    [InlineData("text")]
    [InlineData("missing")]
    public void InvalidGraphs_AreRejected(string change)
    {
        var i = Input();
        var invalid = change switch
        {
            "version" => i with { ContractVersion = "future" },
            "primary" => i with { PrimaryDocumentId = Guid.NewGuid() },
            "counter" => i with { Extraction = i.Extraction with { ItemCount = 2 } },
            "reference" => i with { Proposal = i.Proposal with { Items = [i.Proposal.Items[0] with { ExtractedItemSequence = 9 }] } },
            "duplicate" => i with { Extraction = i.Extraction with { Items = [i.Extraction.Items[0], i.Extraction.Items[0]], ItemCount = 2 } },
            "outcome" => i with { Outcome = DocumentProcessingOutcome.Completed },
            "status" => i with { Proposal = i.Proposal with { Status = RequirementTechnicalProposalStatus.Completed } },
            "quantity" => i with { Extraction = i.Extraction with { Items = [i.Extraction.Items[0] with { Quantity = 0 }] } },
            "location" => i with { Extraction = i.Extraction with { Items = [i.Extraction.Items[0] with { Evidence = [i.Extraction.Items[0].Evidence[0] with { PageNumber = null }] }] } },
            "rank" => i with { Proposal = i.Proposal with { Items = [i.Proposal.Items[0] with { SystemAlternatives = [i.Proposal.Items[0].SystemAlternatives[0], i.Proposal.Items[0].SystemAlternatives[0]] }] } },
            "text" => i with { Extraction = i.Extraction with { ProcessingMethod = " padded " } },
            _ => i with { Proposal = null! }
        };
        Assert.ThrowsAny<ArgumentException>(() => PreparedJobPublication.Create(invalid));
    }

    [Fact]
    public void UnknownCommercialFields_AndDuplicateJson_AreRejected_EmptyGraphIsValid()
    {
        var snapshot = PreparedJobPublication.Create(Input(true));
        Assert.Empty(snapshot.ReadInput().Proposal.Items);
        var json = JsonNode.Parse(snapshot.CanonicalJson)!;
        json["Proposal"]!["CommercialConfirmedByUserId"] = Guid.NewGuid();
        Assert.Throws<JsonException>(() => PreparedJobPublication.FromJson(json.ToJsonString()));
        Assert.Throws<ArgumentException>(() => PublicationCanonicalJson.Canonicalize("{\"x\":{\"a\":1,\"a\":2}}"));
        Assert.ThrowsAny<JsonException>(() => PreparedJobPublication.FromJson("not json"));
    }

    [Fact]
    public void ManifestAndReasonArrays_RetainOrder_AndJsonDocumentCanBeDisposed()
    {
        var input = Input();
        var second = input.Manifest[0] with { FileId = new("10000000-0000-0000-0000-000000000002") };
        input = input with { Manifest = [input.Manifest[0], second],
            Proposal = input.Proposal with { Items = [input.Proposal.Items[0] with { ReviewReasons = ["A", "B"] }] } };
        PreparedJobPublication snapshot;
        using (var document = JsonDocument.Parse(input.Extraction.Payload.GetRawText()))
            snapshot = PreparedJobPublication.Create(input with { Extraction = input.Extraction with { Payload = document.RootElement } });
        Assert.Equal(input.PrimaryDocumentId, snapshot.ReadInput().PrimaryDocumentId);
        Assert.NotEqual(snapshot.ContentHash, PreparedJobPublication.Create(input with { Manifest = [second, input.Manifest[0]] }).ContentHash);
        Assert.NotEqual(snapshot.ContentHash, PreparedJobPublication.Create(input with
        { Proposal = input.Proposal with { Items = [input.Proposal.Items[0] with { ReviewReasons = ["B", "A"] }] } }).ContentHash);
    }

    [Theory]
    [InlineData("issue")]
    [InlineData("primary")]
    [InlineData("summary")]
    [InlineData("sourceReview")]
    [InlineData("duration")]
    public void ContradictoryCanonicalEnvelope_IsRejected(string field)
    {
        var input = Input();
        var payload = JsonNode.Parse(input.Extraction.Payload.GetRawText())!;
        switch (field)
        {
            case "issue": payload["structuredExtraction"]!["issues"] = new JsonArray(new JsonObject()); break;
            case "primary": payload["documentId"] = Guid.NewGuid(); break;
            case "summary": payload["structuredExtraction"]!["summary"]!["itemCount"] = 2; break;
            case "sourceReview": payload["structuredExtraction"]!["items"]![0]!["requiresReview"] = false; break;
            case "duration": payload["structuredExtraction"]!["processingMetadata"]!["durationMs"] = 999; break;
        }
        Assert.Throws<ArgumentException>(() => PreparedJobPublication.Create(input with
        { Extraction = input.Extraction with { Payload = JsonSerializer.SerializeToElement(payload) } }));
    }
}
