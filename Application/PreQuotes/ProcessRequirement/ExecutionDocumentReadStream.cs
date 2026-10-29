namespace Application.PreQuotes.ProcessRequirement;

// Transport completion is not used as proof that every outstanding read has ended.
internal sealed class ExecutionDocumentReadStream(Stream inner, long length) : Stream
{
    private readonly object gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int activeReads;
    private bool closing;
    private Task? disposal;

    private void EnterRead()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            activeReads++;
        }
    }

    private void ExitRead()
    {
        lock (gate)
        {
            activeReads--;
            if (closing && activeReads == 0) drained.TrySetResult();
        }
    }

    public override bool CanRead { get { lock (gate) return !closing && inner.CanRead; } }
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        EnterRead();
        try { return inner.Read(buffer, offset, count); }
        finally { ExitRead(); }
    }
    public override int Read(Span<byte> buffer)
    {
        EnterRead();
        try { return inner.Read(buffer); }
        finally { ExitRead(); }
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        EnterRead();
        try { return await inner.ReadAsync(buffer, ct); }
        finally { ExitRead(); }
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closing = true;
            if (activeReads == 0) drained.TrySetResult();
            disposal ??= CloseAsync();
            return new(disposal);
        }
    }

    private async Task CloseAsync()
    {
        await drained.Task;
        await inner.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.Dispose(disposing);
    }
}
