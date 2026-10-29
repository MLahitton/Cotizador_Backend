using System.Data.Common;
using System.Text;
using System.Text.Json;
using Application.Common.Abstractions.Storage;
using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed partial class PublicSubmissionEvidencePostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
    private const string Fingerprint = "381DEE88EE1D3A1EF415C3DF1468AF7169BB14A033683DDF67551941981C23F7";
    private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("cancel")]
    public async Task CaptureFailure_NoWritesAndSourceDisposed(string scenario)
    {
        await fixture.ResetAsync();
        var before = Spools();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var source = new PartialSource(scenario == "short" ? "ab" : "abcd");
        var opened = 0;
        var upload = new PublicExperienceUpload("plan.pdf", "application/pdf", 3, () => { opened++; return source; });
        var storage = Substitute.For<IFileStorage>();
        await using var db = fixture.CreateDbContext();
        if (scenario == "cancel")
        {
            source.OnRead = n => { if (n == 2) cts.Cancel(); };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db, storage).ExecuteAsync(
                PublicExperienceIntakeTests.Payload([upload]), [upload], cts.Token));
        }
        else Assert.Equal(400, (await Service(db, storage).ExecuteAsync(PublicExperienceIntakeTests.Payload([upload]), [upload], Token)).StatusCode);
        Assert.Equal(1, opened); Assert.True(source.Closed);
        Assert.Empty(storage.ReceivedCalls());
        Assert.False(await db.Set<PublicExperienceSubmission>().AnyAsync(Token));
        Assert.Equal(before, Spools());
    }

    [Fact]
    public async Task SingleOpening_LiteralFingerprint_RealStorage_ReplayConflictAndCleanup()
    {
        await fixture.ResetAsync();
        var root = Directory.CreateTempSubdirectory("sng-public-evidence-").FullName;
        var before = Spools();
        try
        {
            var storage = Storage(root);
            var opens = 0;
            var sources = new List<PartialSource>();
            var upload = new PublicExperienceUpload("plan.pdf", "application/pdf", 3, () =>
            {
                var source = new PartialSource(++opens == 1 ? "abc" : "xyz");
                sources.Add(source);
                return source;
            });
            var json = PublicExperienceIntakeTests.Payload([upload]);
            var started = DateTimeOffset.UtcNow;
            await using (var db = fixture.CreateDbContext())
                Assert.Equal(201, (await Service(db, storage).ExecuteAsync(json, [upload], Token)).StatusCode);
            Assert.Equal(1, opens);
            Assert.All(sources, s => Assert.True(s.Closed));
            Assert.Equal(before, Spools());
            string snapshot;
            await using (var db = fixture.CreateDbContext())
            {
                var row = await db.Set<PublicExperienceSubmission>().Include(s => s.Files).SingleAsync(Token);
                var file = Assert.Single(row.Files);
                Assert.Equal(Fingerprint, row.PayloadFingerprint);
                Assert.Equal(Hash, file.ContentHash);
                Assert.Equal(3, file.StoredLengthBytes);
                Assert.Equal((short)1, file.StoredEvidenceVersion);
                Assert.NotNull(file.StoredEvidenceAtUtc);
                Assert.InRange(file.StoredEvidenceAtUtc.Value, started.AddTicks(-10), DateTimeOffset.UtcNow);
                Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(root, file.StorageKey), Token));
                snapshot = JsonSerializer.Serialize(row);
            }
            var stable = PublicExperienceIntakeTests.Upload("abc"u8.ToArray());
            await using (var db = fixture.CreateDbContext())
                Assert.True((await Service(db, storage).ExecuteAsync(json, [stable], Token)).IsReplay);
            await using (var db = fixture.CreateDbContext())
                Assert.Equal(409, (await Service(db, storage).ExecuteAsync(json, [upload], Token)).StatusCode);
            Assert.Equal(2, opens);
            Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
            Assert.Equal(before, Spools());
            await using var read = fixture.CreateDbContext();
            Assert.Equal(snapshot, JsonSerializer.Serialize(await read.Set<PublicExperienceSubmission>().Include(s => s.Files).SingleAsync(Token)));
            Assert.False(await read.RequirementProcessingJobs.AnyAsync(Token));
            Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
        }
        finally { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiptMismatch_IsInternalStorageFailure_NoReferenceAndNoCompensation(bool lengthMismatch)
    {
        await fixture.ResetAsync();
        var before = Spools();
        var root = Directory.CreateTempSubdirectory("sng-public-evidence-").FullName;
        try
        {
            var real = Storage(root);
            var storage = Substitute.For<IFileStorage>();
            var invoked = false;
            storage.SaveVerifiedAsync(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    invoked = true;
                    using var other = new MemoryStream(Encoding.UTF8.GetBytes(lengthMismatch ? "abcd" : "xyz"));
                    return await real.SaveVerifiedAsync(call.Arg<string>()!, other, other.Length, 20, call.Arg<CancellationToken>());
                });
            var upload = PublicExperienceIntakeTests.Upload("abc"u8.ToArray());
            await using var db = fixture.CreateDbContext();
            var error = await Assert.ThrowsAsync<FileStorageWriteException>(() => Service(db, storage)
                .ExecuteAsync(PublicExperienceIntakeTests.Payload([upload]), [upload], Token));
            Assert.IsType<ContentVerificationException>(error.InnerException);
            Assert.True(invoked);
            await using var read = fixture.CreateDbContext();
            Assert.False(await read.Set<PublicExperienceSubmission>().AnyAsync(Token));
            Assert.False(await read.Set<PublicExperienceSubmissionFile>().AnyAsync(Token));
            Assert.Single(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
            await storage.DidNotReceive().DeleteIfExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            Assert.Equal(before, Spools());
        }
        finally { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
    }

    public static IEnumerable<object[]> Mutations()
    {
        foreach (var property in new[] { "StoredLengthBytes", "StoredEvidenceAtUtc", "StoredEvidenceVersion" })
            foreach (var kind in new[] { "change", "clear", "populate" }) yield return [property, kind];
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task EfRejectsNineMutations_WithoutUpdate(string property, string kind)
    {
        await fixture.ResetAsync();
        await Seed(kind != "populate");
        var commands = new Updates();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.ConnectionString).AddInterceptors(commands).Options);
        var file = await db.Set<PublicExperienceSubmissionFile>().SingleAsync(Token);
        var before = JsonSerializer.Serialize(file);
        object? value = kind == "clear" ? null : property switch
        {
            "StoredLengthBytes" => 4L, "StoredEvidenceVersion" => (short)2, _ => At.AddSeconds(1)
        };
        db.Entry(file).Property(property).CurrentValue = value;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
        Assert.Contains(property, error.Message);
        Assert.Equal(0, commands.Count);
        await using var read = fixture.CreateDbContext();
        Assert.Equal(before, JsonSerializer.Serialize(await read.Set<PublicExperienceSubmissionFile>().SingleAsync(Token)));
    }

    [Fact]
    public async Task CheckRejectsPartialAndInvalidGroups_UnknownHashRemainsCompatible()
    {
        await fixture.ResetAsync();
        await Seed(false);
        await using var db = fixture.CreateDbContext();
        async Task Reject(long? length, DateTimeOffset? at, short? version, string hash = Hash)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE core.public_experience_submission_files SET stored_length_bytes={length},stored_evidence_at_utc={at},stored_evidence_version={version},content_hash={hash}", Token));
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
            Assert.Equal("ck_submission_files_stored_evidence", error.ConstraintName);
        }
        for (var mask = 1; mask < 7; mask++)
            await Reject((mask & 1) != 0 ? 3L : null, (mask & 2) != 0 ? At : null, (mask & 4) != 0 ? (short)1 : null);
        foreach (var length in new long[] { -1, 0, 2, 4 }) await Reject(length, At, 1);
        await Reject(3, At, 0); await Reject(3, At, 2);
        foreach (var hash in new[] { "", Hash[..63], Hash.ToLowerInvariant(), new string('Z', 64) }) await Reject(3, At, 1, hash);
        await db.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submission_files SET content_hash='legacy'", Token);
        Assert.Equal("legacy", (await db.Set<PublicExperienceSubmissionFile>().SingleAsync(Token)).ContentHash);
    }

    [Fact]
    public async Task MigrationPreservesOriginalHashFingerprintAndBytes_WithoutBackfill()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var migrator = db.GetService<IMigrator>();
        var root = Directory.CreateTempSubdirectory("sng-public-evidence-").FullName;
        try
        {
            await migrator.MigrateAsync("20261022202916_AddRequirementFileStoredEvidence", Token);
            var row = Submission();
            db.Add(row); await db.SaveChangesAsync(Token);
            var id = Guid.NewGuid();
            var path = Path.Combine(root, "old.pdf");
            await File.WriteAllBytesAsync(path, "abc"u8.ToArray(), Token);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO core.public_experience_submission_files (id,submission_id,sequence,original_file_name,content_type,size_bytes,storage_key,content_hash,created_at_utc) VALUES ({id},{row.Id},{0},{"old.pdf"},{"application/pdf"},{3L},{"old.pdf"},{"legacy"},{At})", Token);
            await migrator.MigrateAsync(cancellationToken: Token);
            db.ChangeTracker.Clear();
            var file = await db.Set<PublicExperienceSubmissionFile>().SingleAsync(Token);
            Assert.Equal(id, file.Id); Assert.Equal(row.Id, file.SubmissionId);
            Assert.Equal(0, file.Sequence); Assert.Equal("old.pdf", file.OriginalFileName);
            Assert.Equal("application/pdf", file.ContentType); Assert.Equal(3, file.SizeBytes);
            Assert.Equal("old.pdf", file.StorageKey); Assert.Equal(At, file.CreatedAtUtc);
            Assert.Equal("legacy", file.ContentHash);
            Assert.Null(file.StoredLengthBytes); Assert.Null(file.StoredEvidenceAtUtc); Assert.Null(file.StoredEvidenceVersion);
            var saved = await db.Set<PublicExperienceSubmission>().SingleAsync(Token);
            Assert.Equal(row.PayloadFingerprint, saved.PayloadFingerprint);
            Assert.Equal(JsonSerializer.Serialize(JsonDocument.Parse(row.AnswersJson)), JsonSerializer.Serialize(JsonDocument.Parse(saved.AnswersJson)));
            Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(root, file.StorageKey), Token));
            db.Add(Unknown(row.Id, 1)); await db.SaveChangesAsync(Token);
            Assert.Equal(2, await db.Set<PublicExperienceSubmissionFile>().CountAsync(Token));
            Assert.False(db.Database.HasPendingModelChanges());
        }
        finally
        {
            try { await migrator.MigrateAsync(cancellationToken: CancellationToken.None); }
            finally { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
        }
    }

    private async Task Seed(bool evidence)
    {
        await using var db = fixture.CreateDbContext();
        var row = Submission();
        row.Files.Add(evidence ? PublicExperienceSubmissionFile.CreateWithStoredEvidence(Guid.NewGuid(), row.Id,
            0, "a.pdf", "application/pdf", 3, "a", Hash, At, 3, At, 1) : Unknown(row.Id));
        db.Add(row); await db.SaveChangesAsync(Token);
    }
    private static PublicExperienceSubmission Submission() => new()
    {
        SubmissionId = "test", SchemaVersion = "sng-public-experience-v1", Source = "SNG_WEB",
        ClientFullName = "Client", ClientWhatsapp = "1", AdvisorCode = "AD01", AdvisorDisplayName = "Advisor",
        ProjectName = "Project", ProjectCity = "City", AnswersJson = "{}", PayloadFingerprint = Fingerprint,
        CreatedAtUtc = At, UpdatedAtUtc = At
    };
    private static PublicExperienceSubmissionFile Unknown(Guid id, int sequence = 0) => new()
    {
        SubmissionId = id, Sequence = sequence, OriginalFileName = "a.pdf", ContentType = "application/pdf",
        SizeBytes = 3, StorageKey = "a" + sequence, ContentHash = Hash, CreatedAtUtc = At
    };
    private static LocalFileStorage Storage(string root) => new(new FileStorageOptions(root), Substitute.For<IHostEnvironment>());
    private static PublicExperienceIntakeService Service(ApplicationDbContext db, IFileStorage storage) =>
        new(new PublicExperienceSubmissionRepository(db), storage, TimeProvider.System,
            NullLogger<PublicExperienceIntakeService>.Instance, PublicExperienceIntakeTests.Capture());
    private static string[] Spools() => Directory.GetDirectories(Path.GetTempPath(), "sng-sealed-*").Order().ToArray();
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
    private sealed class PartialSource(string text) : Stream
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(text);
        private int offset;
        private int reads;
        internal bool Closed;
        internal Action<int>? OnRead;
        public override bool CanRead => !Closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            OnRead?.Invoke(++reads);
            ct.ThrowIfCancellationRequested();
            if (offset == bytes.Length) return ValueTask.FromResult(0);
            buffer.Span[0] = bytes[offset++]; return ValueTask.FromResult(1);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
    }
}
