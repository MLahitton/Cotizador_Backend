using System.Text.Json;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Domain.PreQuotes.ExpiredExecutionCancellationTests;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class ExpiredExecutionCancellationPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private async Task<Input> Seed(int index = 1, bool attempt = false, bool v2 = true, bool succeeded = false)
    {
        var input = Valid(attempt);
        await using var db = fixture.CreateDbContext();
        var owner = User.CreateFromGoogle($"expired-owner{index}@test.local", "Owner", null, null, At);
        var closer = User.CreateFromGoogle($"expired-closer{index}@test.local", "Closer", null, null, At);
        var client = Client.Create(ClientType.Person, "Client", null, null, null, null, null, null, null, owner.Id, At);
        var project = Project.Create(client.Id, $"EXPIRED{index}", "Receipt model", null, null, owner.Id, At);
        var quote = PreQuote.Create(project.Id, owner.Id, $"PC-2026-{index:D4}", null, At);
        var requirement = Requirement.Create(quote.Id, owner.Id, RequirementCommercialLine.Essential, At);
        var job = v2 ? RequirementProcessingJob.CreateForVerifiedPublication(requirement.Id, Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.ManualInitial, At) : RequirementProcessingJob.Create(requirement.Id,
            Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, At);
        const string hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
        var file = RequirementFile.CreateWithStoredEvidence(requirement.Id, "control.pdf", "application/pdf", 3,
            $"tests/receipt-model-{index}-not-opened", At, hash, 3, At, 1);
        Guid? attemptId = null;
        if (attempt || succeeded)
        {
            job.AssignExecution(input.LeaseOwner, input.AssignedAtUtc, input.AssignedAtUtc.AddSeconds(120));
            var processing = RequirementProcessingAttempt.Create(requirement.Id, owner.Id, job.CorrelationId, input.AssignedAtUtc);
            processing.Start(input.AssignedAtUtc); job.LinkStartedAttempt(processing.Id); requirement.StartProcessing(input.AssignedAtUtc);
            attemptId = processing.Id; db.Add(processing);
            if (succeeded)
            {
                var end = input.AssignedAtUtc.AddSeconds(30);
                processing.Complete(DocumentProcessingOutcome.Completed, end); requirement.MarkProcessed(end);
                job.SucceedWithPublicationReceipt(job.RequiredPublicationVersion, new string('A', 64), end);
                var extraction = RequirementExtractionResult.Create(processing.Id, "1", "fixture", "{}", 0, 0, 0, 0, "fixture", 0, end);
                var proposal = RequirementTechnicalProposal.Create(requirement.Id, extraction.Id, processing.Id, false, end);
                var evidenceId = Guid.NewGuid();
                var evidence = RequirementExecutionEvidence.Create(evidenceId, requirement.Id, job.Id, job.ExecutionRequestId,
                    processing.Id, 1, owner.Id, file.Id, 1, input.AssignedAtUtc, end, 1,
                    new[] { RequirementExecutionDocument.Create(evidenceId, file.Id, 0, file.OriginalFileName,
                        file.ContentType, file.StorageKey, At, 3, hash, 3, At, 1, hash, 3) });
                db.AddRange(extraction, proposal, evidence);
            }
        }
        var claim = JobLeaseOperation.CreateClaim(input.ClaimOperationId, job.Id, requirement.Id, job.ExecutionRequestId,
            owner.Id, input.ClaimOperationId, input.LeaseOwner, 1, 1, input.AssignedAtUtc,
            input.AssignedAtUtc, input.AssignedAtUtc.AddSeconds(120));
        var renew = JobLeaseOperation.CreateRenew(Guid.NewGuid(), job.Id, requirement.Id, job.ExecutionRequestId,
            owner.Id, claim.OperationId, claim.LeaseOwner, 1, 1, claim.ExpiresAtUtc, attemptId,
            claim.AssignedAtUtc, claim.AssignedAtUtc.AddSeconds(1), claim.ExpiresAtUtc.AddSeconds(1));
        db.AddRange(owner, closer, client, project, quote, requirement, job, file, claim, renew);
        await db.SaveChangesAsync(Token);
        // Structural history only: no operational Claim/Renew/close or remote publication is performed.
        return input with { JobId = job.Id, RequirementId = requirement.Id, ExecutionRequestId = job.ExecutionRequestId,
            ClosedByActorUserId = closer.Id, CorrelationId = job.CorrelationId, ExpectedProcessingAttemptId = attemptId };
    }

    private async Task Store(ExpiredExecutionCancellation receipt)
    {
        await using var db = fixture.CreateDbContext(); db.Add(receipt); await db.SaveChangesAsync(Token);
    }
    private async Task<string> Receipts()
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(await db.ExpiredExecutionCancellations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(Token));
    }
    private async Task<string> History()
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(new {
            Jobs = await db.RequirementProcessingJobs.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Requirements = await db.Requirements.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Attempts = await db.RequirementProcessingAttempts.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Quotes = await db.PreQuotes.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Files = await db.RequirementFiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).OrderBy(x => x.Id).ToListAsync(Token),
            Extractions = await db.RequirementExtractionResults.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Proposals = await db.RequirementTechnicalProposals.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Token),
            Leases = await db.JobLeaseOperations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(Token) });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Roundtrip_AllFields_DifferentClosingActor_NoBusinessMutation(bool attempt)
    {
        await fixture.ResetAsync(); var a = await Seed(attempt: attempt); await Seed(2, succeeded: true);
        var before = await History(); await Store(a.Make());
        await using var read = fixture.CreateDbContext();
        var receipt = await read.ExpiredExecutionCancellations.AsNoTracking().SingleAsync(Token);
        Assert.Equal(JsonSerializer.Serialize(a.Make()), JsonSerializer.Serialize(receipt));
        Assert.NotEqual(receipt.ClosedByActorUserId,
            (await read.JobLeaseOperations.SingleAsync(x => x.OperationId == a.ClaimOperationId, Token)).ActorUserId);
        Assert.Equal(before, await History());
        Assert.Equal(attempt ? RequirementProcessingJobState.Running : RequirementProcessingJobState.Queued,
            (await read.RequirementProcessingJobs.SingleAsync(x => x.Id == a.JobId, Token)).State);
        Assert.False(read.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task AllowedTemporalEqualities_AndEarlierRequest_PersistWithoutExtraRules()
    {
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed(2);
        a = a with { FinishedAtUtc = a.ExpectedLeaseExpiresAtUtc, GuardAtUtc = a.ExpectedLeaseExpiresAtUtc,
            CancellationRequestedAtUtc = a.ExpectedLeaseExpiresAtUtc };
        b = b with { CancellationRequestedAtUtc = b.AssignedAtUtc.AddDays(-1) };
        var before = await History(); await Store(a.Make()); await Store(b.Make());
        await using var read = fixture.CreateDbContext();
        foreach (var input in new[] { a, b })
            Assert.Equal(JsonSerializer.Serialize(input.Make()), JsonSerializer.Serialize(await read.ExpiredExecutionCancellations
                .AsNoTracking().SingleAsync(x => x.OperationId == input.OperationId, Token)));
        Assert.Equal(before, await History());
    }

    public static IEnumerable<object[]> Properties() => typeof(ExpiredExecutionCancellation).GetProperties()
        .Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Properties))]
    public async Task EfThrow_AllSixteenProperties_NoDml_IndependentRead(string property)
    {
        await fixture.ResetAsync(); var a = await Seed(attempt: true); await Store(a.Make());
        var before = await Receipts(); var graph = await History(); var writes = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(writes).Options);
        var row = await db.ExpiredExecutionCancellations.SingleAsync(Token); var entry = db.Entry(row).Property(property);
        var changed = entry.CurrentValue switch
        {
            Guid => (object)Guid.NewGuid(), string s => s + "x", long n => n + 1,
            short n => (short)(n + 1), DateTimeOffset date => date.AddTicks(10),
            _ => throw new InvalidOperationException(property)
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => { entry.CurrentValue = changed; await db.SaveChangesAsync(Token); });
        Assert.Contains(property, error.Message); Assert.Equal(0, writes.Count);
        Assert.Equal(before, await Receipts()); Assert.Equal(graph, await History());
        output.WriteLine($"EF {property}: InvalidOperationException, DML=0, independent receipt/graph unchanged.");
    }

    private static async Task Reject(ApplicationDbContext db, string sql, string code, string? constraint = null)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token));
        Assert.Equal(code, error.SqlState); if (constraint is not null) Assert.Equal(constraint, error.ConstraintName);
    }

    [Fact]
    public async Task Checks_Nulls_EmptyIds_CanonicalOwner_TemporalOrder_AndFiniteDates()
    {
        await fixture.ResetAsync(); var a = await Seed(); await Store(a.Make());
        var before = await Receipts(); var graph = await History();
        await using var db = fixture.CreateDbContext();
        var required = new[] { "operation_id", "job_id", "requirement_id", "execution_request_id", "claim_operation_id",
            "closed_by_actor_user_id", "correlation_id", "lease_owner", "lease_generation", "assigned_at_utc",
            "expected_lease_expires_at_utc", "cancellation_requested_at_utc", "finished_at_utc", "guard_at_utc", "policy_version" };
        foreach (var column in required)
            await Reject(db, $"UPDATE core.expired_execution_cancellations SET {column}=NULL", "23502");
        foreach (var column in required.Take(7).Append("expected_processing_attempt_id"))
            await Reject(db, $"UPDATE core.expired_execution_cancellations SET {column}='{Guid.Empty}'", "23514");
        foreach (var owner in new[] { "bad", a.LeaseOwner.ToUpperInvariant(), $"exec:{Guid.Empty:D}:{a.ClaimOperationId:D}", $"exec:{Instance:D}:{Guid.NewGuid():D}" })
            await Reject(db, $"UPDATE core.expired_execution_cancellations SET lease_owner='{owner}'", "23514", "ck_expired_cancellation_owner");
        foreach (var change in new[] { "lease_generation=0", "lease_generation=2", "policy_version=0", "policy_version=2",
            "assigned_at_utc=expected_lease_expires_at_utc", "expected_lease_expires_at_utc=finished_at_utc+interval '1 microsecond'",
            "finished_at_utc=guard_at_utc+interval '1 microsecond'", "cancellation_requested_at_utc=finished_at_utc+interval '1 microsecond'" })
            await Reject(db, "UPDATE core.expired_execution_cancellations SET " + change, "23514", "ck_expired_cancellation_shape");
        foreach (var date in new[] { "assigned_at_utc", "expected_lease_expires_at_utc", "cancellation_requested_at_utc", "finished_at_utc", "guard_at_utc" })
            foreach (var infinity in new[] { "infinity", "-infinity" })
                await Reject(db, $"UPDATE core.expired_execution_cancellations SET {date}='{infinity}'", "23514", "ck_expired_cancellation_shape");
        Assert.Equal(before, await Receipts()); Assert.Equal(graph, await History());
    }

    [Fact]
    public async Task PrimaryKeyAndUniqueJob_OnlyWithinThisReceiptTable()
    {
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed(2);
        a = a with { OperationId = a.ClaimOperationId }; await Store(a.Make());
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => Store((b with { OperationId = a.OperationId }).Make()));
        var pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", pg.SqlState); Assert.Equal("pk_expired_execution_cancellations", pg.ConstraintName);
        error = await Assert.ThrowsAsync<DbUpdateException>(() => Store((a with { OperationId = Guid.NewGuid() }).Make()));
        pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", pg.SqlState); Assert.Equal("ux_expired_cancellation_job", pg.ConstraintName);
        await using var read = fixture.CreateDbContext();
        Assert.Single(await read.ExpiredExecutionCancellations.ToListAsync(Token));
        Assert.Equal(4, await read.JobLeaseOperations.CountAsync(Token));
    }

    [Fact]
    public async Task FiveForeignKeys_AndRestrictiveDeletes_WithConstraintsActive()
    {
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed(2); await Store(a.Make());
        await using var db = fixture.CreateDbContext();
        foreach (var pair in new[] { ("job_id", "job"), ("requirement_id", "requirement"),
            ("closed_by_actor_user_id", "actor"), ("expected_processing_attempt_id", "attempt") })
            await Reject(db, $"UPDATE core.expired_execution_cancellations SET {pair.Item1}='{Guid.NewGuid()}'", "23503", $"fk_expired_cancellation_{pair.Item2}");
        var missing = Guid.NewGuid();
        await Reject(db, $"UPDATE core.expired_execution_cancellations SET claim_operation_id='{missing}',lease_owner='exec:{Instance:D}:{missing:D}'", "23503", "fk_expired_cancellation_claim");
        var actions = await db.Database.SqlQuery<string>($"SELECT confdeltype::text AS \"Value\" FROM pg_constraint WHERE conrelid='core.expired_execution_cancellations'::regclass AND contype='f'").ToListAsync(Token);
        Assert.Equal(5, actions.Count); Assert.All(actions, x => Assert.Equal("r", x));
        await Reject(db, $"DELETE FROM identity.users WHERE id='{a.ClosedByActorUserId}'", "23503", "fk_expired_cancellation_actor");
        // Remove only unrelated fixture dependencies to isolate each restrictive FK, never constraints.
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.job_lease_operations WHERE kind='Renew'", Token);
        await Reject(db, $"DELETE FROM core.job_lease_operations WHERE operation_id='{a.ClaimOperationId}'", "23503", "fk_expired_cancellation_claim");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.job_lease_operations WHERE operation_id={b.ClaimOperationId}", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.expired_execution_cancellations SET job_id={b.JobId},requirement_id={b.RequirementId}", Token);
        await Reject(db, $"DELETE FROM core.requirement_processing_jobs WHERE id='{b.JobId}'", "23503", "fk_expired_cancellation_job");
        var pending = RequirementProcessingAttempt.Create(b.RequirementId, b.ClosedByActorUserId, Guid.NewGuid(), At);
        db.Add(pending); await db.SaveChangesAsync(Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.expired_execution_cancellations SET expected_processing_attempt_id={pending.Id}", Token);
        await Reject(db, $"DELETE FROM core.requirement_processing_attempts WHERE id='{pending.Id}'", "23503", "fk_expired_cancellation_attempt");
        await db.Database.ExecuteSqlRawAsync("UPDATE core.expired_execution_cancellations SET expected_processing_attempt_id=NULL", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.requirement_processing_attempts WHERE id={pending.Id}", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.expired_execution_cancellations SET job_id={a.JobId}", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.requirement_processing_jobs WHERE id={b.JobId}", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.requirement_files WHERE requirement_id={b.RequirementId}", Token);
        await Reject(db, $"DELETE FROM core.requirements WHERE id='{b.RequirementId}'", "23503", "fk_expired_cancellation_requirement");
    }

    [Fact]
    public async Task CrossRowCorrespondenceAndDirectSql_AreFutureStoreLimits()
    {
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed(2, attempt: true);
        await using var db = fixture.CreateDbContext();
        var renew = await db.JobLeaseOperations.AsNoTracking().SingleAsync(x => x.JobId == a.JobId && x.Kind == JobLeaseOperationKind.Renew, Token);
        var mismatched = a with { ClaimOperationId = renew.OperationId, LeaseOwner = $"exec:{Instance:D}:{renew.OperationId:D}",
            RequirementId = b.RequirementId, ExecutionRequestId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            ExpectedProcessingAttemptId = b.ExpectedProcessingAttemptId };
        var graph = await History(); await Store(mismatched.Make());
        Assert.Equal(graph, await History()); // Existence is not type Claim, matching job, authority or closure.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.expired_execution_cancellations SET execution_request_id={Guid.NewGuid()}", Token);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM core.expired_execution_cancellations", Token);
        Assert.Empty(await db.ExpiredExecutionCancellations.ToListAsync(Token)); Assert.Equal(graph, await History());
    }

    [Fact]
    public async Task ConcurrentInserts_IndependentPids_OneReceipt_NotAnOperationalClose()
    {
        await fixture.ResetAsync(); var a = await Seed(); var before = await History();
        await using var winner = fixture.CreateDbContext(); await using var loser = fixture.CreateDbContext();
        await using var tx = await winner.Database.BeginTransactionAsync(Token);
        winner.Add(a.Make()); await winner.SaveChangesAsync(Token);
        await loser.Database.OpenConnectionAsync(Token);
        var wp = ((NpgsqlConnection)winner.Database.GetDbConnection()).ProcessID;
        var lp = ((NpgsqlConnection)loser.Database.GetDbConnection()).ProcessID; Assert.NotEqual(wp, lp);
        loser.Add((a with { OperationId = Guid.NewGuid() }).Make()); var pending = loser.SaveChangesAsync(Token);
        try
        {
            await new JobProtocolTestSupport(fixture).WaitBlocked(lp, wp); Assert.False(pending.IsCompleted);
            output.WriteLine($"Expired receipt INSERT winner PID={wp}, blocked PID={lp}; pg_blocking_pids observed.");
            await tx.CommitAsync(Token);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => pending);
            var pg = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal("23505", pg.SqlState); Assert.Equal("ux_expired_cancellation_job", pg.ConstraintName);
        }
        finally { await tx.DisposeAsync(); try { await pending; } catch (DbUpdateException) { } }
        await using var read = fixture.CreateDbContext();
        Assert.Single(await read.ExpiredExecutionCancellations.ToListAsync(Token)); Assert.Equal(before, await History());
        Assert.Equal(RequirementProcessingJobState.Queued, (await read.RequirementProcessingJobs.SingleAsync(Token)).State);
        Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
    }

    [Fact]
    public async Task UpgradeFromImmediatePreviousSchema_HistoryAndLeaseReceiptsIntact_NewTableEmpty()
    {
        await fixture.ResetAsync(); await using var db = fixture.CreateDbContext(); var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync("20261026165918_AddJobLeaseOperations", Token);
            await Seed(1, v2: false); await Seed(2); await Seed(3, v2: false, succeeded: true); await Seed(4, succeeded: true);
            var before = await History();
            await migrator.MigrateAsync(cancellationToken: Token);
            Assert.Equal(before, await History()); Assert.Empty(await db.ExpiredExecutionCancellations.ToListAsync(Token));
            Assert.Equal(8, await db.JobLeaseOperations.CountAsync(Token)); Assert.False(db.Database.HasPendingModelChanges());
            var jobs = await db.RequirementProcessingJobs.AsNoTracking().ToListAsync(Token);
            Assert.Equal(2, jobs.Count(x => x.State == RequirementProcessingJobState.Queued));
            var terminal = jobs.Where(x => x.State == RequirementProcessingJobState.Succeeded).ToList(); Assert.Equal(2, terminal.Count);
            Assert.All(terminal, j => { Assert.Equal(j.RequiredPublicationVersion, j.PublicationContractVersion); Assert.Equal(new string('A', 64), j.PublicationContentHash); });
        }
        finally { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
    }
}
