using Application.Common.Abstractions.Proposals;

namespace Application.Proposals.FpPro;

public sealed record GenerateFpProQuotationCommand(QuotationWorkbookRequest Request);

public sealed record GenerateFpProQuotationResult(
    bool IsSuccess,
    GenerateFpProQuotationFailure Failure,
    GeneratedQuotationWorkbook? Workbook)
{
    public static GenerateFpProQuotationResult Success(GeneratedQuotationWorkbook workbook) =>
        new(true, GenerateFpProQuotationFailure.None, workbook);

    public static GenerateFpProQuotationResult Failed(GenerateFpProQuotationFailure failure) =>
        new(false, failure, null);
}

public enum GenerateFpProQuotationFailure
{
    None,
    InvalidRequest,
    UnsupportedItemCount,
    UnknownCatalogValue,
    GenerationError
}

public sealed class GenerateFpProQuotationService(
    IQuotationTemplateCatalogReader catalogReader,
    IQuotationWorkbookGenerator workbookGenerator)
{
    private static readonly char[] InvalidProposalFileNameChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    public async Task<GenerateFpProQuotationResult> ExecuteAsync(
        GenerateFpProQuotationCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Request;
        if (request.Items.Count == 0)
        {
            return GenerateFpProQuotationResult.Failed(
                GenerateFpProQuotationFailure.UnsupportedItemCount);
        }

        if (HasInvalidProposalName(request.ProposalName))
        {
            return GenerateFpProQuotationResult.Failed(
                GenerateFpProQuotationFailure.InvalidRequest);
        }

        if (HasInvalidHeaderData(request))
        {
            return GenerateFpProQuotationResult.Failed(
                GenerateFpProQuotationFailure.InvalidRequest);
        }

        if (request.Items.Any(item => HasInvalidRequiredData(request.Report, item)))
        {
            return GenerateFpProQuotationResult.Failed(
                GenerateFpProQuotationFailure.InvalidRequest);
        }

        var catalog = await catalogReader.ReadAsync(cancellationToken);
        if (HasInvalidCatalogValue(request, catalog))
        {
            return GenerateFpProQuotationResult.Failed(
                GenerateFpProQuotationFailure.UnknownCatalogValue);
        }

        try
        {
            var workbook = await workbookGenerator.GenerateAsync(request, cancellationToken);
            return GenerateFpProQuotationResult.Success(workbook);
        }
        catch (QuotationTemplateCapacityExceededException)
        {
            return GenerateFpProQuotationResult.Failed(
                GenerateFpProQuotationFailure.UnsupportedItemCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return GenerateFpProQuotationResult.Failed(
                GenerateFpProQuotationFailure.GenerationError);
        }
    }

    private static bool HasInvalidRequiredData(FpProQuotationReportInput report, FpProQuotationItemInput item) =>
        string.IsNullOrWhiteSpace(report.Location)
        || report.AluminumWastePercent < 0
        || report.AluminumWastePercent > 1000
        || report.BenefitPercent < 0
        || report.BenefitPercent > 1000
        || report.CommissionPercent < 0
        || report.CommissionPercent > 1000
        || string.IsNullOrWhiteSpace(item.ItemNumber)
        || string.IsNullOrWhiteSpace(item.System)
        || string.IsNullOrWhiteSpace(item.GlassDescription)
        || string.IsNullOrWhiteSpace(item.Finish)
        || string.IsNullOrWhiteSpace(item.ImageBase64)
        || item.WidthM <= 0
        || item.HeightM <= 0
        || item.Quantity <= 0
        || item.GlassPrice <= 0
        || item.AccessoriesBase < 0
        || item.AluminumBase < 0
        || item.SelectedThicknessMm <= 0
        || item.StructureWeightKg <= 0
        || item.Module <= 0;

    private static bool HasInvalidProposalName(string proposalName) =>
        string.IsNullOrWhiteSpace(proposalName)
        || !proposalName.Any(value => !InvalidProposalFileNameChars.Contains(value) && !char.IsWhiteSpace(value));

    private static bool HasInvalidHeaderData(QuotationWorkbookRequest request) =>
        string.IsNullOrWhiteSpace(request.ClientName)
        || string.IsNullOrWhiteSpace(request.ProjectName)
        || string.IsNullOrWhiteSpace(request.ProductionLine)
        || string.IsNullOrWhiteSpace(request.PreparedBy)
        || string.IsNullOrWhiteSpace(request.BudgetId);

    private static bool HasInvalidCatalogValue(QuotationWorkbookRequest request, QuotationTemplateCatalog catalog) =>
        !catalog.Locations.Any(value => string.Equals(value.Value, request.Report.Location, StringComparison.Ordinal))
        || request.Items.Any(item =>
            !catalog.Systems.Any(value => string.Equals(value.ExcelValue, item.System, StringComparison.Ordinal))
            || !catalog.GlassDescriptions.Any(value => string.Equals(value.Value, item.GlassDescription, StringComparison.Ordinal))
            || !catalog.Finishes.Any(value => string.Equals(value.Value, item.Finish, StringComparison.Ordinal)));
}
