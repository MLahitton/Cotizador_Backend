namespace Application.Common.Abstractions.Storage;

// Construction is restricted to the storage implementation, not upload callers.
public sealed class StoredContentReceipt
{
    internal StoredContentReceipt(string sha256, long length)
    {
        Sha256 = sha256;
        Length = length;
    }

    public string Algorithm => "SHA256";
    public string Sha256 { get; }
    public long Length { get; }
}

public sealed class ContentVerificationException : Exception
{
    public ContentVerificationException() : base("Content does not match the required length, limit or digest.") { }
}
