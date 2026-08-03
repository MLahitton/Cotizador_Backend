using Application.PreQuotes.GetPreQuoteById;
using Contracts.Common;
using Contracts.PreQuotes;
using Api.ErrorHandling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[ContractualErrors(InvalidRequestErrorCode = PreQuoteErrorCodes.InvalidRequest)]
[Route("api/v1/prequotes")]
public sealed class PreQuotesController(
    GetPreQuoteByIdService getPreQuoteByIdService)
    : ControllerBase
{
    [HttpGet("{preQuoteId}")]
    [ProducesResponseType<PreQuoteDetailsResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
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
                PreQuoteErrorCodes.InvalidRequest,
                "Solicitud invalida",
                "El identificador de la precotizacion no es valido."),
            GetPreQuoteByIdFailure.Unauthorized => PreQuoteProblem(
                StatusCodes.Status401Unauthorized,
                PreQuoteErrorCodes.Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            GetPreQuoteByIdFailure.InactiveUser => PreQuoteProblem(
                StatusCodes.Status403Forbidden,
                PreQuoteErrorCodes.InactiveUser,
                "Usuario inactivo",
                "El usuario no tiene acceso para consultar precotizaciones."),
            GetPreQuoteByIdFailure.NotFound => PreQuoteProblem(
                StatusCodes.Status404NotFound,
                PreQuoteQueryErrorCodes.NotFound,
                "Precotizacion no encontrada",
                "No existe la precotizacion indicada."),
            _ => PreQuoteProblem(
                StatusCodes.Status500InternalServerError,
                PreQuoteErrorCodes.QueryError,
                "Error al consultar la precotizacion",
                "No fue posible consultar la precotizacion.")
        };
    }

    private ObjectResult PreQuoteProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail)
    {
        return ApiProblemDetailsFactory.Create(
            HttpContext, statusCode, errorCode, title, detail);
    }
}
