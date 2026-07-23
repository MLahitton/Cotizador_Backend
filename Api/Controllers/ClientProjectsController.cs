using Application.Projects.GetClientProjects;
using Contracts.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/clients/{clientId:guid}/projects")]
public sealed class ClientProjectsController(
    GetClientProjectsService getClientProjectsService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetClientProjectsResponse>(
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
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetClientProjectsResponse>> Get(
        [FromRoute] Guid clientId,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await getClientProjectsService.ExecuteAsync(
            new GetClientProjectsQuery(
                clientId,
                search,
                page,
                pageSize),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapFailure(result.Failure);
        }

        var projectsPage = result.Page!;
        var items = projectsPage.Items
            .Select(project => new ProjectListItemResponse(
                project.Id,
                project.ClientId,
                project.Code,
                project.Name,
                project.Description,
                project.Location,
                project.IsActive,
                project.CreatedAtUtc,
                project.UpdatedAtUtc))
            .ToArray();

        return Ok(new GetClientProjectsResponse(
            items,
            projectsPage.Page,
            projectsPage.PageSize,
            projectsPage.TotalCount,
            projectsPage.TotalPages));
    }

    private ActionResult<GetClientProjectsResponse> MapFailure(
        GetClientProjectsFailure failure)
    {
        return failure switch
        {
            GetClientProjectsFailure.InvalidRequest => ProjectProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "Los parámetros de consulta de proyectos no son válidos."),
            GetClientProjectsFailure.Unauthorized => ProjectProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            GetClientProjectsFailure.InactiveUser => ProjectProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso para consultar proyectos."),
            GetClientProjectsFailure.ClientNotFound => ProjectProblem(
                StatusCodes.Status404NotFound,
                "Cliente no encontrado",
                "No existe el cliente indicado."),
            GetClientProjectsFailure.InactiveClient => ProjectProblem(
                StatusCodes.Status409Conflict,
                "Cliente inactivo",
                "No se pueden consultar proyectos para un cliente inactivo."),
            _ => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                "Error al consultar proyectos",
                "No fue posible consultar los proyectos.")
        };
    }

    private ObjectResult ProjectProblem(
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
