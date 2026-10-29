namespace Application.PublicExperience;

public interface IIdeaPhotoCapture
{
    Task<IValidatedIdeaPhoto> CaptureAsync(PublicExperienceUpload upload, CancellationToken cancellationToken);
}

public interface IValidatedIdeaPhoto : IAsyncDisposable
{
    string Sha256 { get; }
    long Length { get; }
    string ContentType { get; }
    Stream OpenRead();
}

public sealed class IdeaPhotoValidationException(int statusCode, string code)
    : Exception("La fotografia no cumple los requisitos de recepcion.")
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public static class IdeaPhotoLimits
{
    public const long MaximumBytes = 20L * 1024 * 1024;
    public const long MaximumRequestBytes = 121L * 1024 * 1024;
    public const int MaximumDimension = 8192;
    public const long MaximumPixels = 16_000_000;
}
