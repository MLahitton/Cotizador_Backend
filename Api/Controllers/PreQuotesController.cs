using Application.PreQuotes.GetPreQuoteById;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequotes")]
public sealed class PreQuotesController(
    GetPreQuoteByIdService getPreQuoteByIdService)
    : ControllerBase
{
    [HttpGet("{preQuoteId:guid}")]
    [ProducesResponseType<PreQuoteDetailsResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PreQuoteDetailsResponse>> GetById(
        [FromRoute] Guid preQuoteId,
        CancellationToken cancellationToken)
    {
        var result = await getPreQuoteByIdService.ExecuteAsync(
            new GetPreQuoteByIdQuery(preQuoteId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapFailure(result.Failure);
        }

        var preQuote = result.PreQuote!;
        var response = new PreQuoteDetailsResponse(
            preQuote.Id,
            preQuote.ProjectId,
            preQuote.DocumentCount,
            preQuote.CreatedAtUtc,
            preQuote.UpdatedAtUtc);

        return Ok(response);
    }

    private ActionResult<PreQuoteDetailsResponse> MapFailure(
        GetPreQuoteByIdFailure failure)
    {
        return failure switch
        {
            GetPreQuoteByIdFailure.InvalidRequest => PreQuoteProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "El identificador de la precotización no es válido."),
            GetPreQuoteByIdFailure.Unauthorized => PreQuoteProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            GetPreQuoteByIdFailure.InactiveUser => PreQuoteProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso para consultar precotizaciones."),
            GetPreQuoteByIdFailure.NotFound => PreQuoteProblem(
                StatusCodes.Status404NotFound,
                "Precotización no encontrada",
                "No existe la precotización indicada."),
            _ => PreQuoteProblem(
                StatusCodes.Status500InternalServerError,
                "Error al consultar la precotización",
                "No fue posible consultar la precotización.")
        };
    }

    private ObjectResult PreQuoteProblem(
        int statusCode,
        string title,
        string detail)
    {
        return Problem(
            statusCode: statusCode,
            title: title,
            detail: detail);
    }
}
