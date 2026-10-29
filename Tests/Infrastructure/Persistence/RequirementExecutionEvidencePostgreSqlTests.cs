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
using static CotizadorBackend.Tests.Domain.PreQuotes.RequirementExecutionEvidenceTests;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class RequirementExecutionEvidencePostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private async Task<RequirementExecutionEvidence> Seed(int index = 1)
    {
        await using var db = fixture.CreateDbContext();
        var user = User.CreateFromGoogle($"execution{index}@test.local", "Owner", null, null, At);
        var client = Client.Create(ClientType.Person, "Client", null, null, null, null, null, null, null, user.Id, At);
        var project = Project.Create(client.Id, $"EXEC{index}", "Evidence", null, null, user.Id, At);
        var quote = PreQuote.Create(project.Id, user.Id, $"PC-2026-{index:D4}", null, At);
        var requirement = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, At);
        var file = RequirementFile.CreateWithStoredEvidence(requirement.Id, "a.pdf", "application/pdf", 3,
            $"documents/{index}", At, Hash, 3, At, 1);
        var attempt = RequirementProcessingAttempt.Create(requirement.Id, user.Id, Guid.NewGuid(), At);
        var job = RequirementProcessingJob.Create(requirement.Id, Guid.NewGuid(), Guid.NewGuid(),
            RequirementProcessingJobTrigger.ManualInitial, At);
        job.AssignExecution("fixture", At, At.AddMinutes(1));
        job.LinkStartedAttempt(attempt.Id);
        db.AddRange(user, client, project, quote, requirement, file, attempt, job);
        await db.SaveChangesAsync(Token);
        // Structural fixture only, not a coordinator or proof of authorization/byte verification.
        var id = Guid.NewGuid();
        return RequirementExecutionEvidence.Create(id, requirement.Id, job.Id, job.ExecutionRequestId,
            attempt.Id, 1, user.Id, file.Id, 1, At, At.AddSeconds(-1), 1,
            new[] { RequirementExecutionDocument.Create(id, file.Id, 0, file.OriginalFileName, file.ContentType,
                file.StorageKey, file.CreatedAtUtc, file.SizeBytes, file.StoredSha256!, file.StoredLengthBytes!.Value,
                file.StoredEvidenceAtUtc!.Value, file.StoredEvidenceVersion!.Value, Hash, 3) });
    }
    private async Task Store(RequirementExecutionEvidence evidence)
    {
        await using var db = fixture.CreateDbContext();
        db.Add(evidence); await db.SaveChangesAsync(Token);
    }
    private async Task<string> Snapshot()
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).OrderBy(x => x.Id).ToListAsync(Token));
    }

    [Fact]
    public async Task Roundtrip_AndEquivalentDocumentsAcrossExecutions()
    {
        await fixture.ResetAsync();
        var a = await Seed(); var b = await Seed(2);
        await Store(a); await Store(b);
        await using var db = fixture.CreateDbContext();
        var read = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).SingleAsync(x => x.Id == a.Id, Token);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(read));
        Assert.Equal(a.CanonicalManifest(), read.CanonicalManifest());
        Assert.Equal(a.ManifestHash, read.ManifestHash);
        Assert.Equal(2, await db.Set<RequirementExecutionEvidence>().CountAsync(Token));
        Assert.False(db.Database.HasPendingModelChanges());
        // Valid direct SQL is outside API/EF immutability and hash recomputation guarantees.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_execution_evidence SET manifest_hash={a.ManifestHash} WHERE id={b.Id}", Token);
        Assert.Equal(2, await db.Set<RequirementExecutionEvidence>().CountAsync(x => x.ManifestHash == a.ManifestHash, Token));
    }

    public static IEnumerable<object[]> Properties()
    {
        foreach (var type in new[] { typeof(RequirementExecutionEvidence), typeof(RequirementExecutionDocument) })
            foreach (var property in type.GetProperties().Where(p => p.Name != "Files"))
                yield return new object[] { type == typeof(RequirementExecutionEvidence), property.Name };
    }

    [Fact]
    public async Task MultipleDocuments_RoundtripRetainsManifestOrder_NotMaterializationOrder()
    {
        await fixture.ResetAsync();
        var first = await Seed();
        await using var db = fixture.CreateDbContext();
        var second = RequirementFile.CreateWithStoredEvidence(first.RequirementId, "b.pdf", "application/pdf", 3,
            "documents/second", At, Hash, 3, At, 1);
        db.Add(second); await db.SaveChangesAsync(Token);
        var evidence = RequirementExecutionEvidence.Create(first.Id, first.RequirementId, first.JobId,
            first.ExecutionRequestId, first.ProcessingAttemptId, 1, first.ActorUserId, first.PrimaryDocumentId,
            1, At, At, 2, new[] { first.Files[0], RequirementExecutionDocument.Create(first.Id, second.Id, 1,
                second.OriginalFileName, second.ContentType, second.StorageKey, At, 3, Hash, 3, At, 1, Hash, 3) });
        await Store(evidence);
        await using var read = fixture.CreateDbContext();
        var header = await read.Set<RequirementExecutionEvidence>().SingleAsync(Token);
        await read.Set<RequirementExecutionDocument>().OrderByDescending(x => x.Ordinal).LoadAsync(Token);
        Assert.Equal(new[] { first.PrimaryDocumentId, second.Id }, header.Files.Select(x => x.FileId));
        Assert.Equal(evidence.CanonicalManifest(), header.CanonicalManifest());
        Assert.Equal(evidence.ManifestHash, header.ManifestHash);
    }
    [Theory]
    [MemberData(nameof(Properties))]
    public async Task EfRejectsAllPersistedPropertyChanges_WithoutUpdate(bool header, string property)
    {
        await fixture.ResetAsync(); await Store(await Seed());
        var before = await Snapshot();
        var counter = new Updates();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(counter).Options);
        var entity = await db.Set<RequirementExecutionEvidence>().Include(x => x.Files).SingleAsync(Token);
        var entry = db.Entry(header ? (object)entity : entity.Files[0]).Property(property);
        var old = entry.CurrentValue!;
        object changed = old switch {
            Guid => Guid.NewGuid(), string => new string('A',64), long n => n + 1,
            int n => n + 1, short n => (short)(n + 1), DateTimeOffset d => d.AddSeconds(1),
            _ => throw new InvalidOperationException()
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            entry.CurrentValue = changed;
            await db.SaveChangesAsync(Token);
        });
        Assert.Contains(property, error.Message);
        Assert.Equal(0, counter.Count);
        Assert.Equal(before, await Snapshot());
    }

    [Fact]
    public async Task DatabaseChecksAndForeignKeys_NameExactConstraint()
    {
        await fixture.ResetAsync(); await Store(await Seed());
        await using var db = fixture.CreateDbContext();
        foreach (var value in new[] { "manifest_version=2", "lease_generation=0", "lease_generation=2", "file_count=0", "manifest_hash='bad'" })
            await Reject(db, $"UPDATE core.requirement_execution_evidence SET {value}", "23514", "ck_execution_evidence_shape");
        foreach (var value in new[] { "ordinal=-1", "stored_evidence_version=2", "declared_length_bytes=0",
            "expected_length_bytes=4", "observed_length_bytes=4", "expected_sha256='bad'", "observed_sha256=repeat('A',64)" })
            await Reject(db, $"UPDATE core.requirement_execution_documents SET {value}", "23514", "ck_execution_document_shape");
        await Reject(db, "UPDATE core.requirement_execution_evidence SET execution_request_id='00000000-0000-0000-0000-000000000000'", "23514", "ck_execution_evidence_ids");
        await Reject(db, "UPDATE core.requirement_execution_documents SET file_id='00000000-0000-0000-0000-000000000000'", "23514", "ck_execution_document_ids");
        foreach (var pair in new[] { ("requirement_id","requirement"), ("job_id","job"), ("processing_attempt_id","attempt"), ("actor_user_id","actor") })
            await Reject(db, $"UPDATE core.requirement_execution_evidence SET {pair.Item1}='{Guid.NewGuid()}'", "23503", $"fk_execution_evidence_{pair.Item2}");
        foreach (var pair in new[] { ("file_id","file"), ("evidence_id","evidence") })
            await Reject(db, $"UPDATE core.requirement_execution_documents SET {pair.Item1}='{Guid.NewGuid()}'", "23503", $"fk_execution_document_{pair.Item2}");
    }

    [Fact]
    public async Task UniqueKeys_AndRestrictiveDeletes()
    {
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed(2); await Store(a); await Store(b);
        await using var db = fixture.CreateDbContext();
        await Reject(db, $"UPDATE core.requirement_execution_evidence SET job_id='{a.JobId}' WHERE id='{b.Id}'", "23505", "ux_execution_evidence_job");
        await Reject(db, $"UPDATE core.requirement_execution_evidence SET processing_attempt_id='{a.ProcessingAttemptId}' WHERE id='{b.Id}'", "23505", "ux_execution_evidence_attempt");
        var columns = "evidence_id,file_id,ordinal,original_file_name,content_type,storage_key,file_created_at_utc,declared_length_bytes,expected_sha256,expected_length_bytes,stored_evidence_at_utc,stored_evidence_version,observed_sha256,observed_length_bytes";
        var tail = "original_file_name,content_type,storage_key,file_created_at_utc,declared_length_bytes,expected_sha256,expected_length_bytes,stored_evidence_at_utc,stored_evidence_version,observed_sha256,observed_length_bytes";
        await Reject(db, $"INSERT INTO core.requirement_execution_documents ({columns}) SELECT evidence_id,file_id,1,{tail} FROM core.requirement_execution_documents WHERE evidence_id='{a.Id}'", "23505", "pk_execution_documents");
        await Reject(db, $"INSERT INTO core.requirement_execution_documents ({columns}) SELECT evidence_id,'{b.PrimaryDocumentId}',0,{tail} FROM core.requirement_execution_documents WHERE evidence_id='{a.Id}'", "23505", "ux_execution_document_ordinal");
        await Reject(db, $"DELETE FROM core.requirement_execution_evidence WHERE id='{a.Id}'", "23503", "fk_execution_document_evidence");
        await Reject(db, $"DELETE FROM core.requirement_files WHERE id='{a.PrimaryDocumentId}'", "23503", "fk_execution_document_file");
        await Reject(db, $"DELETE FROM core.requirement_processing_jobs WHERE id='{a.JobId}'", "23503", "fk_execution_evidence_job");
        // Isolate these restrict edges from older relationships which also prevent deletion.
        var actor = User.CreateFromGoogle("only-evidence@test.local", "Actor", null, null, At);
        var project = await db.Projects.FirstAsync(Token);
        var quote = PreQuote.Create(project.Id, a.ActorUserId, "PC-2026-0099", null, At);
        var requirement = Requirement.Create(quote.Id, a.ActorUserId, RequirementCommercialLine.Essential, At);
        var attempt = RequirementProcessingAttempt.Create(a.RequirementId, a.ActorUserId, Guid.NewGuid(), At);
        attempt.Start(At);
        attempt.Cancel(At);
        db.AddRange(actor, quote, requirement, attempt); await db.SaveChangesAsync(Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_execution_evidence SET requirement_id={requirement.Id},processing_attempt_id={attempt.Id},actor_user_id={actor.Id} WHERE id={a.Id}", Token);
        await Reject(db, $"DELETE FROM core.requirements WHERE id='{requirement.Id}'", "23503", "fk_execution_evidence_requirement");
        await Reject(db, $"DELETE FROM core.requirement_processing_attempts WHERE id='{attempt.Id}'", "23503", "fk_execution_evidence_attempt");
        await Reject(db, $"DELETE FROM identity.users WHERE id='{actor.Id}'", "23503", "fk_execution_evidence_actor");
    }

    [Fact]
    public async Task MigrationPreservesHistory_AndDatabaseLimitsAreExplicit()
    {
        await fixture.ResetAsync(); var evidence = await Seed();
        await using var db = fixture.CreateDbContext();
        db.Add(RequirementFile.Create(evidence.RequirementId, "historical.pdf", "application/pdf", 3, "historical/unknown", At));
        await db.SaveChangesAsync(Token);
        var before = JsonSerializer.Serialize(new {
            Jobs = await db.RequirementProcessingJobs.AsNoTracking().ToListAsync(Token),
            Files = await db.RequirementFiles.AsNoTracking().ToListAsync(Token),
            Requirements = await db.Requirements.AsNoTracking().ToListAsync(Token) });
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync("20261022211442_AddPublicSubmissionStoredEvidence", Token);
            await migrator.MigrateAsync(cancellationToken: Token);
            var after = JsonSerializer.Serialize(new {
                Jobs = await db.RequirementProcessingJobs.AsNoTracking().ToListAsync(Token),
                Files = await db.RequirementFiles.AsNoTracking().ToListAsync(Token),
                Requirements = await db.Requirements.AsNoTracking().ToListAsync(Token) });
            Assert.Equal(before, after);
            Assert.False(await db.Set<RequirementExecutionEvidence>().AnyAsync(Token));
            Assert.False(db.Database.HasPendingModelChanges());
            await Store(evidence);
            var other = await Seed(2);
            // These valid SQL values are deliberately NOT proof of compound ownership, completeness or hashing.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_execution_evidence SET requirement_id={other.RequirementId},execution_request_id={Guid.NewGuid()},primary_document_id={Guid.NewGuid()},file_count=4,manifest_hash=repeat('A',64) WHERE id={evidence.Id}", Token);
            Assert.Equal(4, await db.Set<RequirementExecutionEvidence>().Select(x => x.FileCount).SingleAsync(Token));
            await db.Database.ExecuteSqlRawAsync("DELETE FROM core.requirement_execution_documents", Token);
            Assert.False(await db.Set<RequirementExecutionDocument>().AnyAsync(Token));
        }
        finally { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
    }

    private static async Task Reject(ApplicationDbContext db, string sql, string code, string constraint)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token));
        Assert.Equal(code, error.SqlState); Assert.Equal(constraint, error.ConstraintName);
    }
    private sealed class Updates : DbCommandInterceptor
    {
        internal int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (command.CommandText.Contains("UPDATE", StringComparison.Ordinal)) Count++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (command.CommandText.Contains("UPDATE", StringComparison.Ordinal)) Count++; return ValueTask.FromResult(result); }
    }
}

