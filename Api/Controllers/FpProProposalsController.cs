using Api.ErrorHandling;
using Application.Common.Abstractions.Proposals;
using Application.Proposals.FpPro;
using Contracts.Common;
using Contracts.Proposals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

public sealed class FpProPreviewForm
{
    public IFormFile? File { get; init; }
}

[ApiController]
[Authorize]
[ContractualErrors(
    InvalidRequestErrorCode = FpProPreviewErrorCodes.InvalidRequest,
    UnsupportedMediaTypeErrorCode = FpProPreviewErrorCodes.UnsupportedFileType)]
[Route("api/proposals/fp-pro")]
public sealed class FpProProposalsController(
    PreviewFpProReportService previewService,
    GenerateFpProQuotationService generateService,
    IQuotationTemplateCatalogReader catalogReader) : ControllerBase
{
    [HttpGet("catalogs")]
    [ProducesResponseType(typeof(FpProCatalogsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<FpProCatalogsResponse>> Catalogs(CancellationToken cancellationToken)
    {
        var catalog = await catalogReader.ReadAsync(cancellationToken);
        return Ok(new FpProCatalogsResponse(
            DistinctOptions(catalog.Systems.Select(value => new FpProCatalogOptionResponse(value.Label, value.ExcelValue))),
            DistinctOptions(catalog.GlassDescriptions.Select(value => new FpProCatalogOptionResponse(value.Label, value.Value))),
            DistinctOptions(catalog.Finishes.Select(value => new FpProCatalogOptionResponse(value.Label, value.Value))),
            DistinctOptions(catalog.Locations.Select(value => new FpProCatalogOptionResponse(value.Label, value.Value)))));
    }

    [HttpPost("preview")]
    [ProducesResponseType(typeof(FpProPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Preview(
        [FromForm] FpProPreviewForm form,
        CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
        {
            return ProposalProblem(
                StatusCodes.Status415UnsupportedMediaType,
                ApiErrorCodes.ApiUnsupportedMediaType,
                "Solicitud multipart invalida",
                "La solicitud debe enviarse como multipart/form-data.");
        }

        IFormCollection formCollection;
        try
        {
            formCollection = await Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return InvalidMultipartRequest();
        }
        catch (IOException)
        {
            return InvalidMultipartRequest();
        }

        if (form.File is null
            || formCollection.Files.Count != 1
            || !string.Equals(form.File.Name, "file", StringComparison.Ordinal))
        {
            return InvalidMultipartRequest();
        }

        await using var stream = form.File.OpenReadStream();
        var result = await previewService.ExecuteAsync(
            new PreviewFpProReportCommand(new FpProReportFile(
                Path.GetFileName(form.File.FileName),
                form.File.ContentType,
                form.File.Length,
                stream)),
            cancellationToken);

        return result.IsSuccess && result.Preview is { } preview
            ? Ok(Map(preview))
            : MapFailure(result.Failure);
    }

    [HttpPost("generate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateFpProQuotationRequest request,
        CancellationToken cancellationToken)
    {
        var command = MapGenerateCommand(request);
        if (command is null)
        {
            return ProposalProblem(
                StatusCodes.Status400BadRequest,
                FpProPreviewErrorCodes.GenerateInvalidRequest,
                "Solicitud invalida",
                "Debe enviar un item FP Pro con todos los valores definitivos requeridos.");
        }

        var result = await generateService.ExecuteAsync(command, cancellationToken);
        if (result.IsSuccess && result.Workbook is { } workbook)
        {
            return File(workbook.Content, workbook.ContentType, workbook.FileName);
        }

        return MapGenerateFailure(result.Failure);
    }

    private IActionResult InvalidMultipartRequest() =>
        ProposalProblem(
            StatusCodes.Status400BadRequest,
            FpProPreviewErrorCodes.InvalidRequest,
            "Solicitud multipart invalida",
            "Debe enviar exactamente un archivo PDF en el campo file.");

    private IActionResult MapFailure(PreviewFpProReportFailure failure) =>
        failure switch
        {
            PreviewFpProReportFailure.InvalidRequest => ProposalProblem(
                StatusCodes.Status400BadRequest,
                FpProPreviewErrorCodes.InvalidRequest,
                "Solicitud invalida",
                "Debe enviar un reporte FP Pro PDF valido."),
            PreviewFpProReportFailure.UnsupportedFileType => ProposalProblem(
                StatusCodes.Status415UnsupportedMediaType,
                FpProPreviewErrorCodes.UnsupportedFileType,
                "Tipo de archivo no soportado",
                "El preview FP Pro solo acepta archivos PDF."),
            PreviewFpProReportFailure.EmptyFile => ProposalProblem(
                StatusCodes.Status422UnprocessableEntity,
                FpProPreviewErrorCodes.EmptyFile,
                "Archivo vacio",
                "El reporte FP Pro no puede estar vacio."),
            PreviewFpProReportFailure.FileTooLarge => ProposalProblem(
                StatusCodes.Status413PayloadTooLarge,
                FpProPreviewErrorCodes.FileTooLarge,
                "Archivo demasiado grande",
                "El reporte FP Pro supera el tamano maximo permitido."),
            PreviewFpProReportFailure.InvalidFpProReport => ProposalProblem(
                StatusCodes.Status422UnprocessableEntity,
                FpProPreviewErrorCodes.InvalidReport,
                "Reporte FP Pro invalido",
                "El PDF no parece ser un reporte FP Pro interpretable."),
            _ => ProposalProblem(
                StatusCodes.Status500InternalServerError,
                FpProPreviewErrorCodes.ParseError,
                "Error al analizar reporte FP Pro",
                "No fue posible generar el preview del reporte FP Pro.")
        };

    private IActionResult MapGenerateFailure(GenerateFpProQuotationFailure failure) =>
        failure switch
        {
            GenerateFpProQuotationFailure.UnsupportedItemCount => ProposalProblem(
                StatusCodes.Status400BadRequest,
                FpProPreviewErrorCodes.GenerateUnsupportedItemCount,
                "Cantidad de items no soportada",
                "The quotation template does not contain enough prepared item blocks."),
            GenerateFpProQuotationFailure.UnknownCatalogValue => ProposalProblem(
                StatusCodes.Status422UnprocessableEntity,
                FpProPreviewErrorCodes.GenerateUnknownCatalogValue,
                "Valor de catalogo no valido",
                "System, glassDescription y finish deben existir en BD GN."),
            GenerateFpProQuotationFailure.InvalidRequest => ProposalProblem(
                StatusCodes.Status400BadRequest,
                FpProPreviewErrorCodes.GenerateInvalidRequest,
                "Solicitud invalida",
                "Debe enviar un item FP Pro con todos los valores definitivos requeridos."),
            _ => ProposalProblem(
                StatusCodes.Status500InternalServerError,
                FpProPreviewErrorCodes.GenerateError,
                "Error al generar cotizacion FP Pro",
                "No fue posible generar el archivo Excel.")
        };

    private ObjectResult ProposalProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) =>
        ApiProblemDetailsFactory.Create(HttpContext, statusCode, errorCode, title, detail);

    private static FpProCatalogOptionResponse[] DistinctOptions(
        IEnumerable<FpProCatalogOptionResponse> options) =>
        options
            .GroupBy(value => value.Value, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

    private static FpProPreviewResponse Map(FpProReportPreviewData preview) =>
        new(
            new FpProReportResponse(
                preview.Report.OrderId,
                preview.Report.Description,
                preview.Report.Revision,
                preview.Report.ItemsDetected,
                preview.Report.AluminumWastePercent,
                preview.Report.ProfileBarCount,
                preview.Report.DoorCount),
            preview.Items.Select(item => new FpProPreviewItemResponse(
                item.ItemNumber,
                item.Typology,
                item.FpProProfiles,
                item.WidthMm,
                item.HeightMm,
                item.WidthM,
                item.HeightM,
                item.Quantity,
                item.NominalAreaM2,
                item.Notes,
                item.Glass.Select(glass => new FpProGlassPaneResponse(
                    glass.Code,
                    glass.Treatment,
                    glass.ThicknessMm,
                    glass.WidthMm,
                    glass.HeightMm,
                    glass.Quantity)).ToArray(),
                item.SelectedThicknessMm,
                item.System,
                item.GlassDescription,
                item.Finish,
                item.Lock,
                item.GlassPrice,
                item.AluminumBase,
                item.AluminumBaseUnit,
                item.AccessoriesBase,
                item.AccessoriesBaseUnit,
                item.StructureWeightKg,
                item.StructureWeightKgUnit,
                item.Image is null
                    ? null
                    : new FpProPreviewImageResponse(
                        item.Image.ContentType,
                        item.Image.Base64),
                item.PendingFields,
                item.Warnings.Select(warning => new FpProPreviewWarningResponse(
                    warning.Code,
                    warning.Field,
                    warning.Message)).ToArray(),
                item.InferredModuleCount,
                item.ModuleConfidence,
                item.RequiresManualModule,
                item.TechnicalProfiles.Select(profile => new FpProTechnicalProfileResponse(
                    profile.Code,
                    profile.Description,
                    profile.TotalLengthMeters,
                    profile.UnitLengthMeters,
                    profile.Quantity)).ToArray())).ToArray(),
            preview.PendingFields);

    private static GenerateFpProQuotationCommand? MapGenerateCommand(
        GenerateFpProQuotationRequest request)
    {
        if (request.Items is null
            || string.IsNullOrWhiteSpace(request.ProposalName)
            || string.IsNullOrWhiteSpace(request.ClientName)
            || string.IsNullOrWhiteSpace(request.ProjectName)
            || string.IsNullOrWhiteSpace(request.ProductionLine)
            || string.IsNullOrWhiteSpace(request.PreparedBy)
            || string.IsNullOrWhiteSpace(request.BudgetId)
            || request.AluminumWastePercent is null
            || request.BenefitPercent is null
            || request.CommissionPercent is null)
        {
            return null;
        }

        var items = new List<FpProQuotationItemInput>();
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemNumber)
                || string.IsNullOrWhiteSpace(item.System)
                || string.IsNullOrWhiteSpace(item.GlassDescription)
                || string.IsNullOrWhiteSpace(item.Finish)
                || string.IsNullOrWhiteSpace(item.ImageBase64)
                || item.WidthM is null
                || item.HeightM is null
                || item.Quantity is null
                || item.GlassPrice is null
                || item.AccessoriesBase is null
                || item.AluminumBase is null
                || item.SelectedThicknessMm is null
                || item.StructureWeightKg is null
                || item.Module is null)
            {
                return null;
            }

            items.Add(new FpProQuotationItemInput(
                item.ItemNumber,
                item.Typology ?? string.Empty,
                item.WidthM.Value,
                item.HeightM.Value,
                item.Quantity.Value,
                item.System,
                item.GlassDescription,
                item.Finish,
                item.GlassPrice.Value,
                item.AccessoriesBase.Value,
                item.AluminumBase.Value,
                item.SelectedThicknessMm.Value,
                item.StructureWeightKg.Value,
                item.Module.Value,
                item.Notes,
                item.ImageBase64));
        }

        return new GenerateFpProQuotationCommand(new QuotationWorkbookRequest(
            new FpProQuotationReportInput(
                request.Report.Order,
                request.Report.Description,
                request.Report.Location ?? string.Empty,
                request.AluminumWastePercent!.Value,
                request.BenefitPercent!.Value,
                request.CommissionPercent!.Value,
                request.Report.ProfileBarCount,
                request.Report.DoorCount),
            request.ProposalName,
            request.ClientName,
            request.ProjectName,
            request.ProductionLine,
            request.PreparedBy,
            request.BudgetId,
            items));
    }
}
