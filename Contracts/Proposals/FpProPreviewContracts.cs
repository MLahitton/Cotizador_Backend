namespace Contracts.Proposals;

public static class FpProPreviewErrorCodes
{
    public const string InvalidRequest = "FP_PRO_PREVIEW_INVALID_REQUEST";
    public const string UnsupportedFileType = "FP_PRO_PREVIEW_UNSUPPORTED_FILE_TYPE";
    public const string EmptyFile = "FP_PRO_PREVIEW_EMPTY_FILE";
    public const string FileTooLarge = "FP_PRO_PREVIEW_FILE_TOO_LARGE";
    public const string InvalidReport = "FP_PRO_PREVIEW_INVALID_REPORT";
    public const string ParseError = "FP_PRO_PREVIEW_PARSE_ERROR";
    public const string GenerateInvalidRequest = "FP_PRO_GENERATE_INVALID_REQUEST";
    public const string GenerateUnsupportedItemCount = "FP_PRO_GENERATE_UNSUPPORTED_ITEM_COUNT";
    public const string GenerateUnknownCatalogValue = "FP_PRO_GENERATE_UNKNOWN_CATALOG_VALUE";
    public const string GenerateError = "FP_PRO_GENERATE_ERROR";
}

public sealed record GenerateFpProQuotationRequest(
    GenerateFpProQuotationReportRequest Report,
    IReadOnlyList<GenerateFpProQuotationItemRequest> Items,
    string? ProposalName,
    string? ClientName,
    string? ProjectName,
    string? ProductionLine,
    string? PreparedBy,
    string? BudgetId,
    decimal? AluminumWastePercent,
    decimal? BenefitPercent,
    decimal? CommissionPercent);

public sealed record GenerateFpProQuotationReportRequest(
    string? Order,
    string? Description,
    string? Location,
    int? ProfileBarCount,
    int? DoorCount);

public sealed record GenerateFpProQuotationItemRequest(
    string? ItemNumber,
    string? Typology,
    decimal? WidthM,
    decimal? HeightM,
    int? Quantity,
    string? System,
    string? GlassDescription,
    string? Finish,
    decimal? GlassPrice,
    decimal? AccessoriesBase,
    decimal? AluminumBase,
    decimal? SelectedThicknessMm,
    decimal? StructureWeightKg,
    decimal? Module,
    string? Notes,
    string? ImageBase64);

public sealed record FpProPreviewResponse(
    FpProReportResponse Report,
    IReadOnlyList<FpProPreviewItemResponse> Items,
    IReadOnlyList<string> PendingFields);

public sealed record FpProReportResponse(
    string? OrderId,
    string? Description,
    int? Revision,
    int ItemsDetected,
    int? StructureCount,
    decimal? AluminumWastePercent,
    int? ProfileBarCount,
    int? DoorCount);

public sealed record FpProPreviewItemResponse(
    string ItemNumber,
    string? Typology,
    IReadOnlyList<string> FpProProfiles,
    int? WidthMm,
    int? HeightMm,
    decimal? WidthM,
    decimal? HeightM,
    int? Quantity,
    decimal? NominalAreaM2,
    string? Notes,
    IReadOnlyList<FpProGlassPaneResponse> Glass,
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
    FpProPreviewImageResponse? Image,
    IReadOnlyList<string> PendingFields,
    IReadOnlyList<FpProPreviewWarningResponse> Warnings,
    int? InferredModuleCount,
    string? ModuleConfidence,
    bool RequiresManualModule,
    IReadOnlyList<FpProTechnicalProfileResponse> TechnicalProfiles);

public sealed record FpProGlassPaneResponse(
    string Code,
    string? Treatment,
    decimal? ThicknessMm,
    int? WidthMm,
    int? HeightMm,
    int? Quantity);

public sealed record FpProTechnicalProfileResponse(
    string Code,
    string Description,
    decimal? TotalLengthMeters,
    decimal? UnitLengthMeters,
    decimal? Quantity);

public sealed record FpProPreviewImageResponse(
    string ContentType,
    string Base64);

public sealed record FpProPreviewWarningResponse(
    string Code,
    string? Field,
    string Message);

public sealed record FpProCatalogsResponse(
    IReadOnlyList<FpProCatalogOptionResponse> Systems,
    IReadOnlyList<FpProCatalogOptionResponse> GlassDescriptions,
    IReadOnlyList<FpProCatalogOptionResponse> Finishes,
    IReadOnlyList<FpProCatalogOptionResponse> Locations);

public sealed record FpProCatalogOptionResponse(
    string Label,
    string Value);
