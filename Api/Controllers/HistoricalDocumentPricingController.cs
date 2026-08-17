using Application.Common.Abstractions.DocumentProcessing;
using Application.Common.Abstractions.HistoricalPricing;
using Contracts.HistoricalPricing;
using Domain.PreQuotes;
using Infrastructure.DocumentProcessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/historical-pricing")]
public sealed class HistoricalDocumentPricingController(
    IHistoricalDocumentEstimatePipeline pipeline,
    ILogger<HistoricalDocumentPricingController> logger) : ControllerBase
{
    private const string PricingBasis = "PUBLIC_QUOTED_ITEM_PRICES";
    private const string PdfContentType = "application/pdf";
    private const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string JpegContentType = "image/jpeg";
    private const string PngContentType = "image/png";

    private static readonly IReadOnlyDictionary<string, string[]> SupportedFiles =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [PdfContentType] = [".pdf"],
            [XlsxContentType] = [".xlsx"],
            [JpegContentType] = [".jpg", ".jpeg"],
            [PngContentType] = [".png"]
        };

    [HttpPost("document-estimate")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<HistoricalDocumentEstimateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HistoricalDocumentEstimateResponse>> Estimate(
        [FromForm] HistoricalDocumentEstimateForm form,
        CancellationToken cancellationToken)
    {
        if (!HasValidFiles(form.Files)
            || form.ProjectId == Guid.Empty
            || form.RequirementId == Guid.Empty)
        {
            return BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Documento invalido",
                "Debe enviar archivos PDF, XLSX, JPG o PNG validos."));
        }

        var validFiles = form.Files;
        var streams = validFiles.Select(file => file.OpenReadStream()).ToArray();
        try
        {
            var processingFiles = validFiles.Select((file, index) =>
                new DocumentProcessingFile(
                    Guid.NewGuid(),
                    Path.GetFileName(file.FileName),
                    file.ContentType,
                    file.Length,
                    streams[index])).ToArray();
            var estimate = await pipeline.EstimateAsync(
                processingFiles,
                form.ProjectId,
                form.RequirementId,
                cancellationToken);
            if (!estimate.IsSuccess)
            {
                return MapPipelineFailure(estimate.Failure);
            }
            return Ok(Map(
                estimate.ProjectId,
                estimate.RequirementId,
                estimate.SourceCount,
                estimate.SourceItems,
                estimate.Aggregate!));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Historical document pricing failed. TraceIdentifier={TraceIdentifier} ExceptionType={ExceptionType} ExceptionMessage={ExceptionMessage} InnerExceptionType={InnerExceptionType} InnerExceptionMessage={InnerExceptionMessage}",
                HttpContext.TraceIdentifier,
                exception.GetType().Name,
                exception.Message,
                exception.InnerException?.GetType().Name,
                exception.InnerException?.Message);
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error al estimar documentos",
                detail: "No fue posible completar la extraccion y estimacion agregada.");
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    internal static ActionResult<HistoricalDocumentEstimateResponse> MapPipelineFailure(
        HistoricalDocumentEstimatePipelineFailure failure)
    {
        if (failure == HistoricalDocumentEstimatePipelineFailure.Ai2RemoteRejection)
        {
            return new BadRequestObjectResult(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Documento rechazado",
                "AI2 rechazo uno o mas documentos de la solicitud."));
        }

        if (failure == HistoricalDocumentEstimatePipelineFailure.CorpusUnavailable)
        {
            return new ObjectResult(CreateProblem(
                StatusCodes.Status503ServiceUnavailable,
                "Corpus historico no disponible",
                "No fue posible cargar el corpus historico configurado."))
            { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }

        return new ObjectResult(
            CreateProblem(
                StatusCodes.Status502BadGateway,
                failure == HistoricalDocumentEstimatePipelineFailure.InvalidExtraction
                    ? "Extraccion AI2 invalida"
                    : "Servicio de extraccion no disponible",
                failure == HistoricalDocumentEstimatePipelineFailure.InvalidExtraction
                    ? "AI2 no devolvio una extraccion estructurada utilizable."
                    : "No fue posible obtener una extraccion valida desde AI2."))
        {
            StatusCode = StatusCodes.Status502BadGateway
        };
    }

    private static bool HasValidFiles(IReadOnlyCollection<IFormFile>? files) =>
        files is { Count: > 0 }
        && files.All(file =>
            file.Length > 0
            && !string.IsNullOrWhiteSpace(file.FileName)
            && SupportedFiles.TryGetValue(file.ContentType, out var extensions)
            && extensions.Contains(
                Path.GetExtension(file.FileName),
                StringComparer.OrdinalIgnoreCase));

    internal static HistoricalDocumentEstimateResponse Map(
        Guid? projectId,
        Guid? requirementId,
        int sourceCount,
        IReadOnlyList<StructuredItemData> sourceItems,
        PricedRequirementExtraction aggregate)
    {
        var sourceById = sourceItems
            .GroupBy(item => item.Sequence)
            .ToDictionary(group => group.Key, group => group.First());
        return new HistoricalDocumentEstimateResponse(
            projectId,
            requirementId,
            sourceCount,
            sourceItems.Count,
            aggregate.ItemCount,
            aggregate.PricedItemCount,
            aggregate.NotPriceableItemCount,
            aggregate.Items.Count(item => item.Status
                == RequirementElementPricingStatus.TechnicalFailure),
            aggregate.Currency,
            PricingBasis,
            aggregate.CommercialMinimum,
            aggregate.CommercialExpected,
            aggregate.CommercialMaximum,
            aggregate.ConfidenceScore,
            aggregate.ConfidenceLevel.ToString().ToUpperInvariant(),
            aggregate.IsPartial,
            aggregate.RequiresReview,
            aggregate.Assumptions,
            aggregate.MissingData,
            aggregate.Warnings,
            aggregate.Items.Select(item => MapItem(
                item,
                sourceById.GetValueOrDefault(item.ElementId))).ToArray());
    }

    private static HistoricalDocumentEstimateItemResponse MapItem(
        PricedRequirementExtractionItem item,
        StructuredItemData? source)
    {
        var query = item.CandidateQuery;
        var commercial = item.CommercialEstimate;
        return new HistoricalDocumentEstimateItemResponse(
            item.ElementId,
            item.Reference,
            query?.Category ?? source?.ElementType.ToString().ToUpperInvariant(),
            query?.System ?? source?.TechnicalClassification?.SystemCode,
            query?.Glass ?? source?.Glass?.NormalizedCode,
            query?.Configuration ?? source?.Configuration,
            query?.Width ?? source?.WidthMillimeters,
            query?.Height ?? source?.HeightMillimeters,
            query?.Area ?? source?.AreaSquareMeters,
            query?.Quantity ?? source?.Quantity,
            query?.Finish ?? source?.TechnicalClassification?.FinishCode,
            ToContractStatus(item.Status),
            item.UnitMinimum,
            item.UnitExpected,
            item.UnitMaximum,
            item.LineMinimum,
            item.LineExpected,
            item.LineMaximum,
            item.LineMinimum,
            item.LineExpected,
            item.LineMaximum,
            commercial?.ConfidenceScore,
            commercial?.ConfidenceLevel.ToString().ToUpperInvariant(),
            item.RequiresReview,
            item.TechnicalEstimate?.CandidateCount,
            item.TechnicalEstimate?.StrongComparableCount,
            item.MappingWarnings,
            commercial?.Assumptions ?? [],
            commercial?.MissingData ?? []);
    }

    private static string ToContractStatus(RequirementElementPricingStatus status) =>
        status switch
        {
            RequirementElementPricingStatus.Priceable => "PRICEABLE",
            RequirementElementPricingStatus.NotPriceable => "NOT_PRICEABLE",
            RequirementElementPricingStatus.TechnicalFailure => "TECHNICAL_FAILURE",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };

    private static ProblemDetails CreateProblem(
        int status,
        string title,
        string detail) =>
        new() { Status = status, Title = title, Detail = detail };
}
