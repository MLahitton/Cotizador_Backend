using Application.Common.Abstractions.HistoricalPricing;
using Application.HistoricalPricing;
using Contracts.HistoricalPricing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequotes/{preQuoteId:guid}/historical-estimate")]
public sealed class PreQuoteHistoricalPricingController(
    EstimateStoredPreQuoteDocumentsService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<HistoricalDocumentEstimateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HistoricalDocumentEstimateResponse>> Estimate(
        Guid preQuoteId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        StoredPreQuoteHistoricalEstimateRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            preQuoteId,
            request?.DocumentIds,
            cancellationToken);
        if (result.Failure != StoredPreQuoteHistoricalEstimateFailure.None)
        {
            return MapStoredFailure(result.Failure);
        }

        var estimate = result.Estimate!;
        if (!estimate.IsSuccess)
        {
            return HistoricalDocumentPricingController.MapPipelineFailure(
                estimate.Failure);
        }

        return Ok(HistoricalDocumentPricingController.Map(
            estimate.ProjectId,
            estimate.RequirementId,
            estimate.SourceCount,
            estimate.SourceItems,
            estimate.Aggregate!));
    }

    private ActionResult<HistoricalDocumentEstimateResponse> MapStoredFailure(
        StoredPreQuoteHistoricalEstimateFailure failure)
    {
        var (status, title, detail) = failure switch
        {
            StoredPreQuoteHistoricalEstimateFailure.InvalidRequest =>
                (400, "Solicitud invalida", "Los identificadores de documentos no son validos."),
            StoredPreQuoteHistoricalEstimateFailure.Unauthorized =>
                (401, "No autorizado", "No fue posible identificar al usuario autenticado."),
            StoredPreQuoteHistoricalEstimateFailure.InactiveUser =>
                (403, "Usuario inactivo", "El usuario autenticado se encuentra inactivo."),
            StoredPreQuoteHistoricalEstimateFailure.NotFound =>
                (404, "Precotizacion o documento no encontrado", "La precotizacion o alguno de sus documentos no existe."),
            StoredPreQuoteHistoricalEstimateFailure.NoDocuments =>
                (409, "Sin documentos", "La precotizacion no tiene documentos disponibles para estimar."),
            StoredPreQuoteHistoricalEstimateFailure.FileUnavailable =>
                (409, "Archivo no disponible", "Uno de los documentos registrados no tiene contenido recuperable."),
            StoredPreQuoteHistoricalEstimateFailure.QueryError =>
                (500, "Error de consulta", "No fue posible consultar los documentos de la precotizacion."),
            _ => (500, "Error de estimacion", "No fue posible generar la estimacion.")
        };
        return StatusCode(status, new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        });
    }
}
