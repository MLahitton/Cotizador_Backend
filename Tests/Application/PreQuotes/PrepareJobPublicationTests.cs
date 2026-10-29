using System.Text.Json;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.BuildRequirementTechnicalProposal;
using Application.PreQuotes.ProcessRequirement;
using Application.PreQuotes.ResolveHistoricalTechnicalEvidence;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class PrepareJobPublicationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RealCanonicalAdapterOutput_IsAcceptedWithoutContractChanges(bool empty)
    {
        var (context, _) = Input(empty, false);
        var file = context.Files[0];
        var request = new DocumentProcessingClientRequest(file.FileId, context.ProcessingAttemptId, Guid.NewGuid(),
            [new(file.FileId, file.OriginalFileName, file.ContentType, file.SizeBytes, Stream.Null)]);
        var raw = JsonSerializer.Serialize(new { requirement = new { }, evidence = Array.Empty<object>(), extraction_metadata = new { },
            elements = empty ? Array.Empty<object>() : new object[] { new { name = "Window", quantity = 1, width_mm = 1000, height_mm = 2000 } } });
        var response = new global::Infrastructure.DocumentProcessing.Ai2RequirementExtractionAdapter().Adapt(raw, request);
        var repository = Substitute.For<IRequirementRepository>();
        var prepared = await new PrepareJobPublicationService(new(NullLogger.Instance), Builder(repository))
            .PrepareAsync(context, response, TestContext.Current.CancellationToken);
        Assert.Equal(empty ? 0 : 1, prepared.ReadInput().Extraction.Items.Count);
        Assert.Equal(PublicationCanonicalJson.Canonicalize(response.PayloadJson),
            PublicationCanonicalJson.Canonicalize(prepared.ReadInput().Extraction.Payload.GetRawText()));
        Assert.Empty(repository.ReceivedCalls());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)]
    public async Task PreparationUsesExistingMapperAndBuilder_WithoutPersistence(bool empty, bool invalidEvidence)
    {
        var (context, response) = Input(empty, invalidEvidence);
        var repository = Substitute.For<IRequirementRepository>();
        var builder = Builder(repository);
        var mapper = new RequirementExtractionMapper(NullLogger.Instance);
        var prepared = await new PrepareJobPublicationService(mapper, builder).PrepareAsync(context, response, TestContext.Current.CancellationToken);
        var input = prepared.ReadInput();
        var x = response.StructuredExtraction!;
        var extraction = RequirementExtractionResult.Create(context.ProcessingAttemptId, response.SchemaVersion, response.Provider.ToString(),
            response.PayloadJson, x.ItemCount, x.ItemsRequiringReview, x.Issues.Count, x.Conflicts.Count, x.ProcessingMethod, x.DurationMs, DateTimeOffset.UnixEpoch);
        var mapped = mapper.CreateExtractedItems(context.RequirementId, extraction.Id, x.Items, DateTimeOffset.UnixEpoch);
        var proposal = await builder.BuildAsync(context.RequirementId, context.CommercialLine, extraction, mapped.Select(i => i.Item).ToArray(), TestContext.Current.CancellationToken);
        Assert.Equal(JsonSerializer.Serialize(mapped.Select(i => JobPublicationGraph.Snapshot(i.Item))), JsonSerializer.Serialize(input.Extraction.Items));
        Assert.Equal(JsonSerializer.Serialize(proposal.Items.Select(JobPublicationGraph.Snapshot)), JsonSerializer.Serialize(input.Proposal.Items));
        Assert.Equal(empty ? 0 : 1, input.Extraction.ItemCount);
        if (invalidEvidence)
        {
            Assert.Contains("INVALID_EVIDENCE_LOCATION", input.Extraction.Items[0].ReviewReasons);
            Assert.DoesNotContain(input.Extraction.Items[0].Evidence, e => e.SourceType == EvidenceSourceType.Native && e.PageNumber is null);
            Assert.Equal(DocumentProcessingOutcome.RequiresReview, input.Outcome);
        }
        Assert.Empty(repository.ReceivedCalls());
        // Round-trip all mapped initial fields through publication materialization, excluding generated identities/times.
        foreach (var i in input.Extraction.Items)
            Assert.Equal(JsonSerializer.Serialize(i), JsonSerializer.Serialize(JobPublicationGraph.Snapshot(JobPublicationGraph.Materialize(i, Guid.NewGuid(), DateTimeOffset.UnixEpoch))));
        foreach (var i in input.Proposal.Items)
            Assert.Equal(JsonSerializer.Serialize(i), JsonSerializer.Serialize(JobPublicationGraph.Snapshot(JobPublicationGraph.Materialize(i, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UnixEpoch))));
    }

    [Fact]
    public async Task BuilderFailure_PropagatesWithoutPersistence()
    {
        var (context, response) = Input(false, false);
        var repository = Substitute.For<IRequirementRepository>();
        var systems = Substitute.For<IProductSystemCatalogRepository>();
        systems.ListActiveSelectableAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<ProductSystemCatalogReadModel>>(new IOException("simulated catalog failure")));
        await Assert.ThrowsAsync<IOException>(() => new PrepareJobPublicationService(new(NullLogger.Instance), Builder(repository, systems))
            .PrepareAsync(context, response, TestContext.Current.CancellationToken));
        Assert.Empty(repository.ReceivedCalls());
    }

    internal static BuildRequirementTechnicalProposalService Builder(IRequirementRepository repository, IProductSystemCatalogRepository? systems = null)
    {
        systems ??= Substitute.For<IProductSystemCatalogRepository>();
        var glasses = Substitute.For<IGlassTypeCatalogRepository>();
        var finishes = Substitute.For<IFinishTypeCatalogRepository>();
        var similarity = Substitute.For<IHistoricalSimilarityEvaluationService>();
        similarity.EvaluateBatchAsync(Arg.Any<IReadOnlyList<HistoricalSimilarityBatchQuery>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<string, HistoricalSimilarityEvaluationResult>>(
                call.Arg<IReadOnlyList<HistoricalSimilarityBatchQuery>>()!.ToDictionary(q => q.RequestId,
                    _ => new HistoricalSimilarityEvaluationResult(HistoricalSimilarityStatus.Completed, [], null))));
        return new(repository, systems, glasses, finishes, new GlassCandidateResolver(), new FinishCandidateResolver(),
            new ResolveHistoricalTechnicalEvidenceService(similarity, systems, new DeterministicSgTechnicalSelector(systems)));
    }

    private static (JobPublicationPreparationContext, DocumentProcessingResponseData) Input(bool empty, bool invalidEvidence)
    {
        var file = new PublicationFile(Guid.NewGuid(), "test.pdf", "application/pdf", 10, "test.pdf", DateTimeOffset.UnixEpoch);
        var context = new JobPublicationPreparationContext(Guid.NewGuid(), Guid.NewGuid(), RequirementCommercialLine.Essential, file.FileId, [file]);
        var request = new DocumentProcessingClientRequest(file.FileId, context.ProcessingAttemptId, Guid.NewGuid(),
            [new(file.FileId, file.OriginalFileName, file.ContentType, file.SizeBytes, Stream.Null)]);
        var response = ProcessRequirementServiceTests.CreateResponse(request, invalidEvidenceLocation: invalidEvidence);
        var x = response.StructuredExtraction!;
        x = x with { Items = empty ? [] : x.Items, ItemCount = empty ? 0 : x.Items.Count,
            ItemsRequiringReview = empty ? 0 : x.Items.Count(i => i.RequiresReview) };
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = "3.0", documentId = file.FileId, processingAttemptId = context.ProcessingAttemptId, status = "COMPLETED",
            structuredExtraction = new { items = x.Items.Select(i => new { sequence = i.Sequence, requiresReview = i.RequiresReview }),
                issues = x.Issues, conflicts = x.Conflicts,
                summary = new { itemCount = x.ItemCount, itemsRequiringReview = x.ItemsRequiringReview },
                processingMetadata = new { method = x.ProcessingMethod, durationMs = x.DurationMs } }
        });
        return (context, response with { SchemaVersion = "3.0", PayloadJson = payload, StructuredExtraction = x });
    }
}
