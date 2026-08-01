using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class StructuredExtractionPayloadReaderTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Read_WithValidPayload_ReturnsCompleteTraceability()
    {
        var fixture = CreateFixture();

        var result = Read(fixture);

        Assert.Equal(fixture.DocumentId, fixture.PayloadDocumentId);
        Assert.Equal([1, 2], result.Project.SourcePages);
        Assert.Collection(
            result.Project.Evidence,
            evidence =>
            {
                Assert.Equal(EvidenceSourceType.Native, evidence.SourceType);
                Assert.Equal("Native line 1\nNative line 2", evidence.Text);
            },
            evidence => Assert.Equal(
                EvidenceSourceType.Ocr,
                evidence.SourceType));
        Assert.Equal(
            [
                RequirementCategory.GlassSpecification,
                RequirementCategory.ProfileSpecification,
                RequirementCategory.Finish,
                RequirementCategory.AccessoriesAndSealants,
                RequirementCategory.GeneralNote
            ],
            result.Requirements.Select(value => value.Category));
        Assert.Equal(
            [StructuredIssueCode.OcrReviewRequired],
            result.Items[0].ReviewReasons);
        Assert.Equal([1, 2], result.Items[0].SourcePages);
        Assert.Equal([1, 2], result.DocumentReferences[0].SourcePages);
        Assert.Equal(1, result.Issues[0].Sequence);
        Assert.Equal(1, result.Conflicts[0].Sequence);
    }

    [Theory]
    [InlineData("invalid_json")]
    [InlineData("schema")]
    [InlineData("missing_structured")]
    [InlineData("empty_document_id")]
    [InlineData("different_document_id")]
    [InlineData("empty_attempt_id")]
    [InlineData("different_attempt_id")]
    [InlineData("item_sequence")]
    [InlineData("reference_sequence")]
    [InlineData("requirements_count")]
    [InlineData("issues_count")]
    [InlineData("conflicts_count")]
    [InlineData("summary")]
    [InlineData("normalized_value")]
    [InlineData("source_page")]
    [InlineData("evidence_page")]
    [InlineData("evidence_null")]
    [InlineData("review_reasons_null")]
    [InlineData("missing_required")]
    [InlineData("unknown_property")]
    public void Read_WithInvalidPayload_ThrowsControlledQueryException(
        string scenario)
    {
        var fixture = CreateFixture();
        fixture = fixture with
        {
            PayloadJson = Mutate(fixture.PayloadJson, scenario)
        };

        Assert.Throws<PreQuoteDocumentQueryException>(() => Read(fixture));
    }

    [Theory]
    [InlineData("not_processed", DocumentProcessingAvailability.NotProcessed)]
    [InlineData("pending", DocumentProcessingAvailability.Pending)]
    [InlineData("processing", DocumentProcessingAvailability.Processing)]
    [InlineData("failed", DocumentProcessingAvailability.Failed)]
    [InlineData("legacy", DocumentProcessingAvailability.LegacyOnly)]
    [InlineData("current", DocumentProcessingAvailability.AvailableCurrent)]
    [InlineData("previous", DocumentProcessingAvailability.AvailablePrevious)]
    [InlineData("previous_pending", DocumentProcessingAvailability.AvailablePrevious)]
    [InlineData("previous_processing", DocumentProcessingAvailability.AvailablePrevious)]
    public void ResolveAvailability_ReturnsContractValue(
        string scenario,
        DocumentProcessingAvailability expected)
    {
        var latestId = Guid.NewGuid();
        var extractionId = scenario switch
        {
            "current" => latestId,
            "previous" or "previous_pending" or "previous_processing" =>
                Guid.NewGuid(),
            _ => (Guid?)null
        };
        var latest = scenario == "not_processed"
            ? null
            : CreateAttempt(
                latestId,
                scenario switch
                {
                    "pending" or "previous_pending" =>
                        DocumentProcessingState.Pending,
                    "processing" or "previous_processing" =>
                        DocumentProcessingState.Processing,
                    _ => DocumentProcessingState.Finished
                },
                scenario == "failed" || scenario == "previous"
                    ? DocumentProcessingOutcome.Failed
                    : scenario is "legacy" or "current"
                        ? DocumentProcessingOutcome.Completed
                        : null,
                scenario == "legacy" ? "1.0" : null);

        var result = PreQuoteDocumentQueryRepository.ResolveAvailability(
            latest,
            extractionId);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void PublicContracts_DoNotSerializeSensitiveProperties()
    {
        var list = new global::Contracts.PreQuotes.GetPreQuoteDocumentsResponse(
            [], 1, 20, 0, 0);
        var detail =
            new global::Contracts.PreQuotes.StructuredDocumentExtractionDetailsResponse(
                new global::Contracts.PreQuotes.PreQuoteDocumentResponse(
                    Guid.NewGuid(), Guid.NewGuid(), "a.pdf",
                    "application/pdf", 1, CreatedAt),
                "NOT_PROCESSED",
                null,
                null);

        foreach (var json in new[]
        {
            JsonSerializer.Serialize(list),
            JsonSerializer.Serialize(detail)
        })
        {
            Assert.DoesNotContain("payloadJson", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PreQuoteDocumentQueryRepository.AttemptProjection CreateAttempt(
        Guid id,
        DocumentProcessingState state,
        DocumentProcessingOutcome? outcome,
        string? schemaVersion) =>
        new(
            id,
            state,
            outcome,
            outcome == DocumentProcessingOutcome.Failed ? "FAILED" : null,
            CreatedAt,
            state == DocumentProcessingState.Pending ? null : CreatedAt,
            state == DocumentProcessingState.Finished ? CreatedAt : null,
            schemaVersion is null
                ? null
                : new PreQuoteDocumentQueryRepository.ResultMetadataProjection(
                    schemaVersion,
                    PdfClassification.PdfText,
                    false,
                    2,
                    "pymupdf",
                    1));

    private static StructuredExtractionDetailsReadModel Read(Fixture fixture) =>
        StructuredExtractionPayloadReader.Read(
            fixture.DocumentId,
            fixture.Extraction with { PayloadJson = fixture.PayloadJson },
            fixture.Extraction.ProcessingAttemptId,
            fixture.Items,
            fixture.Requirements,
            fixture.References,
            fixture.Issues,
            fixture.Conflicts,
            []);

    private static Fixture CreateFixture()
    {
        var documentId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var extractionId = Guid.NewGuid();
        var payload = new
        {
            schemaVersion = "2.0",
            documentId,
            processingAttemptId = attemptId,
            status = "REQUIRES_REVIEW",
            document = new { pageCount = 2 },
            pages = Array.Empty<object>(),
            warnings = Array.Empty<object>(),
            processingMetadata = new { method = "pymupdf", durationMs = 10 },
            structuredExtraction = new
            {
                status = "REQUIRES_REVIEW",
                project = new
                {
                    name = "Project",
                    clientName = "Client",
                    location = "Bogota",
                    sourcePages = new[] { 1, 2 },
                    evidence = new[]
                    {
                        new { pageNumber = 1, sourceType = "NATIVE", text = "Native line 1\nNative line 2" },
                        new { pageNumber = 2, sourceType = "OCR", text = "OCR text" }
                    }
                },
                requirements = new
                {
                    glassSpecifications = new[] { Requirement("Glass") },
                    profileSpecifications = new[] { Requirement("Profile") },
                    finishes = new[] { Requirement("Finish") },
                    accessoriesAndSealants = new[] { Requirement("Sealant") },
                    generalNotes = new[] { Requirement("Note") }
                },
                items = new[]
                {
                    new
                    {
                        sequence = 1,
                        reference = "W-01",
                        description = "Window",
                        elementType = "WINDOW",
                        rawMeasurements = "1000x1200",
                        widthMillimeters = (int?)1000,
                        heightMillimeters = (int?)1200,
                        quantity = (int?)2,
                        requiresReview = true,
                        reviewReasons = new[] { "OCR_REVIEW_REQUIRED" },
                        sourcePages = new[] { 1, 2 },
                        evidence = new[] { Evidence(2, "OCR", "Item OCR") }
                    }
                },
                documentReferences = new[]
                {
                    new
                    {
                        sequence = 1,
                        reference = "P-01",
                        description = "Plan",
                        detail = "Detail",
                        quantity = (int?)1,
                        sourcePages = new[] { 1, 2 },
                        evidence = new[] { Evidence(1, "NATIVE", "Reference") }
                    }
                },
                issues = new[]
                {
                    new
                    {
                        code = "OCR_REVIEW_REQUIRED",
                        message = "Review",
                        itemSequence = (int?)1,
                        pageNumbers = new[] { 2 }
                    }
                },
                conflicts = new[]
                {
                    new
                    {
                        code = "DUPLICATE_ITEM_REFERENCE",
                        message = "Conflict",
                        itemSequences = new[] { 1 },
                        pageNumbers = new[] { 1 }
                    }
                },
                summary = new
                {
                    itemCount = 1,
                    documentReferenceCount = 1,
                    itemsRequiringReview = 1,
                    knownQuoteableUnitCount = 2
                },
                processingMetadata = new
                {
                    method = "rule_based_v1",
                    durationMs = 5
                }
            }
        };

        return new Fixture(
            documentId,
            documentId,
            JsonSerializer.Serialize(payload),
            new AvailableExtractionProjection(
                attemptId, Guid.NewGuid(), "2.0", 2, string.Empty, extractionId,
                StructuredExtractionStatus.RequiresReview,
                "Project", "Client", "Bogota", 1, 1, 1, 2,
                null, null,
                "rule_based_v1", 5, CreatedAt),
            [
                new PersistedItem(
                    1, "W-01", "Window", StructuredElementType.Window,
                    "1000x1200", 1000, 1200, 2, true)
            ],
            [
                new PersistedRequirement(1, RequirementCategory.GlassSpecification, "Glass"),
                new PersistedRequirement(2, RequirementCategory.ProfileSpecification, "Profile"),
                new PersistedRequirement(3, RequirementCategory.Finish, "Finish"),
                new PersistedRequirement(4, RequirementCategory.AccessoriesAndSealants, "Sealant"),
                new PersistedRequirement(5, RequirementCategory.GeneralNote, "Note")
            ],
            [new PersistedReference(1, "P-01", "Plan", "Detail", 1)],
            [new PersistedIssue(1, StructuredIssueCode.OcrReviewRequired, "Review", 1, [2])],
            [new PersistedConflict(1, StructuredConflictCode.DuplicateItemReference, "Conflict", [1], [1])]);
    }

    private static object Requirement(string value) => new
    {
        value,
        evidence = new[] { Evidence(1, "NATIVE", value) }
    };

    private static object Evidence(int pageNumber, string sourceType, string text) =>
        new { pageNumber, sourceType, text };

    private static string Mutate(string json, string scenario)
    {
        if (scenario == "invalid_json")
        {
            return "{";
        }

        var root = JsonNode.Parse(json)!.AsObject();
        var structured = root["structuredExtraction"]!.AsObject();

        switch (scenario)
        {
            case "schema": root["schemaVersion"] = "1.0"; break;
            case "missing_structured": root.Remove("structuredExtraction"); break;
            case "empty_document_id": root["documentId"] = Guid.Empty; break;
            case "different_document_id": root["documentId"] = Guid.NewGuid(); break;
            case "empty_attempt_id": root["processingAttemptId"] = Guid.Empty; break;
            case "different_attempt_id": root["processingAttemptId"] = Guid.NewGuid(); break;
            case "item_sequence": structured["items"]![0]!["sequence"] = 2; break;
            case "reference_sequence": structured["documentReferences"]![0]!["sequence"] = 2; break;
            case "requirements_count": structured["requirements"]!["generalNotes"]!.AsArray().Clear(); break;
            case "issues_count": structured["issues"]!.AsArray().Clear(); break;
            case "conflicts_count": structured["conflicts"]!.AsArray().Clear(); break;
            case "summary": structured["summary"]!["itemCount"] = 2; break;
            case "normalized_value": structured["items"]![0]!["description"] = "Changed"; break;
            case "source_page": structured["project"]!["sourcePages"]![1] = 3; break;
            case "evidence_page": structured["project"]!["evidence"]![0]!["pageNumber"] = 3; break;
            case "evidence_null": structured["project"]!["evidence"] = null; break;
            case "review_reasons_null": structured["items"]![0]!["reviewReasons"] = null; break;
            case "missing_required": structured["items"]![0]!.AsObject().Remove("description"); break;
            case "unknown_property": root["unknown"] = true; break;
        }

        return root.ToJsonString();
    }

    private sealed record Fixture(
        Guid DocumentId,
        Guid PayloadDocumentId,
        string PayloadJson,
        AvailableExtractionProjection Extraction,
        IReadOnlyList<PersistedItem> Items,
        IReadOnlyList<PersistedRequirement> Requirements,
        IReadOnlyList<PersistedReference> References,
        IReadOnlyList<PersistedIssue> Issues,
        IReadOnlyList<PersistedConflict> Conflicts);
}
