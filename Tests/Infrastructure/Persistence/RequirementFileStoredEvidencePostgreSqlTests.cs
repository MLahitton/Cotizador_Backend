using System.Data.Common;
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

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class RequirementFileStoredEvidencePostgreSqlTests(PostgreSqlIntegrationFixture fixture)
{
    private const string Hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Mutations()
    {
        foreach (var property in new[] { "StoredSha256", "StoredLengthBytes", "StoredEvidenceAtUtc", "StoredEvidenceVersion" })
            foreach (var kind in new[] { "change", "clear", "populate" }) yield return [property, kind];
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task EfRejectsEveryEvidenceMutation_BeforeUpdate(string property, string kind)
    {
        await fixture.ResetAsync();
        var id = await Seed(kind != "populate");
        var counter = new Updates();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(counter).Options);
        var file = await db.RequirementFiles.SingleAsync(Token);
        object? value = kind == "clear" ? null : property switch
        {
            "StoredSha256" => new string('A', 64),
            "StoredLengthBytes" => 4L,
            "StoredEvidenceVersion" => (short)2,
            _ => At.AddSeconds(1)
        };
        db.Entry(file).Property(property).CurrentValue = value;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
        Assert.Contains(property, error.Message);
        Assert.Equal(0, counter.Count);
        await using var read = fixture.CreateDbContext();
        var persisted = await read.RequirementFiles.SingleAsync(f => f.Id == id, Token);
        Assert.Equal(kind == "populate" ? null : Hash, persisted.StoredSha256);
        Assert.Equal(kind == "populate" ? (long?)null : 3, persisted.StoredLengthBytes);
        Assert.Equal(kind == "populate" ? (DateTimeOffset?)null : At, persisted.StoredEvidenceAtUtc);
        Assert.Equal(kind == "populate" ? (short?)null : 1, persisted.StoredEvidenceVersion);
    }

    [Fact]
    public async Task DatabaseRejectsAllPartialGroups_AndInvalidCompleteGroups()
    {
        await fixture.ResetAsync();
        await Seed(false);
        await using var db = fixture.CreateDbContext();
        for (var mask = 1; mask < 15; mask++)
        {
            string? hash = (mask & 1) != 0 ? Hash : null;
            long? length = (mask & 2) != 0 ? 3 : null;
            DateTimeOffset? at = (mask & 4) != 0 ? At : null;
            short? version = (mask & 8) != 0 ? (short)1 : null;
            await Reject(db, hash, length, at, version);
        }
        foreach (var hash in new[] { "", Hash.ToLowerInvariant(), new string('Z', 64), Hash[..63] })
            await Reject(db, hash, 3, At, 1);
        foreach (var length in new long[] { -1, 0, 2, 4 }) await Reject(db, Hash, length, At, 1);
        await Reject(db, Hash, 3, At, 0);
        await Reject(db, Hash, 3, At, 2);
        Assert.Null((await db.RequirementFiles.SingleAsync(Token)).StoredSha256);
        // Shape checks intentionally permit valid direct SQL changes; they are not immutability triggers.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_files SET stored_sha256={Hash}, stored_length_bytes={3L}, stored_evidence_at_utc={At}, stored_evidence_version={(short)1}", Token);
        db.ChangeTracker.Clear();
        Assert.Equal(Hash, (await db.RequirementFiles.SingleAsync(Token)).StoredSha256);
    }

    private static async Task Reject(ApplicationDbContext db, string? hash, long? length, DateTimeOffset? at, short? version)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE core.requirement_files SET stored_sha256={hash}, stored_length_bytes={length}, stored_evidence_at_utc={at}, stored_evidence_version={version}", Token));
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_requirement_files_stored_evidence", error.ConstraintName);
    }

    [Fact]
    public async Task MigrationPreservesPreviousRows_AndModelHasNoPendingChanges()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        var previous = "20261022042216_AddJobPublicationReceipt";
        var root = Directory.CreateTempSubdirectory("sng-evidence-migration-").FullName;
        var path = Path.Combine(root, "old", "original.pdf");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, "abc"u8.ToArray(), Token);
            await migrator.MigrateAsync(previous, Token);
            var requirement = await SeedParents(db);
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO core.requirement_files (id,requirement_id,original_file_name,content_type,size_bytes,storage_key,created_at_utc) VALUES ({id},{requirement.Id},{"historical.pdf"},{"application/pdf"},{3L},{"old/original.pdf"},{At})", Token);
            await migrator.MigrateAsync(cancellationToken: Token);
            db.ChangeTracker.Clear();
            var file = await db.RequirementFiles.SingleAsync(Token);
            Assert.Equal(id, file.Id);
            Assert.Equal(requirement.Id, file.RequirementId);
            Assert.Equal("historical.pdf", file.OriginalFileName);
            Assert.Equal("application/pdf", file.ContentType);
            Assert.Equal(3, file.SizeBytes);
            Assert.Equal("old/original.pdf", file.StorageKey);
            Assert.Equal(path, Path.Combine(root, file.StorageKey.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(At, file.CreatedAtUtc);
            Assert.Null(file.StoredSha256);
            Assert.Null(file.StoredLengthBytes);
            Assert.Null(file.StoredEvidenceAtUtc);
            Assert.Null(file.StoredEvidenceVersion);
            db.Add(RequirementFile.Create(requirement.Id, "public.pdf", "application/pdf", 3, "public/new", At));
            await db.SaveChangesAsync(Token);
            Assert.Equal(2, await db.RequirementFiles.CountAsync(Token));
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(path, Token));
        }
        finally
        {
            try { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
            finally { Directory.Delete(root, recursive: true); }
        }
    }

    private async Task<Guid> Seed(bool evidence)
    {
        await using var db = fixture.CreateDbContext();
        var requirement = await SeedParents(db);
        var file = evidence ? RequirementFile.CreateWithStoredEvidence(requirement.Id, "a.pdf", "application/pdf", 3, "a", At, Hash, 3, At, 1)
            : RequirementFile.Create(requirement.Id, "a.pdf", "application/pdf", 3, "a", At);
        db.Add(file);
        await db.SaveChangesAsync(Token);
        return file.Id;
    }

    private static async Task<Requirement> SeedParents(ApplicationDbContext db)
    {
        var user = User.CreateFromGoogle("evidence@test.local", "Owner", null, null, At);
        var client = Client.Create(ClientType.Person, "Client", null, null, null, null, null, null, null, user.Id, At);
        var project = Project.Create(client.Id, "EVIDENCE", "Evidence", null, null, user.Id, At);
        var quote = PreQuote.Create(project.Id, user.Id, "PC-2026-0001", null, At);
        var requirement = Requirement.Create(quote.Id, user.Id, RequirementCommercialLine.Essential, At);
        db.AddRange(user, client, project, quote, requirement);
        await db.SaveChangesAsync(Token);
        return requirement;
    }

    private sealed class Updates : DbCommandInterceptor
    {
        internal int Count;
        private void Inspect(DbCommand command) { if (command.CommandText.Contains("UPDATE", StringComparison.Ordinal)) Count++; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
    }
}
