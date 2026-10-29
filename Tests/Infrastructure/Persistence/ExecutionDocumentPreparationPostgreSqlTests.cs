using System.Data.Common;
using System.Security.Cryptography;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.ProcessRequirement;
using Domain.PreQuotes;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
[Trait("Category", "PostgreSql")]
public sealed partial class ExecutionDocumentPreparationPostgreSqlTests(PostgreSqlIntegrationFixture fixture, ITestOutputHelper output)
{
    private JobTerminationTestSupport H => new(fixture);

    private sealed class Files : IFileStorage, IAsyncDisposable
    {
        internal readonly string Root = Directory.CreateTempSubdirectory("sng-execution-docs-").FullName;
        internal readonly LocalFileStorage Inner;
        internal int Opens, Deletes;
        internal Func<int, Task>? BeforeOpen;
        internal Files() => Inner = new(new FileStorageOptions(Root), Substitute.For<IHostEnvironment>());
        public Task SaveAsync(string key, Stream content, CancellationToken ct) => Inner.SaveAsync(key, content, ct);
        public Task<StoredContentReceipt> SaveVerifiedAsync(string key, Stream content, long expected, long maximum, CancellationToken ct)
            => Inner.SaveVerifiedAsync(key, content, expected, maximum, ct);
        public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
        {
            var count = Interlocked.Increment(ref Opens);
            if (BeforeOpen is not null) await BeforeOpen(count);
            return await Inner.OpenReadAsync(key, ct);
        }
        public Task DeleteIfExistsAsync(string key, CancellationToken ct) { Deletes++; return Inner.DeleteIfExistsAsync(key, ct); }
        public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }

    private sealed class Copies : IVerifiedContentCopy
    {
        internal readonly List<Copy> Created = [];
        internal Func<Task>? AfterCopy;
        public async Task<ICapturedContent> PrepareAsync(Stream source, long expectedLength, string expectedSha256,
            long maximumLength, CancellationToken cancellationToken)
        {
            var inner = await new VerifiedContentCopy(NullLogger<VerifiedContentCopy>.Instance)
                .PrepareAsync(source, expectedLength, expectedSha256, maximumLength, cancellationToken);
            var copy = new Copy(inner);
            Created.Add(copy);
            try { if (AfterCopy is not null) await AfterCopy(); }
            catch { await copy.DisposeAsync(); throw; }
            return copy;
        }
        internal sealed class Copy(ICapturedContent inner) : ICapturedContent
        {
            internal bool Disposed;
            public string Sha256 => inner.Sha256;
            public long Length => inner.Length;
            public Stream OpenRead() => inner.OpenRead();
            public async ValueTask DisposeAsync() { Disposed = true; await inner.DisposeAsync(); }
        }
    }

    private ExecutionDocumentPreparationStore Store(Files files, Copies copies, params IInterceptor[] hooks) =>
        new(H.Options(hooks), files, copies, NullLogger<ExecutionDocumentPreparationStore>.Instance);

    private async Task<Seed> SeedFiles(Files files, bool publicContext = false, int count = 2, bool start = true, bool verifiedPublication = true, bool assigned = true)
    {
        var seed = await H.Create(running: assigned, attempt: start, publicContext: publicContext, verifiedPublication: verifiedPublication);
        await using var db = fixture.CreateDbContext();
        db.RemoveRange(await db.RequirementFiles.ToListAsync(Token));
        var now = await H.Now();
        for (var i = 0; i < count; i++)
        {
            var key = $"documents/{i}.pdf";
            using var source = new MemoryStream("abc"u8.ToArray());
            var receipt = await files.SaveVerifiedAsync(key, source, 3, 20 * 1024 * 1024, Token);
            db.Add(RequirementFile.CreateWithStoredEvidence(seed.Execution.RequirementId, $"{count-i}.pdf", "application/pdf", 3,
                key, now.AddTicks(i * 10), receipt.Sha256, receipt.Length, now, 1));
        }
        await db.SaveChangesAsync(Token);
        output.WriteLine($"Storage root: {files.Root}");
        return seed;
    }

    private async Task AssertRows(int expected)
    {
        await using var db = fixture.CreateDbContext();
        Assert.Equal(expected, await db.Set<RequirementExecutionEvidence>().CountAsync(Token));
        Assert.Equal(expected * 2, await db.Set<RequirementExecutionDocument>().CountAsync(Token));
        Assert.Single(await db.RequirementProcessingJobs.ToListAsync(Token));
        Assert.Single(await db.RequirementProcessingAttempts.ToListAsync(Token));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RealBytes_PublicAndManual_ReplayWithoutCopiesOrWrites(bool publicContext)
    {
        await using var files = new Files();
        var seed = await SeedFiles(files, publicContext);
        var copies = new Copies();
        var before = await H.Snapshot();
        var result = await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentPreparationState.Registered, result.State);
        await using var package = Assert.IsType<PreparedExecutionDocuments>(result.Package);
        Assert.Equal(before, await H.Snapshot());
        Assert.Equal(2, files.Opens);
        await AssertRows(1);
        await using (var db = fixture.CreateDbContext())
        {
            var persisted = await db.Set<RequirementExecutionEvidence>().AsNoTracking().Include(x => x.Files).SingleAsync(Token);
            Assert.Equal(result.Evidence!.CanonicalManifest(), persisted.CanonicalManifest());
            Assert.Equal(result.Evidence.ManifestHash, persisted.ManifestHash);
            Assert.Equal(result.Evidence.VerifiedAtUtc, persisted.VerifiedAtUtc);
            Assert.Equal(result.Evidence.RegisteredAtUtc, persisted.RegisteredAtUtc);
            Assert.Equal(0, persisted.VerifiedAtUtc.Ticks % 10);
            Assert.Equal(0, persisted.RegisteredAtUtc.Ticks % 10);
        }
        Assert.Equal(result.Evidence!.Files[0].FileId, result.Evidence.PrimaryDocumentId);
        Assert.Equal(new[] { "2.pdf", "1.pdf" }, result.Evidence.Files.Select(f => f.OriginalFileName));
        // The original is changed only after all copies have been sealed; readers must use retained handles.
        await File.WriteAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), "xyz"u8.ToArray(), Token);
        foreach (var document in result.Evidence.Files)
        {
            using var reader = package.OpenRead(document.FileId);
            Assert.False(reader.CanWrite);
            using var bytes = new MemoryStream();
            await reader.CopyToAsync(bytes, Token);
            Assert.Equal("abc"u8.ToArray(), bytes.ToArray());
            Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", document.ObservedSha256);
            Assert.Equal(document.ObservedSha256, Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
            Assert.Throws<InvalidOperationException>(() => package.OpenRead(document.FileId));
        }
        var writes = new JobProtocolTestSupport.WriteProbe();
        var replay = await Store(files, copies, writes).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentPreparationState.AlreadyRegistered, replay.State);
        Assert.Null(replay.Package);
        Assert.Equal(result.Evidence.Id, replay.Evidence!.Id);
        Assert.Equal(result.Evidence.RegisteredAtUtc, replay.Evidence.RegisteredAtUtc);
        Assert.Equal(0, writes.Count);
        Assert.Equal(2, files.Opens);
        await H.Call("fail", seed);
        var historical = await Store(files, copies, writes).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentPreparationState.AlreadyRegistered, historical.State);
        Assert.Null(historical.Package);
        Assert.Equal(result.Evidence.Id, historical.Evidence!.Id);
        Assert.Equal(0, writes.Count); Assert.Equal(2, files.Opens);
        await package.DisposeAsync();
        await package.DisposeAsync();
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
        Assert.Throws<ObjectDisposedException>(() => package.OpenRead(result.Evidence.PrimaryDocumentId));
        Assert.Equal(0, files.Deletes);
        Assert.Equal("xyz"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
    }

    [Theory]
    [InlineData("historical")] [InlineData("digest")] [InlineData("length")]
    [InlineData("missing-second")] [InlineData("cancel")]
    public async Task PartialFailure_DisposesCopies_AndNeverRegisters(string mode)
    {
        await using var files = new Files();
        var seed = await SeedFiles(files);
        var copies = new Copies();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        if (mode == "historical")
        {
            await using var db = fixture.CreateDbContext();
            await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_files SET stored_sha256=NULL, stored_length_bytes=NULL, stored_evidence_at_utc=NULL, stored_evidence_version=NULL", Token);
        }
        if (mode is "digest" or "length")
            await File.WriteAllBytesAsync(Path.Combine(files.Root, "documents/1.pdf"), mode == "digest" ? "xyz"u8.ToArray() : "ab"u8.ToArray(), Token);
        if (mode == "missing-second") File.Delete(Path.Combine(files.Root, "documents/1.pdf"));
        if (mode == "cancel") files.BeforeOpen = n => { if (n == 2) caller.Cancel(); return Task.CompletedTask; };
        var before = await H.Snapshot();
        var writes = new JobProtocolTestSupport.WriteProbe();
        if (mode == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(files, copies, writes).PrepareAsync(seed.Execution, seed.Actor, caller.Token));
        else
        {
            var result = await Store(files, copies, writes).PrepareAsync(seed.Execution, seed.Actor, caller.Token);
            Assert.Equal(ExecutionDocumentPreparationState.Rejected, result.State);
            Assert.Null(result.Package);
            Assert.Equal(mode == "historical" ? ExecutionDocumentPreparationFailure.MissingIncorporation
                : mode == "missing-second" ? ExecutionDocumentPreparationFailure.SourceUnavailable : ExecutionDocumentPreparationFailure.ContentMismatch, result.Failure);
        }
        await AssertRows(0);
        Assert.Equal(before, await H.Snapshot());
        Assert.Equal(0, writes.Count);
        Assert.Equal(0, files.Deletes);
        if (mode == "historical") Assert.Equal(0, files.Opens);
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
        Assert.True(File.Exists(Path.Combine(files.Root, "documents/0.pdf")));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailureAfterSave_RollsBackWholeEvidence(bool expire)
    {
        await using var files = new Files();
        var seed = await SeedFiles(files);
        var copies = new Copies();
        var injected = false;
        var hook = new Saved(async db =>
        {
            injected = true;
            if (!expire) throw new IOException("Injected before guard");
            await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=clock_timestamp()-interval '1 second'", Token);
        });
        var before = await H.Snapshot();
        var result = await Store(files, copies, hook).PrepareAsync(seed.Execution, seed.Actor, Token);
        Assert.True(injected);
        Assert.Equal(ExecutionDocumentPreparationState.Rejected, result.State);
        Assert.Equal(expire ? ExecutionDocumentPreparationFailure.LeaseConflict : ExecutionDocumentPreparationFailure.PersistenceError, result.Failure);
        await AssertRows(0);
        Assert.Equal(before, await H.Snapshot());
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
        Assert.Equal(0, files.Deletes);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task RealCommit_ResponseLoss_IndependentObservationAndCallerCancellation(bool cancel, bool unavailable)
    {
        await using var files = new Files();
        var seed = await SeedFiles(files);
        var copies = new Copies();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var observing = false;
        var committed = false;
        var gate = new ObservationGate(() => observing, unavailable, caller.Token);
        var writes = new JobProtocolTestSupport.WriteProbe(() => observing);
        var hook = new CommitHook(() => { committed = true; observing = true; throw new IOException("Lost actual commit response"); });
        var pending = Store(files, copies, hook, gate, writes).PrepareAsync(seed.Execution, seed.Actor, caller.Token);
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.True(committed);
            Assert.True(gate.Independent);
            Assert.True(gate.FreshContext);
            await AssertRows(1);
            var snapshot = await H.Snapshot();
            if (cancel) caller.Cancel();
            gate.Release.TrySetResult();
            if (cancel)
            {
                var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
                Assert.Equal(caller.Token, exception.CancellationToken);
            }
            else
            {
                var result = await pending;
                Assert.Equal(unavailable ? ExecutionDocumentPreparationState.Indeterminate : ExecutionDocumentPreparationState.Registered, result.State);
                if (result.Package is not null) await result.Package.DisposeAsync();
            }
            Assert.True(gate.Injected);
            Assert.Equal(snapshot, await H.Snapshot());
            Assert.Equal(0, writes.Count);
            Assert.All(copies.Created, c => Assert.True(c.Disposed));
            var replay = await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token);
            Assert.Equal(ExecutionDocumentPreparationState.AlreadyRegistered, replay.State);
            Assert.Null(replay.Package);
            Assert.Equal(2, files.Opens);
            Assert.Equal(0, files.Deletes);
            foreach (var path in Directory.GetFiles(files.Root, "*.pdf", SearchOption.AllDirectories))
                Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(path, Token));
        }
        finally
        {
            gate.Release.TrySetResult();
            try { var result = await pending; if (result.Package is not null) await result.Package.DisposeAsync(); }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class ObservationGate(Func<bool> enabled, bool fail, CancellationToken original) : DbCommandInterceptor
    {
        internal readonly TaskCompletionSource Reached = Signal(), Release = Signal();
        private readonly HashSet<Guid> earlierContexts = [];
        internal bool Independent, Injected, FreshContext;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!enabled()) earlierContexts.Add(eventData.Context!.ContextId.InstanceId);
            if (enabled() && !Injected)
            {
                Injected = true;
                FreshContext = !earlierContexts.Contains(eventData.Context!.ContextId.InstanceId);
                Independent = cancellationToken != original && cancellationToken.CanBeCanceled;
                Reached.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                if (fail) throw new IOException("Observer unavailable");
            }
            return result;
        }
    }
}
