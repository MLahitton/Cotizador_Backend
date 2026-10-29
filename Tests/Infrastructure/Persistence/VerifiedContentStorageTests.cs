using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Application.Common.Abstractions.Storage;
using Infrastructure.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[CollectionDefinition("Private content filesystem", DisableParallelization = true)]
public sealed class PrivateContentFilesystemCollection;

[Collection("Private content filesystem")]
public sealed class VerifiedContentStorageTests : IDisposable
{
    private const string AbcHash = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
    private readonly string root = Directory.CreateTempSubdirectory("sng-content-tests-").FullName;
    private readonly DiagnosticLog log = new();
    private LocalFileStorage Storage => new(new FileStorageOptions(root), Substitute.For<IHostEnvironment>(), log);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InitialCapture_ObservedBytes_SingleReader_RetainedHandle_AndCleanup()
    {
        var before = Spools();
        using var source = new ControlledSource("abc", 1);
        var copy = await SealedContentCopy.CaptureInitialAsync(source, 3, 10, log, Token);
        var directory = Assert.Single(Spools().Except(before));
        try
        {
            Assert.Equal(AbcHash, copy.Sha256);
            Assert.Equal(3, copy.Length);
            Assert.False(source.Disposed);
            using var reader = copy.OpenRead();
            Assert.Equal(0, reader.Position);
            Assert.False(reader.CanWrite);
            Assert.False(reader.CanSeek);
            Assert.Throws<NotSupportedException>(() => reader.WriteByte(1));
            Assert.Throws<NotSupportedException>(() => reader.SetLength(0));
            Assert.Throws<InvalidOperationException>(() => copy.OpenRead());
            var path = Assert.Single(Directory.GetFiles(directory));
            if (OperatingSystem.IsWindows())
                Assert.Throws<IOException>(() => File.OpenRead(path));
            else File.Delete(path);
            using var buffer = new MemoryStream();
            await reader.CopyToAsync(buffer, Token);
            Assert.Equal("abc"u8.ToArray(), buffer.ToArray());
            await copy.DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() => reader.ReadByte());
        }
        finally { await copy.DisposeAsync(); }
        Assert.False(Directory.Exists(directory));
        Assert.False(source.Disposed);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("limit")]
    [InlineData("cancel")]
    public async Task InitialCapture_Failure_CleansOnlyItsSpool(string kind)
    {
        var before = Spools();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var source = new ControlledSource(kind == "short" ? "ab" : "abcd", 1,
            n => { if (kind == "cancel" && n == 2) cancel.Cancel(); });
        if (kind == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SealedContentCopy.CaptureInitialAsync(source, 3, 3, log, cancel.Token));
        else if (kind == "limit")
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SealedContentCopy.CaptureInitialAsync(source, 3, 2, log, Token));
        else
            await Assert.ThrowsAsync<ContentVerificationException>(() => SealedContentCopy.CaptureInitialAsync(source, 3, 3, log, Token));
        Assert.Equal(before, Spools());
        Assert.False(source.Disposed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SaveVerified_PartialNonSeekableSource_UsesLiteralVector(int chunk)
    {
        using var source = new ControlledSource("abc", chunk);
        var receipt = await Storage.SaveVerifiedAsync("target", source, 3, 10, Token);
        Assert.Equal("abc", await File.ReadAllTextAsync(Path.Combine(root, "target"), Token));
        Assert.Equal(AbcHash, receipt.Sha256);
        Assert.Equal("SHA256", receipt.Algorithm);
        Assert.Equal(3, receipt.Length);
        Assert.Equal(3, source.BytesRead);
        Assert.True(source.SawEof);
        Assert.False(source.Disposed);
        Assert.Single(Directory.GetFiles(root));
    }

    [Fact]
    public async Task SaveVerified_ReadsCurrentPosition_WithoutTakingOwnership()
    {
        using var source = new MemoryStream("xabc"u8.ToArray());
        source.Position = 1;
        var receipt = await Storage.SaveVerifiedAsync("target", source, 3, 3, Token);
        Assert.Equal(AbcHash, receipt.Sha256);
        Assert.True(source.CanRead);
        Assert.Equal(4, source.Position);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    [InlineData(3, 0)]
    [InlineData(4, 3)]
    public async Task SaveVerified_InvalidLimits_ReadNothing(long expected, long maximum)
    {
        using var source = new ControlledSource("abc");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Storage.SaveVerifiedAsync("target", source, expected, maximum, Token));
        Assert.Equal(0, source.ReadCalls);
        Assert.Empty(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcd")]
    public async Task SaveVerified_LengthMismatch_NeverPublishes(string input)
    {
        using var source = new ControlledSource(input, 1);
        await Assert.ThrowsAsync<ContentVerificationException>(() => Storage.SaveVerifiedAsync("target", source, 3, 3, Token));
        Assert.Empty(Directory.GetFiles(root));
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task SaveVerified_UnboundedSource_StopsAtFirstExcessByte()
    {
        using var source = new ControlledSource("", unbounded: true);
        await Assert.ThrowsAsync<ContentVerificationException>(() => Storage.SaveVerifiedAsync("target", source, 90000, 90000, Token));
        Assert.Equal(90001, source.BytesRead);
        Assert.Equal(2, source.ReadCalls);
        Assert.Empty(Directory.GetFiles(root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveVerified_SourceFailureOrCancellation_PreservesForeignFiles(bool cancel)
    {
        var foreign = Path.Combine(root, "foreign.tmp");
        await File.WriteAllTextAsync(foreign, "keep", Token);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var source = new ControlledSource("abc", 1, call =>
        {
            if (call != 2) return;
            if (cancel) cts.Cancel(); else throw new IOException("controlled source failure");
        });
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Storage.SaveVerifiedAsync("target", source, 3, 3, cts.Token));
        else
            await Assert.ThrowsAsync<FileStorageWriteException>(() => Storage.SaveVerifiedAsync("target", source, 3, 3, cts.Token));
        Assert.Equal(new[] { foreign }, Directory.GetFiles(root));
        Assert.Equal("keep", await File.ReadAllTextAsync(foreign, Token));
        Assert.False(source.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveVerified_CollisionBeforeOrDuringCopy_NeverOverwrites(bool duringCopy)
    {
        var target = Path.Combine(root, "target");
        if (!duringCopy) await File.WriteAllTextAsync(target, "foreign", Token);
        using var source = new ControlledSource("abc", 1, call =>
        {
            if (duringCopy && call == 1) File.WriteAllText(target, "foreign");
        });
        await Assert.ThrowsAsync<FileStorageConflictException>(() => Storage.SaveVerifiedAsync("target", source, 3, 3, Token));
        Assert.Equal("foreign", await File.ReadAllTextAsync(target, Token));
        Assert.Single(Directory.GetFiles(root));
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task SaveVerified_CannotCreateDirectory_DoesNotDeleteForeignResource()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "parent"), "foreign", Token);
        using var source = new ControlledSource("abc");
        await Assert.ThrowsAsync<FileStorageWriteException>(() => Storage.SaveVerifiedAsync("parent/target", source, 3, 3, Token));
        Assert.Equal(0, source.ReadCalls);
        Assert.Equal("foreign", await File.ReadAllTextAsync(Path.Combine(root, "parent"), Token));
    }

    [Fact]
    public async Task ExistingSaveAsync_StillAcceptsEmptyAndCurrentPosition_AndPreservesCollisions()
    {
        using var empty = new MemoryStream();
        await Storage.SaveAsync("empty", empty, Token);
        Assert.Empty(await File.ReadAllBytesAsync(Path.Combine(root, "empty"), Token));
        using var source = new MemoryStream("xabc"u8.ToArray());
        source.Position = 1;
        await Storage.SaveAsync("original", source, Token);
        using var other = new MemoryStream("changed"u8.ToArray());
        await Assert.ThrowsAsync<FileStorageConflictException>(() => Storage.SaveAsync("original", other, Token));
        Assert.Equal("abc", await File.ReadAllTextAsync(Path.Combine(root, "original"), Token));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task SealedCopy_LiteralVector_OneReadOnlyView_AndOwnerDisposal()
    {
        var before = Spools();
        using var source = new ControlledSource("abc", 1);
        var copy = await SealedContentCopy.PrepareAsync(source, 3, AbcHash, 3, log, Token);
        var directory = Assert.Single(Spools().Except(before));
        try
        {
            Assert.Equal(AbcHash, copy.Sha256);
            Assert.Equal(3, copy.Length);
            Assert.Equal("SHA256", copy.Algorithm);
            Assert.True(source.SawEof);
            Assert.False(source.Disposed);
            using var reader = copy.OpenRead();
            Assert.Equal(0, reader.Position);
            Assert.False(reader.CanWrite);
            Assert.False(reader.CanSeek);
            Assert.IsNotType<FileStream>(reader);
            Assert.Throws<NotSupportedException>(() => reader.WriteByte(1));
            Assert.Throws<NotSupportedException>(() => reader.SetLength(0));
            Assert.Throws<NotSupportedException>(() => reader.SetLength(4));
            Assert.Throws<NotSupportedException>(() => reader.Position = 0);
            Assert.Throws<NotSupportedException>(() => reader.Seek(0, SeekOrigin.Begin));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await reader.WriteAsync(new byte[] { 1 }, Token));
            Assert.Throws<InvalidOperationException>(() => copy.OpenRead());
            var path = Assert.Single(Directory.GetFiles(directory));
            if (OperatingSystem.IsWindows())
            {
                // Exclusive retained handle forbids reopening by name; the view still reads.
                Assert.Throws<IOException>(() => File.OpenRead(path));
                var acl = new DirectoryInfo(directory).GetAccessControl();
                Assert.True(acl.AreAccessRulesProtected);
                using var identity = WindowsIdentity.GetCurrent();
                var rule = Assert.Single(acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>());
                Assert.Equal(identity.User, rule.IdentityReference);
            }
            else
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
                File.Delete(path); // The unlinked handle must remain readable without a name.
            }
            using var output = new MemoryStream();
            await reader.CopyToAsync(output, Token);
            Assert.Equal("abc"u8.ToArray(), output.ToArray());
            await copy.DisposeAsync();
            Assert.False(reader.CanRead);
            Assert.Throws<ObjectDisposedException>(() => reader.ReadByte());
            Assert.Throws<ObjectDisposedException>(() => copy.OpenRead());
            Assert.False(Directory.Exists(directory));
            await copy.DisposeAsync();
        }
        finally { await copy.DisposeAsync(); }
    }

    [Fact]
    public async Task SealedCopy_DisposingViewDoesNotDisposeOwner_AndSourceReplacementIsIrrelevant()
    {
        var sourcePath = Path.Combine(root, "source");
        await File.WriteAllBytesAsync(sourcePath, "abc"u8.ToArray(), Token);
        var before = Spools();
        SealedContentCopy copy;
        await using (var input = File.OpenRead(sourcePath))
            copy = await SealedContentCopy.PrepareAsync(input, 3, AbcHash, 3, log, Token);
        var directory = Assert.Single(Spools().Except(before));
        await using (copy)
        {
            File.Delete(sourcePath);
            await File.WriteAllTextAsync(sourcePath, "replacement", Token);
            var reader = copy.OpenRead();
            using var output = new MemoryStream();
            await reader.CopyToAsync(output, Token);
            Assert.Equal("abc"u8.ToArray(), output.ToArray());
            reader.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reader.ReadByte());
            Assert.True(Directory.Exists(directory));
        }
        Assert.False(Directory.Exists(directory));
        Assert.Equal("replacement", await File.ReadAllTextAsync(sourcePath, Token));
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData("abcd", false)]
    [InlineData("", false)]
    [InlineData("abc", true)]
    public async Task SealedCopy_BadLengthOrDigest_DeliversNoVerifiedObject(string content, bool wrongHash)
    {
        var before = Spools();
        using var source = new ControlledSource(content, 1);
        await Assert.ThrowsAsync<ContentVerificationException>(() => SealedContentCopy.PrepareAsync(source, 3,
            wrongHash ? new string('0', 64) : AbcHash, 3, log, Token));
        Assert.Equal(before, Spools());
        Assert.False(source.Disposed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("ZZ7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    public async Task SealedCopy_RequiresValidExpectedDigestBeforeReading(string? digest)
    {
        var before = Spools();
        using var source = new ControlledSource("abc");
        await Assert.ThrowsAsync<ArgumentException>(() => SealedContentCopy.PrepareAsync(source, 3, digest!, 3, log, Token));
        Assert.Equal(0, source.ReadCalls);
        Assert.Equal(before, Spools());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SealedCopy_SourceErrorOrCancellation_CleansItsResources(bool cancel)
    {
        var before = Spools();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var error = new IOException("controlled source failure");
        using var source = new ControlledSource("abc", 1, call =>
        {
            if (call != 2) return;
            if (cancel) cts.Cancel(); else throw error;
        });
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SealedContentCopy.PrepareAsync(source, 3, AbcHash, 3, log, cts.Token));
        else
            Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => SealedContentCopy.PrepareAsync(source, 3, AbcHash, 3, log, cts.Token)));
        Assert.Equal(before, Spools());
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task SealedCopy_CleanupFailureIsObservable_DoesNotMaskPreparationErrorOrDeleteForeignFile()
    {
        var before = Spools();
        string? directory = null;
        var error = new IOException("primary error");
        using var source = new ControlledSource("abc", 1, _ =>
        {
            directory = Assert.Single(Spools().Except(before));
            File.WriteAllText(Path.Combine(directory, "foreign"), "keep");
            throw error;
        });
        try
        {
            Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => SealedContentCopy.PrepareAsync(source, 3, AbcHash, 3, log, Token)));
            Assert.NotNull(directory);
            Assert.Equal(new[] { Path.Combine(directory, "foreign") }, Directory.GetFiles(directory));
            Assert.Contains(log.Messages, m => m.Contains("cleanup failed") && m.Contains("Directory"));
            Assert.DoesNotContain(log.Messages, m => m.Contains(directory));
        }
        finally
        {
            if (directory is not null) { File.Delete(Path.Combine(directory, "foreign")); Directory.Delete(directory); }
        }
    }

    [Fact]
    public void VerificationResults_HaveNoPublicConstructorsOrSetters()
    {
        Assert.Empty(typeof(StoredContentReceipt).GetConstructors());
        Assert.Empty(typeof(SealedContentCopy).GetConstructors());
        Assert.All(typeof(StoredContentReceipt).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.All(typeof(SealedContentCopy).GetProperties(), p => Assert.Null(p.SetMethod));
    }

    private static string[] Spools() => Directory.GetDirectories(Path.GetTempPath(), "sng-sealed-*").Order().ToArray();
    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class DiagnosticLog : ILogger<LocalFileStorage>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class ControlledSource(string text, int chunk = int.MaxValue, Action<int>? onRead = null, bool unbounded = false) : Stream
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(text);
        private int offset;
        public int ReadCalls { get; private set; }
        public long BytesRead { get; private set; }
        public bool SawEof { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ReadCalls++;
            onRead?.Invoke(ReadCalls);
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, Math.Min(chunk, unbounded ? int.MaxValue : bytes.Length - offset));
            if (unbounded) buffer.Span[..count].Fill(42);
            else { bytes.AsMemory(offset, count).CopyTo(buffer); offset += count; }
            BytesRead += count;
            if (count == 0) SawEof = true;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Only async reads expected.");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
