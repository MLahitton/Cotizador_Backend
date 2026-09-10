namespace Application.Common.Abstractions.Proposals;

public sealed record FpProReportFile(
    string FileName,
    string ContentType,
    long SizeBytes,
    Stream Content);

public interface IFpProReportParser
{
    Task<FpProReportPreviewData> ParseAsync(
        FpProReportFile file,
        CancellationToken cancellationToken);
}

public sealed record FpProReportPreviewData(
    FpProReportData Report,
    IReadOnlyList<FpProPreviewItemData> Items,
    IReadOnlyList<string> PendingFields);

public sealed record FpProReportData(
    string? OrderId,
    string? Description,
    int? Revision,
    int ItemsDetected,
    decimal? AluminumWastePercent);

public sealed record FpProPreviewItemData(
    string ItemNumber,
    string? Typology,
    IReadOnlyList<string> FpProProfiles,
    IReadOnlyList<string> TechnicalProfileDescriptions,
    IReadOnlyList<FpProTechnicalProfile> TechnicalProfiles,
    int? WidthMm,
    int? HeightMm,
    decimal? WidthM,
    decimal? HeightM,
    int? Quantity,
    decimal? NominalAreaM2,
    string? Notes,
    IReadOnlyList<FpProGlassPaneData> Glass,
    decimal? SelectedThicknessMm,
    string? System,
    string? GlassDescription,
    string? Finish,
    string? Lock,
    decimal? GlassPrice,
    decimal? AluminumBase,
    decimal? AluminumBaseUnit,
    decimal? AccessoriesBase,
    decimal? AccessoriesBaseUnit,
    decimal? StructureWeightKg,
    decimal? StructureWeightKgUnit,
    FpProPreviewImageData? Image,
    IReadOnlyList<string> PendingFields,
    IReadOnlyList<FpProPreviewWarningData> Warnings,
    int? InferredModuleCount = null,
    string? ModuleConfidence = null,
    bool RequiresManualModule = false);

public sealed record FpProGlassPaneData(
    string Code,
    decimal? ThicknessMm,
    int? WidthMm,
    int? HeightMm,
    int? Quantity);

public sealed record FpProTechnicalProfile(
    string Code,
    string Description,
    decimal? TotalLengthMeters,
    decimal? UnitLengthMeters,
    decimal? Quantity);

public sealed record FpProPreviewImageData(
    string ContentType,
    string Base64);

public sealed record FpProPreviewWarningData(
    string Code,
    string? Field,
    string Message);

public interface IQuotationWorkbookGenerator
{
    Task<GeneratedQuotationWorkbook> GenerateAsync(
        QuotationWorkbookRequest request,
        CancellationToken cancellationToken);
}

public sealed class QuotationTemplateCapacityExceededException(int capacity) : InvalidOperationException(
    "The quotation template does not contain enough prepared item blocks.")
{
    public int Capacity { get; } = capacity;
}

public sealed record QuotationWorkbookRequest(
    FpProQuotationReportInput Report,
    string ProposalName,
    string ClientName,
    string ProjectName,
    string ProductionLine,
    string PreparedBy,
    string BudgetId,
    IReadOnlyList<FpProQuotationItemInput> Items);

public sealed record FpProQuotationReportInput(
    string? Order,
    string? Description,
    string Location,
    decimal AluminumWastePercent,
    decimal BenefitPercent,
    decimal CommissionPercent);

public sealed record FpProQuotationItemInput(
    string ItemNumber,
    string Typology,
    decimal WidthM,
    decimal HeightM,
    int Quantity,
    string System,
    string GlassDescription,
    string Finish,
    decimal GlassPrice,
    decimal AccessoriesBase,
    decimal AluminumBase,
    decimal SelectedThicknessMm,
    decimal StructureWeightKg,
    decimal Module,
    string? Notes,
    string ImageBase64);

public sealed record GeneratedQuotationWorkbook(
    string FileName,
    string ContentType,
    byte[] Content);

public interface IQuotationTemplateCatalogReader
{
    Task<QuotationTemplateCatalog> ReadAsync(CancellationToken cancellationToken);
}

public sealed record QuotationTemplateCatalog(
    IReadOnlyList<QuotationTemplateSystemOption> Systems,
    IReadOnlyList<QuotationTemplateCatalogOption> GlassDescriptions,
    IReadOnlyList<QuotationTemplateCatalogOption> Finishes,
    IReadOnlyList<QuotationTemplateCatalogOption> Locations);

public sealed record QuotationTemplateSystemOption(
    string TechnicalName,
    string Label,
    string ExcelValue,
    string? Lock);

public sealed record QuotationTemplateCatalogOption(
    string Label,
    string Value);
