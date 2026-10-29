using System.Data.Common;
using System.Text.Json;
using Domain.Clients;
using Domain.Identity;
using Domain.PreQuotes;
using Domain.Projects;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Domain.PreQuotes.JobLeaseOperationTests;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class JobLeaseOperationPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private async Task<Input> Seed(int index = 1, bool v2 = true, bool succeeded = false)
    {
        await using var db = fixture.CreateDbContext();
        var user = User.CreateFromGoogle($"lease-owner{index}@test.local", "Owner", null, null, At);
        var actor = User.CreateFromGoogle($"lease-actor{index}@test.local", "Actor", null, null, At);
        var client = Client.Create(ClientType.Person, "Client", null, null, null, null, null, null, null, user.Id, At);
        var project = Project.Create(client.Id, $"LEASE{index}", "Lease model", null, null, user.Id, At);
        var quote = PreQuote.Create(project.Id, user.Id, $"PC-2026-{index:D4}", null, At);
        var requirement = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, At);
        var job = v2 ? RequirementProcessingJob.CreateForVerifiedPublication(requirement.Id, Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.ManualInitial, At) : RequirementProcessingJob.Create(requirement.Id,
            Guid.NewGuid(), Guid.NewGuid(), RequirementProcessingJobTrigger.ManualInitial, At);
        if (succeeded)
        {
            job.AssignExecution("historical-executor", At, At.AddMinutes(5));
            var attempt = RequirementProcessingAttempt.Create(requirement.Id, user.Id, job.CorrelationId, At);
            attempt.Start(At); attempt.Complete(DocumentProcessingOutcome.Completed, At.AddSeconds(30));
            job.LinkStartedAttempt(attempt.Id);
            job.SucceedWithPublicationReceipt(job.RequiredPublicationVersion, new string('A', 64), At.AddSeconds(30));
            requirement.StartProcessing(At); requirement.MarkProcessed(At.AddSeconds(30));
            db.Add(attempt);
        }
        db.AddRange(user, actor, client, project, quote, requirement, job);
        await db.SaveChangesAsync(Token);
        // Structural fixtures, not operational claim, authorization or a publication pipeline.
        return Claim() with { JobId = job.Id, RequirementId = requirement.Id, ExecutionRequestId = job.ExecutionRequestId, ActorUserId = actor.Id };
    }
    private async Task Store(params JobLeaseOperation[] receipts)
    {
        await using var db = fixture.CreateDbContext(); db.AddRange(receipts); await db.SaveChangesAsync(Token);
    }
    private async Task<string> Receipts()
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(await db.JobLeaseOperations.AsNoTracking().OrderBy(x => x.OperationId).ToListAsync(Token));
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
            Evidence = await db.Set<RequirementExecutionEvidence>().AsNoTracking().ToListAsync(Token),
            Extractions = await db.RequirementExtractionResults.AsNoTracking().ToListAsync(Token),
            Proposals = await db.RequirementTechnicalProposals.AsNoTracking().ToListAsync(Token) });
    }

    [Fact]
    public async Task Roundtrip_SelfReference_RenewalsWithoutExtension_AndNoBusinessMutation()
    {
        await fixture.ResetAsync(); var a = await Seed(); var before = await History();
        await Store(a.Make());
        var b = Renew(a); var c = Renew(a, false); var d = Renew(a, false);
        await Store(b.Make(true), c.Make(true), d.Make(true));
        await using var db = fixture.CreateDbContext();
        foreach (var input in new[] { a, b, c, d })
        {
            var row = await db.JobLeaseOperations.AsNoTracking().SingleAsync(x => x.OperationId == input.OperationId, Token);
            Assert.Equal(JsonSerializer.Serialize(input.Make(input != a)), JsonSerializer.Serialize(row));
        }
        Assert.Equal(4, await db.JobLeaseOperations.CountAsync(Token));
        Assert.Equal(before, await History());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Null(db.Model.FindEntityType(typeof(JobLeaseOperation))!.FindProperty(nameof(JobLeaseOperation.IsEffectiveExtension)));
    }

    public static IEnumerable<object[]> Properties() => typeof(JobLeaseOperation).GetProperties()
        .Where(p => p.Name != nameof(JobLeaseOperation.IsEffectiveExtension)).Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Properties))]
    public async Task EfThrow_EveryPersistedProperty_NoUpdate_IndependentRead(string property)
    {
        await fixture.ResetAsync(); var a = await Seed(); await Store(a.Make());
        var b = Renew(a) with { Attempt = Guid.NewGuid() }; await Store(b.Make(true));
        var before = await Receipts(); var writes = new JobProtocolTestSupport.WriteProbe();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(writes).Options);
        var row = await db.JobLeaseOperations.SingleAsync(x => x.OperationId == b.OperationId, Token);
        var entry = db.Entry(row).Property(property);
        var changed = entry.CurrentValue switch
        {
            Guid => (object)Guid.NewGuid(), string s => s + "x", long n => n + 1,
            short n => (short)(n + 1), DateTimeOffset date => date.AddTicks(10),
            JobLeaseOperationKind => JobLeaseOperationKind.Claim, _ => throw new InvalidOperationException(property)
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () => { entry.CurrentValue = changed; await db.SaveChangesAsync(Token); });
        Assert.Contains(property, error.Message); Assert.Equal(0, writes.Count); Assert.Equal(before, await Receipts());
    }

    private static async Task Reject(ApplicationDbContext db, string sql, string code, string? constraint = null)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token));
        Assert.Equal(code, error.SqlState); if (constraint is not null) Assert.Equal(constraint, error.ConstraintName);
    }

    [Fact]
    public async Task Checks_NullsAndInvalidShapes_CannotPassThroughUnknown()
    {
        await fixture.ResetAsync(); var a = await Seed(); await Store(a.Make());
        var b = Renew(a); await Store(b.Make(true));
        await using var db = fixture.CreateDbContext();
        foreach (var input in new[] { a, b })
        {
            var where = $" WHERE operation_id='{input.OperationId}'";
            foreach (var column in new[] { "operation_id", "kind", "job_id", "requirement_id", "execution_request_id", "actor_user_id", "claim_operation_id", "lease_owner", "lease_generation", "policy_version", "assigned_at_utc", "applied_at_utc", "expires_at_utc" })
                await Reject(db, $"UPDATE core.job_lease_operations SET {column}=NULL" + where, "23502");
            foreach (var change in new[] { "kind='Other'", "lease_generation=0", "lease_generation=2", "policy_version=2",
                "expires_at_utc=assigned_at_utc", "applied_at_utc=assigned_at_utc-interval '1 second'", "expires_at_utc=expires_at_utc+interval '1 second'", "expires_at_utc='infinity'" })
                await Reject(db, "UPDATE core.job_lease_operations SET " + change + where, "23514", "ck_lease_operation_shape");
            foreach (var column in new[] { "job_id", "requirement_id", "execution_request_id", "actor_user_id" })
                await Reject(db, $"UPDATE core.job_lease_operations SET {column}='{Guid.Empty}'" + where, "23514", "ck_lease_operation_ids");
            foreach (var owner in new[] { "bad", a.LeaseOwner.ToUpperInvariant(), $"exec:{Guid.Empty:D}:{a.OperationId:D}", $"exec:{Instance:D}:{Guid.NewGuid():D}" })
                await Reject(db, $"UPDATE core.job_lease_operations SET lease_owner='{owner}'" + where, "23514", "ck_lease_operation_owner");
        }
        await Reject(db, $"UPDATE core.job_lease_operations SET expected_lease_expires_at_utc=NULL WHERE operation_id='{b.OperationId}'", "23514", "ck_lease_operation_shape");
        await Reject(db, $"UPDATE core.job_lease_operations SET expected_lease_expires_at_utc=applied_at_utc WHERE operation_id='{b.OperationId}'", "23514", "ck_lease_operation_shape");
        await Reject(db, $"UPDATE core.job_lease_operations SET expected_processing_attempt_id='{Guid.Empty}' WHERE operation_id='{b.OperationId}'", "23514", "ck_lease_operation_ids");
        foreach (var change in new[] { "expected_lease_expires_at_utc=expires_at_utc", $"expected_processing_attempt_id='{Guid.NewGuid()}'", $"expected_lease_expires_at_utc=expires_at_utc,expected_processing_attempt_id='{Guid.NewGuid()}'" })
            await Reject(db, "UPDATE core.job_lease_operations SET " + change + $" WHERE operation_id='{a.OperationId}'", "23514", "ck_lease_operation_shape");
    }

    [Fact]
    public async Task Keys_PartialIndexes_ForeignKeys_AndRestrictiveDeletes()
    {
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed(2); await Store(a.Make());
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => Store(a.Make()));
        var pk = Assert.IsType<PostgresException>(duplicate.InnerException);
        Assert.Equal("23505", pk.SqlState); Assert.Equal("pk_job_lease_operations", pk.ConstraintName);
        var otherId = Guid.NewGuid(); var sameJob = a with { OperationId = otherId, ClaimOperationId = otherId, LeaseOwner = $"exec:{Instance:D}:{otherId:D}" };
        var unique = await Assert.ThrowsAsync<DbUpdateException>(() => Store(sameJob.Make()));
        Assert.Equal("ux_lease_operation_claim_job", Assert.IsType<PostgresException>(unique.InnerException).ConstraintName);
        await Store(Renew(a).Make(true));
        await using var db = fixture.CreateDbContext();
        var indexes = await db.Database.SqlQuery<string>($"SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname='core' AND tablename='job_lease_operations'").ToListAsync(Token);
        Assert.Contains(indexes, x => x.Contains("UNIQUE INDEX ux_lease_operation_claim_owner") && x.Contains("(lease_owner)") && x.Contains("WHERE") && x.Contains("Claim"));
        // Same owner implies same ClaimOperationId/OperationId: PK already rejects the duplicate above.
        foreach (var pair in new[] { ("job_id", "job"), ("requirement_id", "requirement"), ("actor_user_id", "actor") })
            await Reject(db, $"UPDATE core.job_lease_operations SET {pair.Item1}='{Guid.NewGuid()}'", "23503", $"fk_lease_operation_{pair.Item2}");
        var missing = Guid.NewGuid();
        await Reject(db, $"UPDATE core.job_lease_operations SET claim_operation_id='{missing}',lease_owner='exec:{Instance:D}:{missing:D}' WHERE kind='Renew'", "23503", "fk_lease_operation_claim");
        await Reject(db, $"DELETE FROM core.job_lease_operations WHERE operation_id='{a.OperationId}'", "23503", "fk_lease_operation_claim");
        await Reject(db, $"DELETE FROM core.requirement_processing_jobs WHERE id='{a.JobId}'", "23503", "fk_lease_operation_job");
        await Reject(db, $"DELETE FROM identity.users WHERE id='{a.ActorUserId}'", "23503", "fk_lease_operation_actor");
        // Isolate requirement FK from the job's existing FK, without disabling either.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET requirement_id={b.RequirementId}", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.requirement_processing_jobs WHERE id={b.JobId}", Token);
        await Reject(db, $"DELETE FROM core.requirements WHERE id='{b.RequirementId}'", "23503", "fk_lease_operation_requirement");
    }

    [Fact]
    public async Task GlobalPrimaryKeyAcrossKinds_AndDuplicateClaimOwner_AreRejected()
    {
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed(2);
        await Store(a.Make(), b.Make());
        var mixedKind = Renew(b) with { OperationId = a.OperationId };
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => Store(mixedKind.Make(true)));
        Assert.Equal("pk_job_lease_operations", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
        // Canonical owner contains the claim's PK, so it cannot duplicate independently of that PK.
        var sameOwnerOtherJob = a with { JobId = b.JobId, RequirementId = b.RequirementId, ExecutionRequestId = b.ExecutionRequestId };
        error = await Assert.ThrowsAsync<DbUpdateException>(() => Store(sameOwnerOtherJob.Make()));
        var pg = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal("23505", pg.SqlState);
        Assert.Contains(pg.ConstraintName, new[] { "pk_job_lease_operations", "ux_lease_operation_claim_owner", "ux_lease_operation_claim_job" });
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.JobLeaseOperations.CountAsync(Token));
    }

    [Fact]
    public async Task CrossRowAndSqlImmutabilityLimits_AreNotMisrepresented()
    {
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed(2); await Store(a.Make());
        var r = Renew(a); await Store(r.Make(true));
        var next = Renew(a) with { ClaimOperationId = r.OperationId, LeaseOwner = $"exec:{Instance:D}:{r.OperationId:D}",
            JobId = b.JobId, RequirementId = b.RequirementId, ExecutionRequestId = Guid.NewGuid(), ActorUserId = b.ActorUserId, Attempt = Guid.NewGuid() };
        await Store(next.Make(true)); // Simple FK permits referencing a Renew and unrelated valid rows.
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.job_lease_operations SET execution_request_id={Guid.NewGuid()} WHERE operation_id={next.OperationId}", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM core.job_lease_operations WHERE operation_id={next.OperationId}", Token);
        Assert.Equal(2, await db.JobLeaseOperations.CountAsync(Token));
        Assert.All(await db.RequirementProcessingJobs.ToListAsync(Token), j => Assert.Equal(RequirementProcessingJobState.Queued, j.State));
    }

    [Fact]
    public async Task ConcurrentClaimInserts_IndependentPids_OneUniqueWinner_NotOperationalClaim()
    {
        await fixture.ResetAsync(); var a = await Seed();
        await using var winner = fixture.CreateDbContext(); await using var loser = fixture.CreateDbContext();
        await using var tx = await winner.Database.BeginTransactionAsync(Token);
        winner.Add(a.Make()); await winner.SaveChangesAsync(Token);
        await loser.Database.OpenConnectionAsync(Token);
        var wp = ((NpgsqlConnection)winner.Database.GetDbConnection()).ProcessID;
        var lp = ((NpgsqlConnection)loser.Database.GetDbConnection()).ProcessID;
        Assert.NotEqual(wp, lp);
        var id = Guid.NewGuid(); loser.Add((a with { OperationId = id, ClaimOperationId = id, LeaseOwner = $"exec:{Instance:D}:{id:D}" }).Make());
        var pending = loser.SaveChangesAsync(Token);
        try
        {
            await new JobProtocolTestSupport(fixture).WaitBlocked(lp, wp);
            Assert.False(pending.IsCompleted); output.WriteLine($"Lease receipt insert winner PID {wp}; blocked PID {lp}; pg_blocking_pids observed.");
            await tx.CommitAsync(Token);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => pending);
            var pg = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal("23505", pg.SqlState); Assert.Equal("ux_lease_operation_claim_job", pg.ConstraintName);
        }
        finally { await tx.DisposeAsync(); try { await pending; } catch (DbUpdateException) { } }
        await using var read = fixture.CreateDbContext();
        Assert.Single(await read.JobLeaseOperations.ToListAsync(Token));
        Assert.Equal(RequirementProcessingJobState.Queued, (await read.RequirementProcessingJobs.SingleAsync(Token)).State);
        Assert.Empty(await read.RequirementProcessingAttempts.ToListAsync(Token));
    }

    [Fact]
    public async Task UpgradeFromImmediatePreviousSchema_PreservesV1V2HistoryAndPublicationReceipts()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext(); var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync("20261026134938_AddRequiredPublicationVersion", Token);
            await Seed(1, false, true); await Seed(2, true, true); await Seed(3, false); await Seed(4, true);
            var before = await History();
            await migrator.MigrateAsync(cancellationToken: Token);
            Assert.Equal(before, await History()); Assert.Empty(await db.JobLeaseOperations.ToListAsync(Token));
            Assert.False(db.Database.HasPendingModelChanges());
            var historical = await db.RequirementProcessingJobs.AsNoTracking().Where(x => x.State == RequirementProcessingJobState.Succeeded).ToListAsync(Token);
            Assert.Equal(2, historical.Count);
            Assert.All(historical, j => { Assert.Equal(j.RequiredPublicationVersion, j.PublicationContractVersion); Assert.Equal(new string('A',64), j.PublicationContentHash); Assert.Null(j.LeaseOwner); });
            var a = Claim() with { JobId = historical[0].Id, RequirementId = historical[0].RequirementId,
                ExecutionRequestId = historical[0].ExecutionRequestId, ActorUserId = await db.Users.Select(x => x.Id).FirstAsync(Token) };
            await Store(a.Make()); // Historical receipt is not tied to the job's cleared live lease.
        }
        finally { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
    }
}
