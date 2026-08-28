namespace Application.PreQuotes.CreateRequirement;

internal sealed record NormalizedRequirementFile(
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string StorageExtension,
    Stream? Content,
    CreateRequirementFailure? Failure)
{
    public static NormalizedRequirementFile Success(
        string originalFileName,
        string contentType,
        long sizeBytes,
        string storageExtension,
        Stream content) =>
        new(originalFileName, contentType, sizeBytes, storageExtension, content, null);

    public static NormalizedRequirementFile Failed(
        CreateRequirementFailure failure) =>
        new(string.Empty, string.Empty, 0, string.Empty, null, failure);
}

internal static class RequirementFileValidation
{
    public const int MaximumFileCount = 10;
    public const long MaximumFileSizeBytes = 20 * 1024 * 1024;
    public const long MaximumTotalSizeBytes = 100 * 1024 * 1024;

    private const string PdfContentType = "application/pdf";
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string JpegContentType = "image/jpeg";
    private const string PngContentType = "image/png";

    private static readonly Dictionary<string, string>
        SupportedExtensionsByContentType =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".pdf"] = PdfContentType,
                [".xlsx"] = XlsxContentType,
                [".jpg"] = JpegContentType,
                [".jpeg"] = JpegContentType,
                [".png"] = PngContentType
            };

    public static NormalizedRequirementFile NormalizeFile(
        CreateRequirementFileInput file)
    {
        var originalFileName = file.OriginalFileName?.Trim();
        if (string.IsNullOrWhiteSpace(originalFileName)
            || originalFileName.Length > 255)
        {
            return NormalizedRequirementFile.Failed(
                CreateRequirementFailure.InvalidFileName);
        }

        var contentType = file.ContentType?.Trim();
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return NormalizedRequirementFile.Failed(
                CreateRequirementFailure.UnsupportedFileType);
        }

        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension)
            || !SupportedExtensionsByContentType.TryGetValue(
                extension,
                out var requiredContentType)
            || !string.Equals(
                requiredContentType,
                contentType,
                StringComparison.OrdinalIgnoreCase))
        {
            return NormalizedRequirementFile.Failed(
                CreateRequirementFailure.UnsupportedFileType);
        }

        if (file.SizeBytes < 0)
        {
            return NormalizedRequirementFile.Failed(
                CreateRequirementFailure.InvalidRequest);
        }

        if (file.SizeBytes == 0)
        {
            return NormalizedRequirementFile.Failed(
                CreateRequirementFailure.EmptyFile);
        }

        if (file.SizeBytes > MaximumFileSizeBytes)
        {
            return NormalizedRequirementFile.Failed(
                CreateRequirementFailure.FileTooLarge);
        }

        var storageExtension = string.Equals(
            extension,
            ".jpeg",
            StringComparison.OrdinalIgnoreCase)
            ? ".jpeg"
            : extension.ToLowerInvariant();

        return NormalizedRequirementFile.Success(
            originalFileName,
            requiredContentType,
            file.SizeBytes,
            storageExtension,
            file.Content!);
    }

    public static string CreateStorageKey(
        Guid requirementId,
        string extension) =>
        $"requirements/{requirementId:D}/{Guid.NewGuid():D}/original{extension}";
}
