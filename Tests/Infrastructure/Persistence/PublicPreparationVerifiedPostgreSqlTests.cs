using System.Data.Common;
using System.Text.Json;
using Application.Common.Abstractions.Storage;
using Application.PublicExperience;
using CotizadorBackend.Tests.Application.PublicExperience;
using Domain.PreQuotes;
using Domain.PublicExperience;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed partial class PublicPreparationVerifiedPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Hash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incorporation_RealBytes_Order_Owner_OriginalUnchanged_AndCallerTracking(bool historical)
    {
        using var data = await Seed(2);
        if (historical)
        {
            await using var change = fixture.CreateDbContext();
            await change.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submission_files SET stored_length_bytes=NULL,stored_evidence_at_utc=NULL,stored_evidence_version=NULL,content_hash=lower(content_hash)", Token);
        }
        var original = await Original();
        var spools = Spools();
        var writes = new WriteCounter();
        await using var caller = Context(writes);
        var tracked = await caller.Set<PublicExperienceSubmission>().SingleAsync(Token);
        tracked.UpdatedAtUtc = tracked.UpdatedAtUtc.AddDays(1);
        var result = await Service(caller, data).ExecuteAsync("public-test-id", Token);
        Assert.True(result!.IsSuccess, result.ErrorCode);
        Assert.True(caller.ChangeTracker.HasChanges());
        Assert.Equal(EntityState.Modified, caller.Entry(tracked).State);
        await using var read = fixture.CreateDbContext();
        var files = await read.RequirementFiles.OrderBy(f => f.CreatedAtUtc).ThenBy(f => f.Id).ToListAsync(Token);
        Assert.Equal(new[] { "first.pdf", "second.pdf" }, files.Select(f => f.OriginalFileName));
        Assert.True(files[0].CreatedAtUtc < files[1].CreatedAtUtc);
        Assert.All(files, f => { Assert.Equal(Hash, f.StoredSha256); Assert.Equal(3, f.StoredLengthBytes); Assert.Equal((short)1, f.StoredEvidenceVersion); Assert.NotNull(f.StoredEvidenceAtUtc); });
        foreach (var file in files)
        {
            Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(data.Root, file.StorageKey), Token));
            Assert.StartsWith($"requirements/{result.RequirementId:D}/", file.StorageKey);
        }
        var requirement = await read.Requirements.SingleAsync(Token);
        var quote = await read.PreQuotes.SingleAsync(Token);
        var project = await read.Projects.SingleAsync(Token);
        Assert.NotEqual(tracked.UpdatedAtUtc, (await read.Set<PublicExperienceSubmission>().SingleAsync(Token)).UpdatedAtUtc);
        Assert.Equal(project.CreatedByUserId, requirement.CreatedByUserId);
        Assert.Equal(project.CreatedByUserId, quote.CreatedByUserId);
        Assert.Equal(PreQuote.FormatSerial(requirement.CreatedAtUtc.Year, 1), quote.Serial);
        Assert.Equal(RequirementStatus.Pending, requirement.Status);
        Assert.Equal(PreparePublicExperienceRequirementService.CommercialLineBridge, requirement.CommercialLine);
        Assert.Equal(original, await Original());
        Assert.False(await read.RequirementProcessingJobs.AnyAsync(Token));
        Assert.False(await read.RequirementProcessingAttempts.AnyAsync(Token));
        Assert.False(read.Database.HasPendingModelChanges());
        Assert.Equal(2, data.Opens); Assert.Equal(2, data.Saves); Assert.Equal(0, data.Deletes);
        Assert.Equal(spools, Spools());
        writes.Enabled = true;
        var replay = await Service(caller, data).ExecuteAsync("public-test-id", Token);
        Assert.Equal(result with { IsReplay = true }, replay);
        Assert.Equal(0, writes.Count);
        Assert.Equal(2, data.Opens); Assert.Equal(2, data.Saves);
    }

    [Fact]
    public async Task HistoricalCompleted_ReplayKeepsLinkedIdentityAfterReplacement_WithoutBackfill()
    {
        using var data = await Seed();
        await using var caller = fixture.CreateDbContext();
        var result = await Service(caller, data).ExecuteAsync("public-test-id", Token);
        Assert.NotNull(result);
        await using (var edit = fixture.CreateDbContext())
        {
            // Historical fixture, not a production SQL writer or backfill.
            await edit.Database.ExecuteSqlRawAsync("UPDATE core.requirement_files SET stored_sha256=NULL,stored_length_bytes=NULL,stored_evidence_at_utc=NULL,stored_evidence_version=NULL", Token);
            var old = await edit.Requirements.SingleAsync(Token);
            var now = DateTimeOffset.UtcNow;
            old.StartProcessing(now); old.MarkProcessed(now);
            var successor = Requirement.Create(old.PreQuoteId, old.CreatedByUserId, old.CommercialLine!.Value, now);
            successor.MarkAsReplacementOf(old.Id); old.SupersedeBy(successor.Id, now);
            edit.Requirements.Add(successor);
            await edit.SaveChangesAsync(Token);
        }
        var before = await Graph();
        var replay = await Service(caller, data).ExecuteAsync("public-test-id", Token);
        Assert.Equal(result with { IsReplay = true }, replay);
        Assert.Equal(before, await Graph());
        Assert.Equal(1, data.Opens); Assert.Equal(1, data.Saves);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("invalid-hash")]
    [InlineData("receipt-hash")]
    [InlineData("receipt-length")]
    [InlineData("second")]
    [InlineData("cancel-read")]
    [InlineData("cancel-after-save")]
    public async Task CopyFailures_NoPartialGraph_KeepDestinations_ReleaseSpools(string fault)
    {
        using var data = await Seed(fault == "second" ? 2 : 1);
        if (fault is "hash" or "short" or "long")
        {
            await using var read = fixture.CreateDbContext();
            var key = (await read.Set<PublicExperienceSubmissionFile>().FirstAsync(Token)).StorageKey;
            await File.WriteAllBytesAsync(Path.Combine(data.Root, key), fault == "hash" ? "xyz"u8.ToArray()
                : fault == "short" ? "ab"u8.ToArray() : "abcd"u8.ToArray(), Token);
        }
        if (fault == "invalid-hash")
        {
            await using var change = fixture.CreateDbContext();
            await change.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submission_files SET stored_length_bytes=NULL,stored_evidence_at_utc=NULL,stored_evidence_version=NULL,content_hash='invalid'", Token);
        }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (fault == "cancel-read") data.CancelRead = cancel;
        if (fault == "cancel-after-save") data.AfterSave = () => { cancel.Cancel(); return Task.CompletedTask; };
        if (fault == "second") data.FailSave = 2;
        if (fault.StartsWith("receipt-", StringComparison.Ordinal)) data.WrongReceipt = fault;
        var before = await Original();
        var spools = Spools();
        await using var db = fixture.CreateDbContext();
        if (fault.StartsWith("cancel-", StringComparison.Ordinal))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db, data).ExecuteAsync("public-test-id", cancel.Token));
        else
        {
            var result = await Service(db, data).ExecuteAsync("public-test-id", Token);
            Assert.False(result!.IsSuccess);
            Assert.Equal(fault is "hash" or "short" or "long" or "invalid-hash"
                ? PublicExperienceRequirementPreparationErrors.FileIntegrity : PublicExperienceRequirementPreparationErrors.Failed, result.ErrorCode);
        }
        Assert.Equal(before, await Original());
        await using var verify = fixture.CreateDbContext();
        Assert.False(await verify.PreQuotes.AnyAsync(Token));
        Assert.False(await verify.Requirements.AnyAsync(Token));
        Assert.False(await verify.RequirementFiles.AnyAsync(Token));
        if (fault == "second") { Assert.True(data.Injected); Assert.Equal(3, Directory.GetFiles(data.Root, "*", SearchOption.AllDirectories).Length); }
        if (fault.StartsWith("receipt-", StringComparison.Ordinal) || fault == "cancel-after-save")
            Assert.Equal(2, Directory.GetFiles(data.Root, "*", SearchOption.AllDirectories).Length);
        Assert.Equal(0, data.Deletes); Assert.Equal(spools, Spools());
    }

    [Theory]
    [InlineData("user", false)]
    [InlineData("client", false)]
    [InlineData("project", false)]
    [InlineData("user", true)]
    [InlineData("client", true)]
    [InlineData("project", true)]
    [InlineData("document", true)]
    [InlineData("late-error", true)]
    public async Task Revalidation_RejectsChangedDependency_WithoutAttributingErrorToNewState(string dependency, bool afterCopy)
    {
        using var data = await Seed();
        async Task Change()
        {
            // Instrumentation of revalidation, not an additional business writer.
            await using var edit = fixture.CreateDbContext();
            var sql = dependency switch
            {
                "user" => "UPDATE identity.users SET is_active=false",
                "client" => "UPDATE core.clients SET is_active=false",
                "project" => "UPDATE core.projects SET is_active=false",
                _ => "UPDATE core.public_experience_submission_files SET original_file_name='changed.pdf'"
            };
            await edit.Database.ExecuteSqlRawAsync(sql, Token);
            if (dependency == "late-error") throw new IOException("Copy failed after source changed");
        }
        if (afterCopy) data.AfterSave = Change; else await Change();
        await using var db = fixture.CreateDbContext();
        var result = await Service(db, data).ExecuteAsync("public-test-id", Token);
        Assert.False(result!.IsSuccess);
        await using var read = fixture.CreateDbContext();
        Assert.False(await read.Requirements.AnyAsync(Token));
        var submission = await read.Set<PublicExperienceSubmission>().SingleAsync(Token);
        Assert.Null(submission.RequirementId);
        if (afterCopy) Assert.Null(submission.RequirementPreparationErrorCode);
        Assert.Equal(afterCopy ? 1 : 0, data.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialOrCrossedLinks_AreRejectedWithoutRepairOrCopy(bool crossed)
    {
        using var data = await Seed();
        await using (var edit = fixture.CreateDbContext())
        {
            var project = await edit.Projects.SingleAsync(Token);
            var q = PreQuote.Create(project.Id, project.CreatedByUserId, "PC-2026-0041", null, DateTimeOffset.UtcNow);
            edit.PreQuotes.Add(q);
            if (crossed)
            {
                var other = PreQuote.Create(project.Id, project.CreatedByUserId, "PC-2026-0042", null, DateTimeOffset.UtcNow);
                var requirement = Requirement.Create(other.Id, project.CreatedByUserId, RequirementCommercialLine.Essential, DateTimeOffset.UtcNow);
                edit.PreQuotes.Add(other); edit.Requirements.Add(requirement);
                await edit.SaveChangesAsync(Token);
                await edit.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.public_experience_submissions SET pre_quote_id={q.Id},requirement_id={requirement.Id},requirement_prepared_at_utc={DateTimeOffset.UtcNow}", Token);
            }
            else
            {
                await edit.SaveChangesAsync(Token);
                await edit.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.public_experience_submissions SET pre_quote_id={q.Id}", Token);
            }
        }
        var before = await Graph();
        await using var caller = fixture.CreateDbContext();
        var result = await Service(caller, data).ExecuteAsync("public-test-id", Token);
        Assert.Equal(PublicExperienceRequirementPreparationErrors.InconsistentLinks, result!.ErrorCode);
        Assert.Equal(before, await Graph()); Assert.Equal(0, data.Opens); Assert.Equal(0, data.Saves);
    }

    private async Task<Files> Seed(int count = 1)
    {
        fixture.RequireAvailable();
        await fixture.ResetAsync();
        var root = Directory.CreateTempSubdirectory("sng-public-preparation-").FullName;
        var data = new Files(root, output);
        try
        {
            await using var db = fixture.CreateDbContext();
            db.Users.Add(ProcessPublicExperienceSubmissionTests.Owner());
            await db.SaveChangesAsync(Token);
            var uploads = Enumerable.Range(0, count).Select(i => PublicExperienceIntakeTests.Upload("abc"u8.ToArray())
                with { FileName = i == 0 ? "first.pdf" : "second.pdf" }).ToArray();
            var intake = new PublicExperienceIntakeService(new PublicExperienceSubmissionRepository(db), data.Inner,
                TimeProvider.System, NullLogger<PublicExperienceIntakeService>.Instance, PublicExperienceIntakeTests.Capture());
            Assert.Equal(201, (await intake.ExecuteAsync(PublicExperienceIntakeTests.Payload(uploads), uploads, Token)).StatusCode);
            var result = await new ProcessPublicExperienceSubmissionService(new PublicExperienceProcessingRepository(db,
                TimeProvider.System, NullLogger<PublicExperienceProcessingRepository>.Instance), new IdentityRepository(db),
                new ClientRepository(db), new ProjectRepository(db), Options.Create(new PublicExperienceOptions { OwnerEmail = "owner@example.test" }),
                TimeProvider.System).ExecuteAsync("public-test-id", Token);
            Assert.Equal("PROCESSED", result!.Status);
            return data;
        }
        catch { data.Dispose(); throw; }
    }

    private ApplicationDbContext Context(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(fixture.ConnectionString).AddInterceptors(interceptors).Options);

    private static PreparePublicExperienceRequirementService Service(ApplicationDbContext db, IFileStorage files) =>
        new(new PublicExperienceRequirementPreparationRepository(db, TimeProvider.System, NullLogger<PublicExperienceRequirementPreparationRepository>.Instance),
            files, PreparePublicExperienceRequirementTests.Copy(), TimeProvider.System, NullLogger<PreparePublicExperienceRequirementService>.Instance);

    private async Task<string> Original()
    {
        await using var db = fixture.CreateDbContext();
        var row = await db.Set<PublicExperienceSubmission>().Include(s => s.Files).SingleAsync(Token);
        return JsonSerializer.Serialize(new { row.Id, row.SubmissionId, row.AnswersJson, row.PayloadFingerprint,
            row.ClientId, row.ProjectId, row.ProcessedAtUtc, Files = row.Files.OrderBy(f => f.Sequence) });
    }

    private async Task<string> Graph()
    {
        await using var db = fixture.CreateDbContext();
        return JsonSerializer.Serialize(new
        {
            Submission = await db.Set<PublicExperienceSubmission>().AsNoTracking().SingleAsync(Token),
            Quotes = await db.PreQuotes.AsNoTracking().OrderBy(q => q.Id).ToListAsync(Token),
            Requirements = await db.Requirements.AsNoTracking().OrderBy(q => q.Id).ToListAsync(Token),
            Files = await db.RequirementFiles.AsNoTracking().OrderBy(q => q.Id).ToListAsync(Token)
        });
    }
    private static string[] Spools() => Directory.GetDirectories(Path.GetTempPath(), "sng-sealed-*").Order().ToArray();

    private sealed class Files(string root, ITestOutputHelper output) : IFileStorage, IDisposable
    {
        internal string Root => root;
        internal LocalFileStorage Inner { get; } = new(new FileStorageOptions(root), Substitute.For<IHostEnvironment>());
        internal Func<Task>? BeforeOpen, AfterSave;
        internal CancellationTokenSource? CancelRead;
        internal int Opens, Saves, Deletes, FailSave;
        internal string? WrongReceipt;
        internal bool Injected;
        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            Opens++;
            if (BeforeOpen is not null) await BeforeOpen();
            var stream = await Inner.OpenReadAsync(key, ct);
            return CancelRead is null ? stream : new CancelReader(stream, CancelRead);
        }
        public async Task<StoredContentReceipt> SaveVerifiedAsync(string key, Stream source, long length, long maximum, CancellationToken ct)
        {
            Saves++;
            if (Saves == FailSave) { Injected = true; throw new IOException("Second copy failed"); }
            StoredContentReceipt receipt;
            if (WrongReceipt is not null)
            {
                using var other = new MemoryStream(WrongReceipt == "receipt-hash" ? "xyz"u8.ToArray() : "abcd"u8.ToArray());
                receipt = await Inner.SaveVerifiedAsync(key, other, other.Length, maximum, ct);
            }
            else receipt = await Inner.SaveVerifiedAsync(key, source, length, maximum, ct);
            if (AfterSave is not null) await AfterSave();
            return receipt;
        }
        public Task SaveAsync(string key, Stream stream, CancellationToken ct) => throw new InvalidOperationException("Unverified preparation write");
        public Task DeleteIfExistsAsync(string key, CancellationToken ct) { Deletes++; throw new InvalidOperationException("Destructive compensation"); }
        public void Dispose() { Directory.Delete(root, true); output.WriteLine($"Removed storage: {root}"); }
    }

    private sealed class CancelReader(Stream inner, CancellationTokenSource cancel) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { var n = await inner.ReadAsync(buffer, ct); cancel.Cancel(); return n; }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
