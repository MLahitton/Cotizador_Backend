using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro.Experimental;

namespace Application.Proposals.FpPro;

public sealed record PreviewFpProReportCommand(FpProReportFile File);

public sealed record PreviewFpProReportResult(
    bool IsSuccess,
    PreviewFpProReportFailure Failure,
    FpProReportPreviewData? Preview)
{
    public static PreviewFpProReportResult Success(FpProReportPreviewData preview) =>
        new(true, PreviewFpProReportFailure.None, preview);

    public static PreviewFpProReportResult Failed(PreviewFpProReportFailure failure) =>
        new(false, failure, null);
}

public enum PreviewFpProReportFailure
{
    None,
    InvalidRequest,
    UnsupportedFileType,
    EmptyFile,
    FileTooLarge,
    InvalidFpProReport,
    ParseError
}

public sealed class PreviewFpProReportService(
    IFpProReportParser parser,
    IFpProPreviewConfigurationResolver configurationResolver,
    IFpProModuleInferenceService moduleInferenceService)
{
    public const long MaximumFileSizeBytes = 20 * 1024 * 1024;
    private const string PdfContentType = "application/pdf";

    public async Task<PreviewFpProReportResult> ExecuteAsync(
        PreviewFpProReportCommand command,
        CancellationToken cancellationToken)
    {
        var file = command.File;
        if (string.IsNullOrWhiteSpace(file.FileName))
        {
            return PreviewFpProReportResult.Failed(
                PreviewFpProReportFailure.InvalidRequest);
        }

        if (!string.Equals(file.ContentType, PdfContentType, StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return PreviewFpProReportResult.Failed(
                PreviewFpProReportFailure.UnsupportedFileType);
        }

        if (file.SizeBytes <= 0)
        {
            return PreviewFpProReportResult.Failed(
                PreviewFpProReportFailure.EmptyFile);
        }

        if (file.SizeBytes > MaximumFileSizeBytes)
        {
            return PreviewFpProReportResult.Failed(
                PreviewFpProReportFailure.FileTooLarge);
        }

        try
        {
            var parsedPreview = await parser.ParseAsync(file, cancellationToken);
            var resolvedPreview = await configurationResolver.ResolveAsync(parsedPreview, cancellationToken);
            var preview = EnrichWithModuleInference(resolvedPreview);
            return preview.Items.Count == 0
                ? PreviewFpProReportResult.Failed(
                    PreviewFpProReportFailure.InvalidFpProReport)
                : PreviewFpProReportResult.Success(preview);
        }
        catch (InvalidDataException)
        {
            return PreviewFpProReportResult.Failed(
                PreviewFpProReportFailure.InvalidFpProReport);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return PreviewFpProReportResult.Failed(
                PreviewFpProReportFailure.ParseError);
        }
    }

    private FpProReportPreviewData EnrichWithModuleInference(FpProReportPreviewData preview)
    {
        var items = preview.Items.Select(EnrichItemWithModuleInference).ToArray();

        return preview with
        {
            Items = items,
            PendingFields = items.SelectMany(item => item.PendingFields)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private FpProPreviewItemData EnrichItemWithModuleInference(FpProPreviewItemData item)
    {
        try
        {
            var inference = moduleInferenceService.Infer(item);
            var requiresManualModule = inference.RequiresManualModule
                || inference.Confidence == FpProModuleInferenceConfidence.Ambiguous;
            int? inferredModuleCount = requiresManualModule ? null : inference.FinalModules;

            return item with
            {
                InferredModuleCount = inferredModuleCount,
                ModuleConfidence = inference.Confidence.ToString(),
                RequiresManualModule = requiresManualModule,
                PendingFields = BuildModulePendingFields(item.PendingFields, inferredModuleCount, requiresManualModule)
            };
        }
        catch
        {
            return item with
            {
                InferredModuleCount = null,
                ModuleConfidence = FpProModuleInferenceConfidence.Ambiguous.ToString(),
                RequiresManualModule = true,
                PendingFields = BuildModulePendingFields(item.PendingFields, null, true)
            };
        }
    }

    private static IReadOnlyList<string> BuildModulePendingFields(
        IReadOnlyList<string> current,
        int? inferredModuleCount,
        bool requiresManualModule)
    {
        var fields = current
            .Where(value => !string.Equals(value, "module", StringComparison.Ordinal))
            .ToList();
        if (requiresManualModule || inferredModuleCount is null or <= 0)
        {
            fields.Add("module");
        }

        return fields
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
