using System.Buffers.Binary;
using Application.PublicExperience;
using Infrastructure.Storage;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

[Collection(PostgreSqlIntegrationCollection.Name)]
public sealed class IdeaPhotoCaptureTests
{
    internal static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAABHNCSVQICAgIfAhkiAAAAC9JREFUWIXtzjEBACAMwLCC8jkHGXtSA81pei12N+cAAAAAAAAAAAAAAAAAAABVHzCKAb88cjm/AAAAAElFTkSuQmCC");
    internal static byte[] Jpeg => Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAgACADASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFgEBAQEAAAAAAAAAAAAAAAAAAAcI/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAwDAQACEQMRAD8AnIBD2bwAAAAAH//Z");
    internal const string PngHash = "08A65B9D47394FAFE293CD5C94E91C3E8E0C78CE3BE6C1C8D014E59C0C3DF621";
    internal static PublicExperienceUpload Upload(byte[]? bytes = null, string name = "idea.png", string mime = "")
    {
        bytes ??= Png;
        return new(name, mime, bytes.Length, () => new MemoryStream(bytes, false));
    }

    [Theory]
    [InlineData("idea.PNG", "")]
    [InlineData("idea.png", "application/octet-stream")]
    [InlineData("idea.png", "image/png")]
    [InlineData("idea.JPG", "")]
    [InlineData("idea.jpeg", "application/octet-stream")]
    [InlineData("idea.jpg", "image/jpeg")]
    public async Task Valid_OriginalBytes_LiteralDigest_SingleOpening_IndependentReaders(string name, string mime)
    {
        var bytes = name.EndsWith("png", StringComparison.OrdinalIgnoreCase) ? Png : Jpeg;
        var opens = 0;
        using var source = new MemoryStream(bytes, false);
        var upload = new PublicExperienceUpload(name, mime, bytes.Length, () =>
        { Assert.Equal(1, ++opens); return source; });
        await using var photo = await new IdeaPhotoCapture().CaptureAsync(upload, TestContext.Current.CancellationToken);
        Assert.False(source.CanRead);
        Assert.Equal(bytes.Length, photo.Length);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), photo.Sha256);
        if (name.EndsWith("png", StringComparison.OrdinalIgnoreCase)) Assert.Equal(PngHash, photo.Sha256);
        for (var i = 0; i < 2; i++)
        {
            await using var reader = photo.OpenRead();
            Assert.False(reader.CanWrite);
            using var copy = new MemoryStream();
            await reader.CopyToAsync(copy, TestContext.Current.CancellationToken);
            Assert.Equal(bytes, copy.ToArray());
        }
        await photo.DisposeAsync(); await photo.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => photo.OpenRead());
    }

    [Theory]
    [InlineData("extension", 415)]
    [InlineData("mime", 415)]
    [InlineData("mismatch", 415)]
    [InlineData("jpeg-extension", 415)]
    [InlineData("unsafe-name", 400)]
    [InlineData("empty", 400)]
    [InlineData("large", 413)]
    [InlineData("corrupt", 400)]
    [InlineData("png-truncated", 400)]
    [InlineData("jpeg-truncated", 400)]
    [InlineData("width", 400)]
    [InlineData("pixels", 400)]
    [InlineData("animation", 400)]
    [InlineData("short", 400)]
    [InlineData("long", 400)]
    public async Task Invalid_RejectsWithoutUsableCapture(string scenario, int status)
    {
        var bytes = scenario switch
        {
            "empty" => [], "corrupt" => "not an image"u8.ToArray(),
            "png-truncated" => Png[..^2], "jpeg-truncated" => Jpeg[..^2],
            "width" => Dimensions(8193, 1), "pixels" => Dimensions(4001, 4000),
            "animation" => Animation(), _ => Png
        };
        var name = scenario switch { "extension" => "a.gif", "jpeg-extension" => "a.jpg", "unsafe-name" => "../a.png", "jpeg-truncated" => "a.jpg", _ => "a.png" };
        var mime = scenario switch { "mime" => "text/plain", "mismatch" => "image/jpeg", _ => "" };
        var upload = Upload(bytes, name, mime);
        upload = upload with { SizeBytes = scenario switch { "large" => IdeaPhotoLimits.MaximumBytes + 1, "short" => bytes.Length + 1, "long" => bytes.Length - 1, _ => bytes.Length } };
        var error = await Assert.ThrowsAsync<IdeaPhotoValidationException>(async () =>
        {
            await using var unexpected = await new IdeaPhotoCapture().CaptureAsync(upload, TestContext.Current.CancellationToken);
        });
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task CancellationWhileWaiting_DoesNotReleaseAnotherCapturesSlot()
    {
        var capture = new IdeaPhotoCapture();
        await using var first = await capture.CaptureAsync(Upload(), TestContext.Current.CancellationToken);
        await using var second = await capture.CaptureAsync(Upload(), TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var pending = capture.CaptureAsync(Upload(), cancellation.Token);
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await second.DisposeAsync();
        await using var next = await capture.CaptureAsync(Upload(), TestContext.Current.CancellationToken);
        await using var reader = first.OpenRead();
        Assert.Equal(137, reader.ReadByte());
    }

    [Theory]
    [InlineData(8192, 1)]
    [InlineData(4000, 4000)]
    public async Task ExactDimensionAndPixelLimits_FullDecodeAccepted(uint width, uint height)
    {
        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Green, width, height);
        var bytes = image.ToByteArray(ImageMagick.MagickFormat.Png);
        await using var photo = await new IdeaPhotoCapture().CaptureAsync(Upload(bytes), TestContext.Current.CancellationToken);
        Assert.Equal(bytes.Length, photo.Length);
    }

    [Fact]
    public async Task PartialNonSeekableSource_CancellationDisposesAndReleasesSlot()
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new CancelSource(Png, cancellation);
        var opens = 0;
        var upload = new PublicExperienceUpload("a.png", "", Png.Length, () => { opens++; return source; });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IdeaPhotoCapture().CaptureAsync(upload, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, opens);
        Assert.True(source.Closed);
        await using var first = await new IdeaPhotoCapture().CaptureAsync(Upload(), TestContext.Current.CancellationToken);
        await using var second = await new IdeaPhotoCapture().CaptureAsync(Upload(), TestContext.Current.CancellationToken);
    }

    private sealed class CancelSource(byte[] bytes, CancellationTokenSource cancellation) : Stream
    {
        private int offset;
        internal bool Closed;
        public override bool CanRead => !Closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (offset == 2) cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            if (offset == bytes.Length) return ValueTask.FromResult(0);
            buffer.Span[0] = bytes[offset++]; return ValueTask.FromResult(1);
        }
        protected override void Dispose(bool disposing) { Closed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // Deliberately invalid dimension fixtures retain a correct IHDR CRC.
    private static byte[] Dimensions(uint width, uint height)
    {
        var bytes = Png;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29), Crc(bytes.AsSpan(12, 17)));
        return bytes;
    }
    private static byte[] Animation()
    {
        var bytes = Png;
        var control = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(control, 2);
        byte[] Frame(uint sequence)
        {
            var data = new byte[26];
            BinaryPrimitives.WriteUInt32BigEndian(data, sequence);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 32);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 32);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(20), 1);
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(22), 10);
            return Chunk("fcTL", data);
        }
        // The literal PNG has one 47-byte IDAT after its sBIT chunk.
        var compressed = bytes.AsSpan(57, 47).ToArray();
        var secondData = new byte[4 + compressed.Length];
        BinaryPrimitives.WriteUInt32BigEndian(secondData, 2);
        compressed.CopyTo(secondData, 4);
        return [..bytes[..33], ..Chunk("acTL", control), ..Frame(0), ..Chunk("IDAT", compressed),
            ..Frame(1), ..Chunk("fdAT", secondData), ..Chunk("IEND", [])];
    }
    private static byte[] Chunk(string type, byte[] data)
    {
        var bytes = new byte[data.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)data.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        data.CopyTo(bytes, 8);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(data.Length + 8), Crc(bytes.AsSpan(4, data.Length + 4)));
        return bytes;
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320U : 0U);
        }
        return ~crc;
    }
}
