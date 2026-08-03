using Application.PreQuotes.CreatePreQuote;
using Application.PreQuotes.GetProjectPreQuotes;
using Contracts.Common;
using Contracts.PreQuotes;
using Api.ErrorHandling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[ContractualErrors(InvalidRequestErrorCode = PreQuoteQueryErrorCodes.ListInvalidRequest)]
[Route("api/v1/projects/{projectId}/prequotes")]
public sealed class ProjectPreQuotesController(
    CreatePreQuoteService createPreQuoteService,
    GetProjectPreQuotesService getProjectPreQuotesService)
    : ControllerBase
{
    [HttpPost]
    [ContractualErrors(InvalidRequestErrorCode = PreQuoteErrorCodes.InvalidRequest)]
    [ProducesResponseType<CreatePreQuoteResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CreatePreQuoteResponse>> Create(
        [FromRoute] Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await createPreQuoteService.ExecuteAsync(
            new CreatePreQuoteCommand(projectId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapFailure(result.Failure);
        }

        var preQuote = result.PreQuote!;
        var response = new CreatePreQuoteResponse(
            preQuote.Id,
            preQuote.ProjectId,
            preQuote.CreatedAtUtc,
            preQuote.UpdatedAtUtc);

        return StatusCode(
            StatusCodes.Status201Created,
            response);
    }

    [HttpGet]
    [ProducesResponseType<GetProjectPreQuotesResponse>(
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
    public async Task<ActionResult<GetProjectPreQuotesResponse>> Get(
        [FromRoute] Guid projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await getProjectPreQuotesService.ExecuteAsync(
            new GetProjectPreQuotesQuery(
                projectId,
                page,
                pageSize),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapGetProjectPreQuotesFailure(result.Failure);
        }

        var preQuotesPage = result.Page!;
        var items = preQuotesPage.Items
            .Select(preQuote => new PreQuoteListItemResponse(
                preQuote.Id,
                preQuote.ProjectId,
                preQuote.DocumentCount,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc))
            .ToArray();

        return Ok(new GetProjectPreQuotesResponse(
            items,
            preQuotesPage.Page,
            preQuotesPage.PageSize,
            preQuotesPage.TotalCount,
            preQuotesPage.TotalPages));
    }

    private ActionResult<CreatePreQuoteResponse> MapFailure(
        CreatePreQuoteFailure failure)
    {
        return failure switch
        {
            CreatePreQuoteFailure.InvalidRequest => PreQuoteProblem(
                StatusCodes.Status400BadRequest,
                PreQuoteErrorCodes.InvalidRequest,
                "Solicitud invalida",
                "El identificador del proyecto no es valido."),
            CreatePreQuoteFailure.Unauthorized => PreQuoteProblem(
                StatusCodes.Status401Unauthorized,
                PreQuoteErrorCodes.Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            CreatePreQuoteFailure.InactiveUser => PreQuoteProblem(
                StatusCodes.Status403Forbidden,
                PreQuoteErrorCodes.InactiveUser,
                "Usuario inactivo",
                "El usuario no tiene acceso para crear precotizaciones."),
            CreatePreQuoteFailure.ProjectNotFound => PreQuoteProblem(
                StatusCodes.Status404NotFound,
                PreQuoteErrorCodes.ProjectNotFound,
                "Proyecto no encontrado",
                "No existe el proyecto indicado."),
            CreatePreQuoteFailure.InactiveProject => PreQuoteProblem(
                StatusCodes.Status409Conflict,
                PreQuoteErrorCodes.ProjectInactive,
                "Proyecto inactivo",
                "No se puede crear una precotizacion para un proyecto inactivo."),
            CreatePreQuoteFailure.ClientNotFound => PreQuoteProblem(
                StatusCodes.Status404NotFound,
                PreQuoteErrorCodes.ClientNotFound,
                "Cliente no encontrado",
                "No existe el cliente asociado al proyecto."),
            CreatePreQuoteFailure.InactiveClient => PreQuoteProblem(
                StatusCodes.Status409Conflict,
                PreQuoteErrorCodes.ClientInactive,
                "Cliente inactivo",
                "No se puede crear una precotizacion para un proyecto cuyo cliente esta inactivo."),
            CreatePreQuoteFailure.QueryError => PreQuoteProblem(
                StatusCodes.Status500InternalServerError,
                PreQuoteErrorCodes.QueryError,
                "Error al consultar el contexto de la precotizacion",
                "No fue posible consultar el proyecto y su cliente."),
            _ => PreQuoteProblem(
                StatusCodes.Status500InternalServerError,
                PreQuoteErrorCodes.PersistenceError,
                "Error al crear la precotizacion",
                "No fue posible guardar la precotizacion.")
        };
    }

    private ActionResult<GetProjectPreQuotesResponse>
        MapGetProjectPreQuotesFailure(
            GetProjectPreQuotesFailure failure)
    {
        return failure switch
        {
            GetProjectPreQuotesFailure.InvalidRequest => PreQuoteProblem(
                StatusCodes.Status400BadRequest,
                PreQuoteQueryErrorCodes.ListInvalidRequest,
                "Solicitud invalida",
                "Los parametros de consulta de precotizaciones no son validos."),
            GetProjectPreQuotesFailure.Unauthorized => PreQuoteProblem(
                StatusCodes.Status401Unauthorized,
                PreQuoteErrorCodes.Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            GetProjectPreQuotesFailure.InactiveUser => PreQuoteProblem(
                StatusCodes.Status403Forbidden,
                PreQuoteErrorCodes.InactiveUser,
                "Usuario inactivo",
                "El usuario no tiene acceso para consultar precotizaciones."),
            GetProjectPreQuotesFailure.ProjectNotFound => PreQuoteProblem(
                StatusCodes.Status404NotFound,
                PreQuoteErrorCodes.ProjectNotFound,
                "Proyecto no encontrado",
                "No existe el proyecto indicado."),
            _ => PreQuoteProblem(
                StatusCodes.Status500InternalServerError,
                PreQuoteQueryErrorCodes.ListQueryError,
                "Error al consultar precotizaciones",
                "No fue posible consultar las precotizaciones del proyecto.")
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
