using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.Storage;
using Application.PreQuotes.ProcessRequirement;
using Domain.Identity;
using Infrastructure.DocumentProcessing;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Storage;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using static CotizadorBackend.Tests.Infrastructure.Persistence.JobTerminationTestSupport;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed partial class ExecutionDocumentPreparationPostgreSqlTests
{
    private const string ExtractionPayload = """
        {"requirement":{},"evidence":[],"extraction_metadata":{},"elements":[{"name":"Window","quantity":1,"width_mm":1000,"height_mm":2000}]}
        """;

    private sealed class ConsumingClient : IVerifiedExecutionDocumentClient
    {
        internal int Calls;
        internal Func<DocumentProcessingClientRequest, CancellationToken, Task<DocumentProcessingClientResult>>? Action;
        internal Func<Task>? DuringRead;
        internal bool IgnoreCancellation;
        internal readonly List<Stream> Readers = [];
        internal readonly List<byte[]> Bytes = [];
        internal DocumentProcessingClientRequest? Request;
        public async Task<DocumentProcessingClientResult> ProcessAsync(DocumentProcessingClientRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Request = request;
            var readToken = IgnoreCancellation ? CancellationToken.None : ct;
            foreach (var f in request.Files)
            {
                Readers.Add(f.Content);
                using var bytes = new MemoryStream();
                if (Bytes.Count == 0 && DuringRead is not null)
                {
                    var first = new byte[1];
                    Assert.Equal(1, await f.Content.ReadAsync(first, readToken));
                    bytes.Write(first);
                    await DuringRead();
                }
                await f.Content.CopyToAsync(bytes, readToken);
                Bytes.Add(bytes.ToArray());
            }
            return Action is null ? Response(request) : await Action(request, ct);
        }
        internal static DocumentProcessingClientResult Response(DocumentProcessingClientRequest request) =>
            DocumentProcessingClientResult.Success(new Ai2RequirementExtractionAdapter().Adapt(ExtractionPayload, request));
    }

    private ConsumeExecutionDocumentsService Consumer(Files files, Copies copies, IVerifiedExecutionDocumentClient client,
        params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] hooks) =>
        new(Store(files, copies, hooks), client, NullLogger<ConsumeExecutionDocumentsService>.Instance);

    private async Task DistinctSecondDocument(Files files)
    {
        using var content = new MemoryStream("defg"u8.ToArray());
        var receipt = await files.SaveVerifiedAsync("documents/distinct.pdf", content, 4, 20 * 1024 * 1024, Token);
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.requirement_files SET storage_key={"documents/distinct.pdf"}, size_bytes={receipt.Length}, stored_length_bytes={receipt.Length}, stored_sha256={receipt.Sha256} WHERE storage_key={"documents/1.pdf"}", Token);
    }

    [Fact]
    public async Task Consume_RealBytes_StableResult_AndNoStorageReopen()
    {
        await using var files = new Files();
        var seed = await SeedFiles(files, true);
        await DistinctSecondDocument(files);
        var copies = new Copies();
        var prepared = await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token);
        var package = prepared.Package!;
        var before = await H.Snapshot();
        await File.WriteAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), "xyz"u8.ToArray(), Token);
        var pages = new List<ProcessedPageData> { new(1, "stable", 6, true) };
        var client = new ConsumingClient
        {
            Action = (r, _) => Task.FromResult(DocumentProcessingClientResult.Success(ConsumingClient.Response(r).Response! with { Pages = pages }))
        };
        var writes = new JobProtocolTestSupport.WriteProbe();
        var result = await Consumer(files, copies, client, writes).ConsumeAsync(package, seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentConsumptionState.ValidResponse, result.State);
        Assert.True(result.ClientInvoked);
        Assert.Equal(1, client.Calls); Assert.Equal(2, files.Opens); Assert.Equal(0, files.Deletes);
        Assert.Equal("abc"u8.ToArray(), client.Bytes[0]);
        Assert.Equal("defg"u8.ToArray(), client.Bytes[1]);
        Assert.Equal(package.Evidence.Files.Select(f => f.FileId), client.Request!.Files.Select(f => f.DocumentId));
        Assert.Equal(package.Evidence.PrimaryDocumentId, client.Request.DocumentId);
        Assert.Equal(seed.Execution.ProcessingAttemptId, client.Request.ProcessingAttemptId);
        Assert.Equal(package.Evidence.Files.Select(f => f.OriginalFileName), client.Request.Files.Select(f => f.FileName));
        Assert.Equal(new long[] { 3, 4 }, client.Request.Files.Select(f => f.SizeBytes));
        Assert.All(client.Request.Files, f => Assert.Equal("application/pdf", f.ContentType));
        Assert.Same(package.Evidence, result.Result!.Evidence);
        pages.Clear();
        var response = result.Result.ReadResponse();
        Assert.Equal("stable", Assert.Single(response.Pages).Text);
        ((IList<ProcessedPageData>)response.Pages).Clear();
        Assert.Single(result.Result.ReadResponse().Pages);
        Assert.Equal(package.Evidence.PrimaryDocumentId, result.Result.ReadResponse().DocumentId);
        Assert.Equal(0, writes.Count); Assert.Equal(before, await H.Snapshot());
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
        Assert.All(client.Readers, r => Assert.False(r.CanRead));
        var repeat = await Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentConsumptionState.AlreadyConsumed, repeat.State);
        Assert.False(repeat.ClientInvoked); Assert.Equal(1, client.Calls);
        Assert.Equal("xyz"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
    }

    [Fact]
    public async Task Consume_ConcurrentReservation_LoserCannotDisposeWinner_AndNoLocksDuringClient()
    {
        await using var files = new Files();
        var seed = await SeedFiles(files, true); var copies = new Copies();
        var package = (await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        var reached = Signal(); var release = Signal();
        var client = new ConsumingClient
        {
            DuringRead = async () =>
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            }
        };
        var first = Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, Token);
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            var loser = await Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, Token);
            Assert.Equal(ExecutionDocumentConsumptionState.AlreadyConsumed, loser.State);
            Assert.All(copies.Created, c => Assert.False(c.Disposed));
            Assert.Throws<InvalidOperationException>(() => package.OpenRead(package.Evidence.PrimaryDocumentId));
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync(Token);
            await using var transaction = await connection.BeginTransactionAsync(Token);
            foreach (var table in new[] { "core.public_experience_submissions", "identity.users", "core.clients", "core.projects",
                "core.pre_quotes", "core.requirements", "core.requirement_processing_jobs", "core.requirement_processing_attempts",
                "core.requirement_files", "core.requirement_execution_evidence", "core.requirement_execution_documents" })
            {
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = $"SELECT * FROM {table} FOR UPDATE NOWAIT";
                await using var reader = await command.ExecuteReaderAsync(Token);
                Assert.True(await reader.ReadAsync(Token));
            }
            output.WriteLine($"All send protocol locks available during client, PID {connection.ProcessID}");
            release.TrySetResult();
            Assert.Equal(ExecutionDocumentConsumptionState.ValidResponse, (await first).State);
            Assert.Equal(1, client.Calls);
        }
        finally { release.TrySetResult(); await first; await package.DisposeAsync(); }
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
    }

    [Theory]
    [InlineData("foreign")] [InlineData("admin")] [InlineData("owner")] [InlineData("user")]
    [InlineData("client")] [InlineData("project")] [InlineData("cancel")] [InlineData("lease")]
    [InlineData("state")] [InlineData("request")] [InlineData("lease-owner")] [InlineData("links")]
    [InlineData("evidence")] [InlineData("registered-time")]
    public async Task Consume_AuthorityRejected_BurnsReservation_WithoutWrites(string mode)
    {
        await using var files = new Files();
        var seed = await SeedFiles(files, true); var copies = new Copies();
        var package = (await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        var actor = seed.Actor; var execution = seed.Execution;
        await using (var db = fixture.CreateDbContext())
        {
            if (mode is "foreign" or "admin" or "owner")
            {
                var other = User.CreateFromGoogle("consume-other@example.test", "Other", null, null, await H.Now());
                if (mode == "admin") other.ChangeRole(UserRole.Admin, await H.Now());
                db.Add(other); await db.SaveChangesAsync(Token);
                if (mode == "owner") await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE core.projects SET created_by_user_id={other.Id}", Token);
                else actor = other.Id;
            }
            if (mode == "user") await db.Database.ExecuteSqlRawAsync("UPDATE identity.users SET is_active=false", Token);
            if (mode == "client") await db.Database.ExecuteSqlRawAsync("UPDATE core.clients SET is_active=false", Token);
            if (mode == "project") await db.Database.ExecuteSqlRawAsync("UPDATE core.projects SET is_active=false", Token);
            if (mode == "cancel") await H.Call("request", seed);
            if (mode == "lease") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_processing_jobs SET lease_expires_at_utc=clock_timestamp()-interval '1 second'", Token);
            if (mode == "state") await H.Call("fail", seed);
            if (mode == "request") execution = execution with { ExecutionRequestId = Guid.NewGuid() };
            if (mode == "lease-owner") execution = execution with { LeaseOwner = "other" };
            if (mode == "links") await db.Database.ExecuteSqlRawAsync("UPDATE core.public_experience_submissions SET requirement_id=NULL", Token);
            if (mode == "evidence") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_execution_documents SET original_file_name='altered.pdf'", Token);
            if (mode == "registered-time") await db.Database.ExecuteSqlRawAsync("UPDATE core.requirement_execution_evidence SET registered_at_utc=registered_at_utc+interval '1 second'", Token);
        }
        var before = await H.Snapshot(); var writes = new JobProtocolTestSupport.WriteProbe(); var client = new ConsumingClient();
        var result = await Consumer(files, copies, client, writes).ConsumeAsync(package, execution, actor, Token);
        Assert.Equal(ExecutionDocumentConsumptionState.Rejected, result.State);
        Assert.False(result.ClientInvoked); Assert.Equal(0, client.Calls); Assert.Null(result.Result);
        Assert.NotEqual(ExecutionDocumentPreparationFailure.PersistenceError, result.Rejection);
        Assert.Equal(before, await H.Snapshot()); Assert.Equal(0, writes.Count);
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
        Assert.Equal(2, files.Opens); Assert.Equal(0, files.Deletes);
        Assert.Equal(ExecutionDocumentConsumptionState.AlreadyConsumed,
            (await Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, Token)).State);
    }

    [Fact]
    public async Task Consume_PreviouslyClaimedReader_PreventsAnyClientInvocation()
    {
        await using var files = new Files(); var seed = await SeedFiles(files); var copies = new Copies();
        var package = (await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        using (var used = package.OpenRead(package.Evidence.Files[1].FileId)) Assert.Equal((int)'a', used.ReadByte());
        var client = new ConsumingClient();
        var result = await Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentConsumptionState.Rejected, result.State);
        Assert.Equal(0, client.Calls); Assert.False(result.ClientInvoked);
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
    }

    [Theory]
    [InlineData("document")] [InlineData("attempt")] [InlineData("json-document")] [InlineData("json-attempt")]
    public async Task Consume_IncompatibleResponse_IsRejected(string mode)
    {
        await using var files = new Files(); var seed = await SeedFiles(files); var copies = new Copies();
        var package = (await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        var client = new ConsumingClient { Action = (r, _) =>
        {
            var response = ConsumingClient.Response(r).Response!;
            if (mode == "document") response = response with { DocumentId = Guid.NewGuid() };
            if (mode == "attempt") response = response with { ProcessingAttemptId = Guid.NewGuid() };
            if (mode.StartsWith("json-"))
            {
                var json = JsonNode.Parse(response.PayloadJson)!;
                json[mode == "json-document" ? "documentId" : "processingAttemptId"] = Guid.NewGuid();
                response = response with { PayloadJson = json.ToJsonString() };
            }
            return Task.FromResult(DocumentProcessingClientResult.Success(response));
        } };
        var result = await Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentConsumptionState.InvalidResponse, result.State);
        Assert.True(result.ClientInvoked); Assert.Null(result.Result); Assert.Equal(1, client.Calls);
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
    }

    [Theory]
    [InlineData("failure")] [InlineData("cancel")] [InlineData("late-response")] [InlineData("pre-cancel")]
    public async Task Consume_FailureAndCancellation_NeverRetry_AndDrainBeforeDisposal(string mode)
    {
        await using var files = new Files(); var seed = await SeedFiles(files); var copies = new Copies();
        var package = (await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var reached = Signal(); var release = Signal();
        var client = new ConsumingClient { Action = async (r, ct) =>
        {
            reached.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            Assert.All(r.Files, f => Assert.True(f.Content.CanRead));
            if (mode == "failure") throw new IOException("Lost response after consuming bytes");
            if (mode == "cancel") ct.ThrowIfCancellationRequested();
            return ConsumingClient.Response(r);
        } };
        if (mode is "cancel" or "late-response")
        {
            client.IgnoreCancellation = mode == "late-response";
            client.DuringRead = async () =>
            {
                reached.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            };
        }
        if (mode == "pre-cancel") caller.Cancel();
        var pending = Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, caller.Token);
        try
        {
            if (mode != "pre-cancel")
            {
                await reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
                if (mode != "failure") caller.Cancel();
                Assert.All(copies.Created, c => Assert.False(c.Disposed));
                release.TrySetResult();
            }
            if (mode == "failure") Assert.Equal(ExecutionDocumentConsumptionState.ProviderFailure, (await pending).State);
            else Assert.Equal(caller.Token, (await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending)).CancellationToken);
        }
        finally
        {
            release.TrySetResult();
            try { await pending; } catch (OperationCanceledException) { }
        }
        Assert.All(copies.Created, c => Assert.True(c.Disposed));
        Assert.All(client.Readers, r => Assert.False(r.CanRead));
        Assert.Equal(ExecutionDocumentConsumptionState.AlreadyConsumed,
            (await Consumer(files, copies, client).ConsumeAsync(package, seed.Execution, seed.Actor, Token)).State);
        Assert.Equal(mode == "pre-cancel" ? 0 : 1, client.Calls); Assert.Equal(2, files.Opens); Assert.Equal(0, files.Deletes);
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
    }

    [Theory]
    [InlineData("ok")] [InlineData("network")] [InlineData("redirect")] [InlineData("invalid")]
    public async Task Consume_ActualAi2Client_OneTransportInvocation_ConsumesMultipart(string mode)
    {
        await using var files = new Files(); var seed = await SeedFiles(files); var copies = new Copies();
        await DistinctSecondDocument(files);
        var package = (await Store(files, copies).PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        var before = await H.Snapshot();
        await File.WriteAllBytesAsync(Path.Combine(files.Root, "documents/distinct.pdf"), "xyz"u8.ToArray(), Token);
        var handler = new MultipartHandler(mode);
        using var client = new SingleRequestAi2Client(new(new Uri("https://ai2.invalid/"), 60, 1024 * 1024, true),
            new Ai2RequirementExtractionAdapter(), NullLogger<CotizadorAi2DocumentProcessingClient>.Instance, handler);
        var countedClient = new CountingClient(client);
        var diagnosticRoot = Directory.CreateDirectory(Path.Combine(files.Root, "diagnostics")).FullName;
        var previousDirectory = Directory.GetCurrentDirectory();
        var previousEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        ExecutionDocumentConsumptionResult result;
        try
        {
            Directory.SetCurrentDirectory(diagnosticRoot);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            result = await Consumer(files, copies, countedClient).ConsumeAsync(package, seed.Execution, seed.Actor, Token);
            Assert.Empty(Directory.GetFiles(diagnosticRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.SetCurrentDirectory(previousDirectory);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", previousEnvironment);
        }
        Assert.Equal(mode == "ok" ? ExecutionDocumentConsumptionState.ValidResponse
            : mode == "invalid" ? ExecutionDocumentConsumptionState.InvalidResponse : ExecutionDocumentConsumptionState.ProviderFailure, result.State);
        Assert.Equal(1, countedClient.Calls); Assert.Equal(1, handler.Calls); Assert.Equal(2, handler.Documents.Count);
        Assert.Equal(package.Evidence.Files.Select(f => f.OriginalFileName), handler.Documents.Select(d => d.Name));
        Assert.Equal("abc"u8.ToArray(), handler.Documents[0].Bytes);
        Assert.Equal("defg"u8.ToArray(), handler.Documents[1].Bytes);
        Assert.All(handler.Documents, d => Assert.Equal("application/pdf", d.Mime));
        for (var i = 0; i < handler.Documents.Count; i++)
            Assert.Equal(package.Evidence.Files[i].ObservedSha256,
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(handler.Documents[i].Bytes)));
        Assert.Equal(seed.Execution.ProcessingAttemptId!.Value.ToString("D"), handler.Attempt);
        Assert.Equal(2, files.Opens); Assert.Equal(0, files.Deletes);
        Assert.All(copies.Created, c => Assert.True(c.Disposed)); Assert.Equal(before, await H.Snapshot());
        Assert.Equal(ExecutionDocumentConsumptionState.AlreadyConsumed,
            (await Consumer(files, copies, countedClient).ConsumeAsync(package, seed.Execution, seed.Actor, Token)).State);
        Assert.Equal(1, countedClient.Calls); Assert.Equal(1, handler.Calls);
        output.WriteLine($"AI2 client invocations={countedClient.Calls}, handler invocations={handler.Calls}, complete multipart documents={handler.Documents.Count}, mode={mode}");
    }

    private sealed class CountingClient(IVerifiedExecutionDocumentClient inner) : IVerifiedExecutionDocumentClient
    {
        internal int Calls;
        public Task<DocumentProcessingClientResult> ProcessAsync(DocumentProcessingClientRequest request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return inner.ProcessAsync(request, ct); }
    }

    [Fact]
    public async Task Consume_CleanupAndLoggerFailures_DoNotReplaceProviderFailure_OrSkipRemainingCopies()
    {
        await using var files = new Files(); var seed = await SeedFiles(files);
        var cleanup = new FailingCleanupCopies();
        var log = new FailingLogger<ExecutionDocumentPreparationStore>();
        var store = new ExecutionDocumentPreparationStore(H.Options(), files, cleanup, log);
        var package = (await store.PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        var client = new ConsumingClient { Action = (_, _) => throw new IOException("Provider failed after consumption") };
        var consumerLog = new FailingLogger<ConsumeExecutionDocumentsService>();
        var result = await new ConsumeExecutionDocumentsService(store, client, consumerLog)
            .ConsumeAsync(package, seed.Execution, seed.Actor, Token);
        Assert.Equal(ExecutionDocumentConsumptionState.ProviderFailure, result.State);
        Assert.True(result.ClientInvoked); Assert.Equal(1, client.Calls);
        Assert.Equal(2, cleanup.CopyDisposals); Assert.Equal(2, cleanup.ReaderDisposals);
        Assert.Equal(2, log.Calls); Assert.Equal(2, consumerLog.Calls);
        Assert.All(client.Readers, r => Assert.False(r.CanRead));
        Assert.Equal(0, files.Deletes); Assert.Equal(2, files.Opens);
        Assert.Equal("abc"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(files.Root, "documents/0.pdf"), Token));
    }

    private sealed class FailingLogger<T> : ILogger<T>
    {
        internal int Calls;
        public bool IsEnabled(LogLevel level) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        { Calls++; throw new IOException("Diagnostic sink failure"); }
    }

    private sealed class FailingCleanupCopies : IVerifiedContentCopy
    {
        internal int CopyDisposals, ReaderDisposals;
        public async Task<ICapturedContent> PrepareAsync(Stream source, long length, string hash, long maximum, CancellationToken ct)
        {
            var inner = await new VerifiedContentCopy(NullLogger<VerifiedContentCopy>.Instance).PrepareAsync(source, length, hash, maximum, ct);
            return new FailingCopy(inner, this);
        }
        private sealed class FailingCopy(ICapturedContent inner, FailingCleanupCopies owner) : ICapturedContent
        {
            public string Sha256 => inner.Sha256;
            public long Length => inner.Length;
            public Stream OpenRead() => new FailingReader(inner.OpenRead(), owner);
            public async ValueTask DisposeAsync()
            { await inner.DisposeAsync(); owner.CopyDisposals++; throw new IOException("Copy cleanup failed after close"); }
        }
        private sealed class FailingReader(Stream inner, FailingCleanupCopies owner) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
            public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
            public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) => inner.ReadAsync(b, ct);
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long l) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
            public override async ValueTask DisposeAsync()
            { await inner.DisposeAsync(); owner.ReaderDisposals++; GC.SuppressFinalize(this); throw new IOException("Reader cleanup failed after close"); }
        }
    }

    [Fact]
    public async Task Consume_ClientReturnsWithOutstandingRead_DrainsBeforeReleasingCopies()
    {
        await using var files = new Files(); var seed = await SeedFiles(files);
        var copies = new BlockingReadCopies();
        var store = new ExecutionDocumentPreparationStore(H.Options(), files, copies, NullLogger<ExecutionDocumentPreparationStore>.Instance);
        var package = (await store.PrepareAsync(seed.Execution, seed.Actor, Token)).Package!;
        var client = new EarlyReturningClient();
        var pending = new ConsumeExecutionDocumentsService(store, client, NullLogger<ConsumeExecutionDocumentsService>.Instance)
            .ConsumeAsync(package, seed.Execution, seed.Actor, Token);
        try
        {
            await copies.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            await client.Returned.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            while (client.Reader!.CanRead) { deadline.Token.ThrowIfCancellationRequested(); await Task.Yield(); }
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, copies.Disposals);
            copies.Release.TrySetResult();
            Assert.Equal(ExecutionDocumentConsumptionState.ValidResponse, (await pending).State);
            Assert.Equal(1, await client.Read!);
            Assert.Equal(2, copies.Disposals);
            Assert.Equal(0, files.Deletes);
        }
        finally { copies.Release.TrySetResult(); await pending; if (client.Read is not null) await client.Read; }
    }

    private sealed class EarlyReturningClient : IVerifiedExecutionDocumentClient
    {
        internal Task<int>? Read;
        internal Stream? Reader;
        internal readonly TaskCompletionSource Returned = Signal();
        public Task<DocumentProcessingClientResult> ProcessAsync(DocumentProcessingClientRequest request, CancellationToken ct)
        {
            Reader = request.Files[0].Content;
            Read = Reader.ReadAsync(new byte[1].AsMemory(), ct).AsTask();
            Returned.TrySetResult();
            return Task.FromResult(ConsumingClient.Response(request));
        }
    }

    private sealed class BlockingReadCopies : IVerifiedContentCopy
    {
        internal readonly TaskCompletionSource Reached = Signal(), Release = Signal();
        internal int Disposals;
        public async Task<ICapturedContent> PrepareAsync(Stream source, long length, string hash, long maximum, CancellationToken ct) =>
            new Copy(await new VerifiedContentCopy(NullLogger<VerifiedContentCopy>.Instance).PrepareAsync(source, length, hash, maximum, ct), this);
        private sealed class Copy(ICapturedContent inner, BlockingReadCopies owner) : ICapturedContent
        {
            public string Sha256 => inner.Sha256;
            public long Length => inner.Length;
            public Stream OpenRead() => new Reader(inner.OpenRead(), owner);
            public async ValueTask DisposeAsync() { await inner.DisposeAsync(); owner.Disposals++; }
        }
        private sealed class Reader(Stream inner, BlockingReadCopies owner) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
            public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
            public override async ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default)
            {
                owner.Reached.TrySetResult();
                await owner.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
                return await inner.ReadAsync(b, ct);
            }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long l) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
            public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }
        }
    }

    private sealed class MultipartHandler(string mode) : HttpMessageHandler
    {
        internal int Calls;
        internal string? Attempt;
        internal readonly List<(string Name, string? Mime, byte[] Bytes)> Documents = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Attempt = request.Headers.GetValues("X-Processing-Attempt-ID").Single();
            using var wire = new MemoryStream();
            await request.Content!.CopyToAsync(wire, ct);
            wire.Position = 0;
            var boundary = request.Content.Headers.ContentType!.Parameters.Single(p => p.Name == "boundary").Value!.Trim('"');
            var multipart = new MultipartReader(boundary, wire);
            MultipartSection? section;
            while ((section = await multipart.ReadNextSectionAsync(ct)) is not null)
            {
                var disposition = Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.Parse(section.ContentDisposition);
                using var bytes = new MemoryStream(); await section.Body.CopyToAsync(bytes, ct);
                if (disposition.FileName.HasValue)
                    Documents.Add((disposition.FileName.Value!.Trim('"'), section.ContentType, bytes.ToArray()));
            }
            if (mode == "network") throw new HttpRequestException("Lost response");
            return new HttpResponseMessage(mode == "redirect" ? HttpStatusCode.TemporaryRedirect : HttpStatusCode.OK)
            { Content = new StringContent(mode == "invalid" ? "{}" : ExtractionPayload, Encoding.UTF8, "application/json") };
        }
    }
}
