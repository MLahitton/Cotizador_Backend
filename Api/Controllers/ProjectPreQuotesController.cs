using Application.PreQuotes.CreatePreQuote;
using Application.PreQuotes.GetProjectPreQuotes;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects/{projectId:guid}/prequotes")]
public sealed class ProjectPreQuotesController(
    CreatePreQuoteService createPreQuoteService,
    GetProjectPreQuotesService getProjectPreQuotesService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreatePreQuoteResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
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
                "Solicitud inválida",
                "El identificador del proyecto no es válido."),
            CreatePreQuoteFailure.Unauthorized => PreQuoteProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            CreatePreQuoteFailure.InactiveUser => PreQuoteProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso para crear precotizaciones."),
            CreatePreQuoteFailure.ProjectNotFound => PreQuoteProblem(
                StatusCodes.Status404NotFound,
                "Proyecto no encontrado",
                "No existe el proyecto indicado."),
            CreatePreQuoteFailure.InactiveProject => PreQuoteProblem(
                StatusCodes.Status409Conflict,
                "Proyecto inactivo",
                "No se puede crear una precotización para un proyecto inactivo."),
            CreatePreQuoteFailure.ClientNotFound => PreQuoteProblem(
                StatusCodes.Status404NotFound,
                "Cliente no encontrado",
                "No existe el cliente asociado al proyecto."),
            CreatePreQuoteFailure.InactiveClient => PreQuoteProblem(
                StatusCodes.Status409Conflict,
                "Cliente inactivo",
                "No se puede crear una precotización para un proyecto cuyo cliente está inactivo."),
            CreatePreQuoteFailure.QueryError => PreQuoteProblem(
                StatusCodes.Status500InternalServerError,
                "Error al consultar el contexto de la precotización",
                "No fue posible consultar el proyecto y su cliente."),
            _ => PreQuoteProblem(
                StatusCodes.Status500InternalServerError,
                "Error al crear la precotización",
                "No fue posible guardar la precotización.")
        };
    }

    private ActionResult<GetProjectPreQuotesResponse>
        MapGetProjectPreQuotesFailure(
            GetProjectPreQuotesFailure failure)
    {
        return failure switch
        {
            GetProjectPreQuotesFailure.InvalidRequest =>
                PreQuoteProblem(
                    StatusCodes.Status400BadRequest,
                    "Solicitud inválida",
                    "Los parámetros de consulta de precotizaciones no son válidos."),
            GetProjectPreQuotesFailure.Unauthorized =>
                PreQuoteProblem(
                    StatusCodes.Status401Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            GetProjectPreQuotesFailure.InactiveUser =>
                PreQuoteProblem(
                    StatusCodes.Status403Forbidden,
                    "Usuario inactivo",
                    "El usuario no tiene acceso para consultar precotizaciones."),
            GetProjectPreQuotesFailure.ProjectNotFound =>
                PreQuoteProblem(
                    StatusCodes.Status404NotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto indicado."),
            _ => PreQuoteProblem(
                StatusCodes.Status500InternalServerError,
                "Error al consultar precotizaciones",
                "No fue posible consultar las precotizaciones del proyecto.")
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
