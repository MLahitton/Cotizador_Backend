using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.DocumentProcessing;
using Application.PreQuotes.ProcessRequirement;
using CotizadorBackend.Tests.Application.PreQuotes;
using Domain.PreQuotes;
using Domain.Identity;
using Infrastructure.DocumentProcessing;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private async Task<(Seed Seed, PreparedJobPublication Snapshot, VerifiedExecutionDocumentResult Result)> V2(Files files, bool empty = false, bool review = false)
    {
        var seed = await SeedFiles(files);
        var copies = new Copies();
        var package = (await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        var client = new ConsumingClient();
        client.Action = (request, _) => {
            var response = empty ? new Ai2RequirementExtractionAdapter().Adapt("""
                {"requirement":{},"evidence":[],"extraction_metadata":{},"elements":[]}
                """, request) : ConsumingClient.Response(request).Response!;
            if (review)
            {
                var json = System.Text.Json.Nodes.JsonNode.Parse(response.PayloadJson)!;
                json["status"] = "REQUIRES_REVIEW";
                response = response with { Outcome = DocumentProcessingOutcome.RequiresReview, PayloadJson = json.ToJsonString() };
            }
            return Task.FromResult(DocumentProcessingClientResult.Success(response));
        };
        var sent = await Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentConsumptionState.ValidResponse, sent.State);
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
        var prepared = await new PrepareJobPublicationService(new(NullLogger.Instance),
            PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>()))
            .PrepareAsync(sent.Result!, RequirementCommercialLine.Essential, Token);
        Assert.Equal(PreparedJobPublication.Version2, prepared.ContractVersion);
        Assert.Equal(1, client.Calls);
        Assert.Equal(2, files.Opens);
        Assert.Equal(0, files.Deletes);
        Assert.Throws<System.Text.Json.JsonException>(() => PreparedJobPublication.FromJson(prepared.CanonicalJson));
        return (seed, prepared, sent.Result!);
    }

    private Task<JobPublicationResult> PublishV2((Seed Seed, PreparedJobPublication Snapshot, VerifiedExecutionDocumentResult Result) s,
        CancellationToken? token = null, params IInterceptor[] hooks) =>
        new JobPublicationStore(H.Options(hooks)).PublishAsync(s.Seed.Execution, s.Seed.Actor, s.Snapshot, token ?? Token);

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task V2_GraphReceiptReplay_PreservesCommercialChangesAndSuccessor(bool empty, bool review)
    {
        await using var files = new Files(); var s = await V2(files, empty, review);
        var first = await PublishV2(s);
        Assert.Equal(JobPublicationState.Published, first.State);
        await using (var db = fixture.CreateDbContext())
        {
            var job = await db.RequirementProcessingJobs.SingleAsync(Token);
            var attempt = await db.RequirementProcessingAttempts.SingleAsync(Token);
            var old = await db.Requirements.SingleAsync(Token);
            var proposal = await db.RequirementTechnicalProposals.SingleAsync(Token);
            Assert.Equal(s.Snapshot.ContentHash, job.PublicationContentHash);
            Assert.Equal(PreparedJobPublication.Version2, job.PublicationContractVersion);
            Assert.Equal(RequirementStatus.Processed, old.Status);
            Assert.Equal(s.Snapshot.ReadInput().Outcome, attempt.Outcome);
            Assert.Equal(job.FinishedAtUtc, attempt.CompletedAtUtc);
            Assert.Equal(job.FinishedAtUtc, proposal.CreatedAtUtc);
            Assert.Null(proposal.CommercialConfirmedAtUtc);
            Assert.Equal(empty ? 0 : 1, await db.RequirementExtractedItems.CountAsync(Token));
            if (!empty) (await db.RequirementTechnicalProposalItems.SingleAsync(Token)).UpdateManualLocation("Preserve this edit");
            var successor = Requirement.Create(old.PreQuoteId, s.Seed.Actor, RequirementCommercialLine.Signature, await H.Now());
            old.SupersedeBy(successor.Id, await H.Now()); db.Add(successor); await db.SaveChangesAsync(Token);
            await db.Database.ExecuteSqlRawAsync("UPDATE core.projects SET is_active=false; UPDATE core.clients SET is_active=false", Token);
            Assert.False(db.Database.HasPendingModelChanges());
        }
        var before = await new JobPublicationTestSupport(fixture).Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        Assert.Equal(first.Receipt, (await PublishV2(s, hooks: [writes])).Receipt);
        Assert.Equal(0, writes.Count);
        Assert.Equal(before, await new JobPublicationTestSupport(fixture).Snapshot());
        var different = await new PrepareJobPublicationService(new(NullLogger.Instance),
            PrepareJobPublicationTests.Builder(Substitute.For<IRequirementRepository>()))
            .PrepareAsync(s.Result, RequirementCommercialLine.Signature, Token);
        Assert.NotEqual(s.Snapshot.ContentHash, different.ContentHash);
        Assert.Equal(JobPublicationFailure.ResultMismatch, (await PublishV2(s with { Snapshot = different })).Failure);
        Assert.Equal(before, await new JobPublicationTestSupport(fixture).Snapshot());
        var downgraded = s with { Snapshot = PreparedJobPublication.Create(s.Snapshot.ReadInput()) };
        Assert.Equal(JobPublicationFailure.ResultMismatch, (await PublishV2(downgraded)).Failure);
    }

    [Theory]
    [InlineData("foreign")] [InlineData("admin")] [InlineData("inactive-user")]
    [InlineData("inactive-project")] [InlineData("inactive-client")]
    [InlineData("evidence")] [InlineData("missing-detail")] [InlineData("registered-time")]
    [InlineData("attempt")] [InlineData("request")] [InlineData("lease")]
    [InlineData("cancel")] [InlineData("superseded")] [InlineData("incorporation")]
    public async Task V2_Rejections_NoWrites(string mode)
    {
        await using var files = new Files(); var s = await V2(files);
        await using (var db = fixture.CreateDbContext())
        {
            if (mode is "foreign" or "admin")
            {
                var user = User.CreateFromGoogle("foreign-v2@test.local", "Foreign", null, null, await H.Now());
                if (mode == "admin") user.ChangeRole(UserRole.Admin, await H.Now());
                db.Add(user); await db.SaveChangesAsync(Token); s = s with { Seed = s.Seed with { Actor = user.Id } };
            }
            if (mode == "inactive-user") await db.Database.ExecuteSqlRawAsync("UPDATE identity.users SET is_active=false", Token);
            if (mode == "inactive-project") await db.Database.ExecuteSqlRawAsync("UPDATE core.projects SET is_active=false", Token);
            if (mode == "inactive-client") await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active=false", Token);
            if (mode == "evidence") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_execution_documents SET original_file_name='changed.pdf'", Token);
            if (mode == "missing-detail") await db.Database.ExecuteSqlRawAsync("DELETE FROM core.requirement_execution_documents WHERE ordinal=1", Token);
            if (mode == "registered-time") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_execution_evidence SET registered_at_utc=registered_at_utc+interval '1 second'", Token);
            if (mode == "lease") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=clock_timestamp()-interval '1 second'", Token);
            if (mode == "superseded") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirements SET is_active=false", Token);
            if (mode == "incorporation") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_files SET stored_evidence_at_utc=stored_evidence_at_utc+interval '1 second'", Token);
        }
        if (mode == "attempt") s = s with { Seed = s.Seed with { Execution = s.Seed.Execution with { ProcessingAttemptId = Guid.NewGuid() } } };
        if (mode == "request") s = s with { Seed = s.Seed with { Execution = s.Seed.Execution with { ExecutionRequestId = Guid.NewGuid() } } };
        if (mode == "cancel") await H.Call("request", s.Seed);
        var before = await new JobPublicationTestSupport(fixture).Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var result = await PublishV2(s, hooks: [writes]);
        Assert.Equal(JobPublicationState.Conflict, result.State);
        Assert.Equal(0, writes.Count);
        Assert.Equal(before, await new JobPublicationTestSupport(fixture).Snapshot());
        if (mode is "foreign" or "admin") Assert.Equal(JobPublicationFailure.NotFound, result.Failure);
    }

    [Theory]
    [InlineData("same")] [InlineData("cancel-first")] [InlineData("publish-first")]
    public async Task V2_Races_ObservedPostgresLocks(string mode)
    {
        await using var files = new Files(); var s = await V2(files);
        var reached = Signal(); var release = Signal(); var competing = Signal();
        int winner = 0, loser = 0;
        var first = new CommandHook(async c => { winner = ((NpgsqlConnection)c.Connection!).ProcessID; reached.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token); });
        var second = new CommandHook(beforeQuoteLock: c => { loser = ((NpgsqlConnection)c.Connection!).ProcessID; competing.TrySetResult(); });
        Task<JobPublicationResult>? publish1 = null, publish2 = null;
        Task<JobTerminationResult>? cancel = null;
        try
        {
            if (mode == "cancel-first") cancel = H.Call("request", s.Seed, H.Service(first));
            else publish1 = PublishV2(s, hooks: [first]);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            if (mode == "publish-first") cancel = H.Call("request", s.Seed, H.Service(second));
            else publish2 = PublishV2(s, hooks: [second]);
            await competing.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.NotEqual(winner, loser);
            await new JobProtocolTestSupport(fixture).WaitBlocked(loser, winner);
            output.WriteLine($"V2 {mode}: winner={winner}, blocked={loser}; pg_blocking_pids observed");
        }
        finally
        {
            release.TrySetResult();
            if (publish1 is not null) await publish1;
            if (publish2 is not null) await publish2;
            if (cancel is not null) await cancel;
        }
        if (publish1 is not null) Assert.Equal(JobPublicationState.Published, (await publish1).State);
        if (publish2 is not null) Assert.Equal(mode == "same" ? JobPublicationState.AlreadyPublished : JobPublicationState.Conflict, (await publish2).State);
        if (cancel is not null) Assert.Equal(mode == "cancel-first" ? JobTerminationState.CancellationRequested : JobTerminationState.Conflict, (await cancel).State);
        await using var db = fixture.CreateDbContext();
        Assert.Equal(mode == "cancel-first" ? 0 : 1, await db.RequirementTechnicalProposals.CountAsync(Token));
        Assert.Equal(1, await db.RequirementProcessingAttempts.CountAsync(Token));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task V2_CommitResponseLoss_OriginalCancellationAndReplay(bool cancelCaller, bool unavailable)
    {
        await using var files = new Files(); var s = await V2(files);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var committed = false; var observing = Signal(); var release = Signal();
        Guid? writeContext = null, observationContext = null;
        CancellationToken observationToken = default;
        var hook = new JobProtocolTestSupport.AfterCommit(() => { committed = true; throw new IOException("lost response"); });
        var open = new V2Open(async (data, ct) => {
            if (!committed) { writeContext = data.Context!.ContextId.InstanceId; return; }
            observationContext = data.Context!.ContextId.InstanceId; observationToken = ct;
            observing.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            if (unavailable) throw new IOException("observation unavailable");
        });
        var pending = PublishV2(s, caller.Token, hook, open);
        try
        {
            await observing.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.NotNull(writeContext); Assert.NotNull(observationContext);
            Assert.NotEqual(writeContext, observationContext);
            Assert.NotEqual(caller.Token, observationToken); Assert.True(observationToken.CanBeCanceled);
            if (cancelCaller) caller.Cancel();
        }
        finally { release.TrySetResult(); }
        if (cancelCaller)
        {
            var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            Assert.Equal(caller.Token, error.CancellationToken);
        }
        else Assert.Equal(unavailable ? JobPublicationState.Indeterminate : JobPublicationState.AlreadyPublished, (await pending).State);
        Assert.True(committed);
        var before = await new JobPublicationTestSupport(fixture).Snapshot();
        Assert.Equal(JobPublicationState.AlreadyPublished, (await PublishV2(s)).State);
        Assert.Equal(before, await new JobPublicationTestSupport(fixture).Snapshot());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task V2_ErrorOrExpiryAfterSave_RollsBack(bool expire)
    {
        await using var files = new Files(); var s = await V2(files);
        var before = await new JobPublicationTestSupport(fixture).Snapshot();
        var injected = false;
        var result = await PublishV2(s, hooks: [new Saved(async db => {
            injected = true;
            Assert.True(await db.Set<RequirementExtractionResult>().AnyAsync(Token));
            if (!expire) throw new IOException("after writes");
            await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=clock_timestamp()-interval '1 second'", Token);
        })]);
        Assert.True(injected);
        Assert.Equal(expire ? JobPublicationState.Conflict : JobPublicationState.PersistenceError, result.State);
        Assert.Equal(before, await new JobPublicationTestSupport(fixture).Snapshot());
    }

    private sealed class V2Open(Func<ConnectionEventData, CancellationToken, Task> callback) : DbConnectionInterceptor
    {
        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(System.Data.Common.DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        { await callback(eventData, cancellationToken); return result; }
    }

    [Theory]
    [InlineData("evidence")] [InlineData("foreign")] [InlineData("admin")] [InlineData("inactive")]
    public async Task V2_HistoricalRecognition_StillRequiresAuthorityAndExactEvidence(string mode)
    {
        await using var files = new Files(); var s = await V2(files);
        Assert.Equal(JobPublicationState.Published, (await PublishV2(s)).State);
        await using (var db = fixture.CreateDbContext())
        {
            if (mode == "evidence") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_execution_documents SET original_file_name='forged.pdf'", Token);
            else if (mode == "inactive") await db.Database.ExecuteSqlRawAsync("UPDATE identity.users SET is_active=false", Token);
            else
            {
                var actor = User.CreateFromGoogle("historical-v2@test.local", "Foreign", null, null, await H.Now());
                if (mode == "admin") actor.ChangeRole(UserRole.Admin, await H.Now());
                db.Add(actor); await db.SaveChangesAsync(Token);
                s = s with { Seed = s.Seed with { Actor = actor.Id } };
            }
        }
        var before = await new JobPublicationTestSupport(fixture).Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        var result = await PublishV2(s, hooks: [writes]);
        Assert.Equal(JobPublicationState.Conflict, result.State); Assert.Null(result.Receipt);
        Assert.Equal(0, writes.Count); Assert.Equal(before, await new JobPublicationTestSupport(fixture).Snapshot());
    }

    [Fact]
    public async Task V2_CommitNotConfirmed_DoesNotPublishOrRetry()
    {
        await using var files = new Files(); var s = await V2(files);
        var before = await new JobPublicationTestSupport(fixture).Snapshot();
        var injected = false;
        var result = await PublishV2(s, hooks: [new CommitHook(() => { injected = true; throw new IOException("before commit"); }, after: false)]);
        Assert.True(injected); Assert.Equal(JobPublicationState.Indeterminate, result.State);
        Assert.Equal(before, await new JobPublicationTestSupport(fixture).Snapshot());
        Assert.Equal(2, files.Opens); Assert.Equal(0, files.Deletes);
    }

    [Fact]
    public async Task V2_RequiredVersion_RejectsV1BeforeCopyAndWrongStartMode()
    {
        await using var files = new Files();
        var seed = await H.Create(attempt: true);
        var result = await Store(files, new Copies()).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentPreparationFailure.StateConflict, result.Failure);
        Assert.Null(result.Package); Assert.Equal(0, files.Opens);
        Assert.Equal(AssignedJobStartFailure.StateConflict, (await new AssignedJobStartStore(H.Options(),
            RequirementProcessingJob.PublicationV2).StartAsync(seed.Execution, seed.Actor, Token)).Failure);
        seed = await H.Create(verifiedPublication: true);
        Assert.Equal(AssignedJobStartFailure.StateConflict, (await H.StartService().StartAsync(seed.Execution, seed.Actor, Token)).Failure);
        await using var db = fixture.CreateDbContext();
        Assert.Empty(await db.RequirementProcessingAttempts.ToListAsync(Token));
    }

    [Fact]
    public async Task V2_VersionEfAndSqlConstraints_AndV1MigrationHistory()
    {
        var support = new JobPublicationTestSupport(fixture);
        var seed = await support.Create(empty: true, review: false);
        Assert.Equal(JobPublicationState.Published, (await support.Publish(seed)).State);
        var before = await support.Snapshot();
        await using (var db = fixture.CreateDbContext())
        {
            var migrator = db.GetService<IMigrator>();
            try
            {
                await migrator.MigrateAsync("20261023151342_AddPublicExperienceIdeaPhoto", Token);
                await migrator.MigrateAsync(cancellationToken: Token);
            }
            finally { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
            Assert.False(db.Database.HasPendingModelChanges());
        }
        Assert.Equal(before, await support.Snapshot());
        Assert.Equal(JobPublicationState.AlreadyPublished, (await support.Publish(seed)).State);
        var writes = new JobProtocolTestSupport.WriteProbe();
        await using (var db = new global::Infrastructure.Persistence.ApplicationDbContext(H.Options(writes)))
        {
            var job = await db.RequirementProcessingJobs.SingleAsync(Token);
            db.Entry(job).Property(x => x.RequiredPublicationVersion).CurrentValue = PreparedJobPublication.Version2;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
            Assert.Equal(0, writes.Count);
        }
        await using (var db = fixture.CreateDbContext())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                "UPDATE core.requirement_processing_jobs SET required_publication_version='sng-job-publication-v2'", Token));
            Assert.Equal("23514", error.SqlState);
            Assert.Equal("ck_processing_jobs_required_publication", error.ConstraintName);
        }
        Assert.Equal(before, await support.Snapshot());
    }
}
