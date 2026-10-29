using System.Security.AccessControl;
using System.Security.Principal;
using Application.Common.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Storage;

// One local reader; no send tracking, reference authorization or crash recovery.
internal sealed class SealedContentCopy : ICapturedContent
{
    private readonly FileStream handle;
    private readonly string filePath;
    private readonly string directoryPath;
    private readonly ILogger logger;
    private int disposed;
    private int readerClaimed;

    private SealedContentCopy(FileStream handle, string filePath, string directoryPath,
        string sha256, long length, ILogger logger)
    {
        this.handle = handle;
        this.filePath = filePath;
        this.directoryPath = directoryPath;
        this.logger = logger;
        Sha256 = sha256;
        Length = length;
    }

    public string Algorithm => "SHA256";
    public string Sha256 { get; }
    public long Length { get; }

    public static async Task<SealedContentCopy> PrepareAsync(Stream source, long expectedLength,
        string expectedSha256, long maximumLength, ILogger logger, CancellationToken cancellationToken)
    {
        VerifiedContentIO.Validate(source, expectedLength, maximumLength);
        ArgumentNullException.ThrowIfNull(logger);
        if (expectedSha256 is null || expectedSha256.Length != 64
            || expectedSha256.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArgumentException("An uppercase SHA256 digest is required.", nameof(expectedSha256));
        return await CreateAsync(source, expectedLength, expectedSha256, maximumLength, logger, cancellationToken);
    }

    internal static Task<SealedContentCopy> CaptureInitialAsync(Stream source, long declaredLength,
        long maximumLength, ILogger logger, CancellationToken cancellationToken)
    {
        VerifiedContentIO.Validate(source, declaredLength, maximumLength);
        ArgumentNullException.ThrowIfNull(logger);
        return CreateAsync(source, declaredLength, null, maximumLength, logger, cancellationToken);
    }

    private static async Task<SealedContentCopy> CreateAsync(Stream source, long expectedLength,
        string? expectedSha256, long maximumLength, ILogger logger, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryInfo? directory = null;
        string? file = null;
        FileStream? handle = null;
        var ownedFile = false;
        try
        {
            directory = Directory.CreateTempSubdirectory("sng-sealed-");
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None, AccessControlType.Allow));
                directory.SetAccessControl(security);
            }
            else
            {
                File.SetUnixFileMode(directory.FullName,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            file = Path.Combine(directory.FullName, "content.tmp");
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            handle = new FileStream(file, options);
            ownedFile = true;
            var evidence = await VerifiedContentIO.CopyAsync(source, handle, expectedLength, maximumLength, cancellationToken);
            if (expectedSha256 is not null && evidence.Sha256 != expectedSha256) throw new ContentVerificationException();
            await handle.FlushAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            handle.Position = 0;
            return new SealedContentCopy(handle, file, directory.FullName, evidence.Sha256, evidence.Length, logger);
        }
        catch
        {
            if (handle is not null) await CloseAsync(handle, logger);
            if (ownedFile) VerifiedContentIO.DeleteFile(file!, logger);
            if (directory is not null) VerifiedContentIO.DeleteDirectory(directory.FullName, logger);
            throw;
        }
    }

    public Stream OpenRead()
    {
        ThrowIfDisposed();
        if (Interlocked.Exchange(ref readerClaimed, 1) != 0)
            throw new InvalidOperationException("The sealed copy has already supplied its reader.");
        return new ReadView(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await CloseAsync(handle, logger);
        VerifiedContentIO.DeleteFile(filePath, logger);
        VerifiedContentIO.DeleteDirectory(directoryPath, logger);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    private static async ValueTask CloseAsync(FileStream stream, ILogger logger)
    {
        try { await stream.DisposeAsync(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Verified content cleanup failed. Resource=Handle ErrorType={ErrorType}", e.GetType().Name);
        }
    }

    // Disposing the view ends reading, but only the owner disposes the retained handle.
    private sealed class ReadView(SealedContentCopy owner) : Stream
    {
        private bool closed;
        private void Check() { ObjectDisposedException.ThrowIf(closed, this); owner.ThrowIfDisposed(); }
        public override bool CanRead => !closed && Volatile.Read(ref owner.disposed) == 0;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length { get { Check(); return owner.Length; } }
        public override long Position { get { Check(); return owner.handle.Position; } set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Check(); return owner.handle.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { Check(); return owner.handle.Read(buffer); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { Check(); return owner.handle.ReadAsync(buffer, ct); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override void Flush() { Check(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { closed = true; base.Dispose(disposing); }
    }
}
