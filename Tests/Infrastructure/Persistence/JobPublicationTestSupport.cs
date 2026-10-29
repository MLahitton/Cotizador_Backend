using System.Text.Json;
using System.Text.Json.Nodes;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.Catalogs;
using Domain.PreQuotes;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

internal sealed class JobPublicationTestSupport(PostgreSqlIntegrationFixture fixture)
{
    internal JobTerminationTestSupport H => new(fixture);
    internal sealed record Seed(JobTerminationTestSupport.Seed Execution, PreparedJobPublication Snapshot);
    internal PublishJobResultService Service(params IInterceptor[] hooks) => new(new JobPublicationStore(H.Options(hooks)));
    internal Task<JobPublicationResult> Publish(Seed s, PublishJobResultService? service = null, CancellationToken? token = null) =>
        (service ?? Service()).PublishAsync(s.Execution.Execution, s.Execution.Actor, s.Snapshot, token ?? JobTerminationTestSupport.Token);

    internal async Task<Seed> Create(bool empty = false, bool review = true)
    {
        var s = await H.Create(attempt: true);
        await using var db = fixture.CreateDbContext();
        var at = await H.Now();
        var system = ProductSystem.Create("FIXED", "Fixed", true, true, false, false, at, functionalType: "FIXED", isSelectable: true);
        var glass = GlassType.Create("TEMP_5", "Tempered", null, at, family: "MONOLITHIC", composition: "TEMPERED");
        var finish = FinishType.Create("FINISH", "Finish", false, at);
        db.AddRange(system, glass, finish);
        await db.SaveChangesAsync(JobTerminationTestSupport.Token);
        var f = await db.RequirementFiles.SingleAsync(JobTerminationTestSupport.Token);
        var i = PreparedJobPublicationTests.Input(empty);
        var outcome = review ? DocumentProcessingOutcome.RequiresReview : DocumentProcessingOutcome.Completed;
        var count = empty || !review ? 0 : 1;
        i = i with { Manifest = [new(f.Id, f.OriginalFileName, f.ContentType, f.SizeBytes, f.StorageKey, f.CreatedAtUtc)], PrimaryDocumentId = f.Id,
            ResponseOutcome = outcome, Outcome = outcome,
            Extraction = i.Extraction with { ItemsRequiringReview = count,
                Items = empty ? [] : [i.Extraction.Items[0] with { RequiresReview = review, ReviewReasons = review ? ["REVIEW"] : [] }] },
            Proposal = i.Proposal with { Status = !empty && review ? RequirementTechnicalProposalStatus.RequiresReview : RequirementTechnicalProposalStatus.Completed,
                Items = empty ? [] : [i.Proposal.Items[0] with { RequiresReview = review, ReviewReasons = review ? ["REVIEW"] : [],
                    SuggestedSystemId = system.Id, SuggestedGlassTypeId = glass.Id, SuggestedFinishTypeId = finish.Id,
                    SystemAlternatives = [new(system.Id, 1, .8m, ["SYSTEM"])], GlassAlternatives = [new(glass.Id, 1, .8m, ["GLASS"])],
                    FinishAlternatives = [new(finish.Id, 1, .8m, ["FINISH"])] }] } };
        var payload = JsonNode.Parse(i.Extraction.Payload.GetRawText())!;
        payload["documentId"] = f.Id;
        payload["processingAttemptId"] = s.Execution.ProcessingAttemptId;
        payload["status"] = review ? "REQUIRES_REVIEW" : "COMPLETED";
        payload["structuredExtraction"]!["summary"]!["itemsRequiringReview"] = count;
        if (!empty) payload["structuredExtraction"]!["items"]![0]!["requiresReview"] = review;
        i = i with { Extraction = i.Extraction with { Payload = JsonSerializer.SerializeToElement(payload) } };
        return new(s, PreparedJobPublication.Create(i));
    }

    internal static Seed Different(Seed s)
    {
        var i = s.Snapshot.ReadInput();
        var payload = JsonNode.Parse(i.Extraction.Payload.GetRawText())!;
        payload["auditDifference"] = true;
        return s with { Snapshot = PreparedJobPublication.Create(i with { Extraction = i.Extraction with { Payload = JsonSerializer.SerializeToElement(payload) } }) };
    }

    internal async Task<string> Snapshot()
    {
        await using var db = fixture.CreateDbContext();
        var data = new List<string> { await H.Snapshot() };
        // Names come from EF metadata, never from input. Includes all graph rows, not only counts.
        var types = new[] { typeof(RequirementExtractionResult), typeof(RequirementExtractedItem), typeof(RequirementExtractedItemEvidence),
            typeof(RequirementExtractedItemSegment), typeof(RequirementTechnicalProposal), typeof(RequirementTechnicalProposalItem),
            typeof(RequirementTechnicalProposalSystemAlternative), typeof(RequirementTechnicalProposalGlassAlternative),
            typeof(RequirementTechnicalProposalFinishAlternative), typeof(RequirementTechnicalProposalHistoricalExample) };
        foreach (var type in types)
        {
            var entity = db.Model.FindEntityType(type)!;
            var table = $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
#pragma warning disable EF1002 // Identifiers come exclusively from EF metadata for the fixed type list above.
            data.AddRange(await db.Database.SqlQueryRaw<string>($"SELECT row_to_json(t)::text AS \"Value\" FROM {table} t ORDER BY id").ToListAsync(JobTerminationTestSupport.Token));
#pragma warning restore EF1002
        }
        return JsonSerializer.Serialize(data);
    }
}
