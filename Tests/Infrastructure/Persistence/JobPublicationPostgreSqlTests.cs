using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed class JobPublicationPostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private JobPublicationTestSupport H => new(fixture);

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task PublishesWholeGraph_AndReplayDoesNotWrite(bool empty, bool review)
    {
        var s = await H.Create(empty, review);
        var result = await H.Publish(s);
        Assert.Equal(JobPublicationState.Published, result.State);
        await using var db = fixture.CreateDbContext();
        var job = await db.RequirementProcessingJobs.SingleAsync(Token);
        var attempt = await db.RequirementProcessingAttempts.SingleAsync(Token);
        var requirement = await db.Requirements.SingleAsync(Token);
        var quote = await db.PreQuotes.SingleAsync(Token);
        var extraction = await db.RequirementExtractionResults.SingleAsync(Token);
        var proposal = await db.RequirementTechnicalProposals.Include(x => x.Items).SingleAsync(Token);
        Assert.Equal(RequirementStatus.Processed, requirement.Status);
        Assert.Equal(DocumentProcessingState.Finished, attempt.ProcessingState);
        Assert.Equal(review ? DocumentProcessingOutcome.RequiresReview : DocumentProcessingOutcome.Completed, attempt.Outcome);
        Assert.Equal(job.FinishedAtUtc, attempt.CompletedAtUtc);
        Assert.Equal(job.FinishedAtUtc, quote.UpdatedAtUtc);
        Assert.Equal(job.FinishedAtUtc, extraction.CreatedAtUtc);
        Assert.Equal(job.FinishedAtUtc, proposal.CreatedAtUtc);
        Assert.Equal(s.Snapshot.ContentHash, job.PublicationContentHash);
        Assert.Equal(extraction.Id, proposal.RequirementExtractionResultId);
        Assert.Equal(1, job.AttemptCount);
        Assert.Null(job.LeaseOwner);
        Assert.Null(proposal.CommercialConfirmedAtUtc);
        Assert.Equal(1, proposal.CommercialRevision);
        Assert.Equal(empty ? 0 : 1, proposal.Items.Count);
        Assert.Equal(empty ? 0 : 1, await db.RequirementExtractedItems.CountAsync(Token));
        foreach (var type in new[] { typeof(RequirementExtractedItemEvidence), typeof(RequirementExtractedItemSegment),
            typeof(RequirementTechnicalProposalSystemAlternative), typeof(RequirementTechnicalProposalGlassAlternative),
            typeof(RequirementTechnicalProposalFinishAlternative), typeof(RequirementTechnicalProposalHistoricalExample) })
        {
            var table = db.Model.FindEntityType(type)!.GetTableName();
#pragma warning disable EF1002 // Identifier comes exclusively from EF metadata for the fixed type list above.
            Assert.Equal(empty ? 0 : 1, await db.Database.SqlQueryRaw<int>($"SELECT count(*)::int AS \"Value\" FROM core.\"{table}\"").SingleAsync(Token));
#pragma warning restore EF1002
        }
        var before = await H.Snapshot();
        var replay = await H.Publish(s);
        Assert.Equal(JobPublicationState.AlreadyPublished, replay.State);
        Assert.Equal(result.Receipt, replay.Receipt);
        Assert.Equal(before, await H.Snapshot());
        Assert.Equal(JobPublicationFailure.ResultMismatch, (await H.Publish(JobPublicationTestSupport.Different(s))).Failure);
        Assert.Equal(before, await H.Snapshot());
    }

    [Fact]
    public async Task HistoricalReplay_IgnoresCommercialEditsAndActivityAndReplacement()
    {
        var s = await H.Create();
        var first = await H.Publish(s);
        Assert.Equal(JobPublicationState.Published, first.State);
        await using (var db = fixture.CreateDbContext())
        {
            var item = await db.RequirementTechnicalProposalItems.SingleAsync(Token);
            item.UpdateManualLocation("Later location");
            var old = await db.Requirements.SingleAsync(Token);
            var successor = Requirement.Create(old.PreQuoteId, s.Execution.Actor, RequirementCommercialLine.Signature, await H.H.Now());
            old.SupersedeBy(successor.Id, await H.H.Now());
            db.Requirements.Add(successor);
            await db.SaveChangesAsync(Token);
            await db.Database.ExecuteSqlRawAsync("UPDATE core.projects SET is_active = false; UPDATE core.clients SET is_active = false", Token);
        }
        var before = await H.Snapshot();
        Assert.Equal(first.Receipt, (await H.Publish(s)).Receipt);
        Assert.Equal(before, await H.Snapshot());
    }

    [Theory]
    [InlineData("manifest")] [InlineData("line")] [InlineData("expired")] [InlineData("attempt")]
    [InlineData("identity")] [InlineData("cancel")] [InlineData("replaced")] [InlineData("catalog")]
    [InlineData("inactive-project")] [InlineData("inactive-client")]
    [InlineData("inactive-user")] [InlineData("correlation")]
    public async Task InvalidPublication_DoesNotWrite(string scenario)
    {
        var s = await H.Create();
        await using (var db = fixture.CreateDbContext())
        {
            if (scenario == "manifest") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_files SET size_bytes = size_bytes + 1", Token);
            if (scenario == "line") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirements SET commercial_line = 'SIGNATURE'", Token);
            if (scenario == "expired") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET lease_expires_at_utc = clock_timestamp() - interval '1 second'", Token);
            if (scenario == "replaced") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirements SET is_active = false", Token);
            if (scenario == "catalog") await db.Database.ExecuteSqlRawAsync("DELETE FROM core.product_systems", Token);
            if (scenario == "inactive-project") await db.Database.ExecuteSqlRawAsync("UPDATE core.projects SET is_active = false", Token);
            if (scenario == "inactive-client") await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active = false", Token);
            if (scenario == "inactive-user") await db.Database.ExecuteSqlRawAsync("UPDATE identity.users SET is_active = false", Token);
            if (scenario == "correlation") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_attempts SET correlation_id = gen_random_uuid()", Token);
        }
        if (scenario == "attempt") s = s with { Execution = s.Execution with { Execution = s.Execution.Execution with { ProcessingAttemptId = Guid.NewGuid() } } };
        if (scenario == "identity") s = s with { Execution = s.Execution with { Execution = s.Execution.Execution with { ExecutionRequestId = Guid.NewGuid() } } };
        if (scenario == "cancel") await H.H.Call("request", s.Execution);
        var before = await H.Snapshot();
        var result = await H.Publish(s);
        Assert.Equal(scenario == "catalog" ? JobPublicationState.PersistenceError : JobPublicationState.Conflict, result.State);
        Assert.Equal(before, await H.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ForeignActor_IncludingAdmin_IsNotFound(bool admin)
    {
        var s = await H.Create();
        var stranger = CotizadorBackend.Tests.Application.PublicExperience.ProcessPublicExperienceSubmissionTests.Owner();
        await using (var db = fixture.CreateDbContext())
        {
            // Use a distinct email because Owner's fixture email is unique.
            var user = global::Domain.Identity.User.CreateFromGoogle("stranger@example.test", "Stranger", null, null, await H.H.Now());
            user.Activate(await H.H.Now());
            if (admin) user.ChangeRole(global::Domain.Identity.UserRole.Admin, await H.H.Now());
            db.Users.Add(user);
            await db.SaveChangesAsync(Token);
            stranger = user;
        }
        var before = await H.Snapshot();
        var result = await H.Service().PublishAsync(s.Execution.Execution, stranger.Id, s.Snapshot, Token);
        Assert.Equal(JobPublicationFailure.NotFound, result.Failure);
        Assert.Null(result.Receipt);
        Assert.Equal(before, await H.Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task HistoricalMissingReceiptOrInconsistentGraph_IsNotAdopted(bool receipt)
    {
        var s = await H.Create();
        await using (var db = fixture.CreateDbContext())
        {
            var job = await db.RequirementProcessingJobs.SingleAsync(Token);
            var now = await H.H.Now();
            if (receipt) job.SucceedWithPublicationReceipt(s.Snapshot.ContractVersion, s.Snapshot.ContentHash, now);
            else job.Succeed(now);
            await db.SaveChangesAsync(Token);
        }
        var before = await H.Snapshot();
        Assert.Equal(JobPublicationState.Indeterminate, (await H.Publish(s)).State);
        Assert.Equal(before, await H.Snapshot());
    }

    [Theory]
    [InlineData("failed")] [InlineData("cancelled")] [InlineData("foreign-attempt")] [InlineData("successor")]
    public async Task OtherTerminalOrRelatedIdentity_CannotBeAdopted(string scenario)
    {
        var s = await H.Create();
        if (scenario == "failed") await H.H.Call("fail", s.Execution);
        if (scenario == "cancelled") { await H.H.Call("request", s.Execution); await H.H.Call("confirm", s.Execution); }
        if (scenario == "foreign-attempt")
        {
            await using var db = fixture.CreateDbContext();
            var q = await db.PreQuotes.SingleAsync(Token);
            var now = await H.H.Now();
            var otherQuote = PreQuote.Create(q.ProjectId, s.Execution.Actor, "PC-2026-0002", null, now);
            var other = Requirement.Create(otherQuote.Id, s.Execution.Actor, RequirementCommercialLine.Essential, now);
            var otherAttempt = RequirementProcessingAttempt.Create(other.Id, s.Execution.Actor, Guid.NewGuid(), now);
            otherAttempt.Start(now);
            db.AddRange(otherQuote, other, otherAttempt);
            await db.SaveChangesAsync(Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_processing_jobs SET processing_attempt_id = {otherAttempt.Id}", Token);
            s = s with { Execution = s.Execution with { Execution = s.Execution.Execution with { ProcessingAttemptId = otherAttempt.Id } } };
        }
        if (scenario == "successor")
        {
            Assert.Equal(JobPublicationState.Published, (await H.Publish(s)).State);
            await using var db = fixture.CreateDbContext();
            var old = await db.Requirements.SingleAsync(Token);
            var next = Requirement.Create(old.PreQuoteId, s.Execution.Actor, RequirementCommercialLine.Essential, await H.H.Now());
            old.SupersedeBy(next.Id, await H.H.Now());
            db.Requirements.Add(next);
            await db.SaveChangesAsync(Token);
            s = s with { Execution = s.Execution with { Execution = s.Execution.Execution with { RequirementId = next.Id } } };
        }
        var before = await H.Snapshot();
        Assert.Equal(JobPublicationState.Conflict, (await H.Publish(s)).State);
        Assert.Equal(before, await H.Snapshot());
    }
}
