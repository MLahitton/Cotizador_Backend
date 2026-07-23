using Application.Projects.CreateProject;
using Contracts.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController(
    CreateProjectService createProjectService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<CreateProjectResponse>(
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
    public async Task<ActionResult<CreateProjectResponse>> Create(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createProjectService.ExecuteAsync(
            new CreateProjectCommand(
                request.ClientId,
                request.Code,
                request.Name,
                request.Description,
                request.Location),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapFailure(result.Failure);
        }

        var project = result.Project!;
        var response = new CreateProjectResponse(
            project.Id,
            project.ClientId,
            project.Code,
            project.Name,
            project.Description,
            project.Location,
            project.IsActive,
            project.CreatedAtUtc,
            project.UpdatedAtUtc);

        return StatusCode(
            StatusCodes.Status201Created,
            response);
    }

    private ActionResult<CreateProjectResponse> MapFailure(
        CreateProjectFailure failure)
    {
        return failure switch
        {
            CreateProjectFailure.InvalidRequest => ProjectProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "Los datos enviados para crear el proyecto no son válidos."),
            CreateProjectFailure.Unauthorized => ProjectProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            CreateProjectFailure.InactiveUser => ProjectProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso para crear proyectos."),
            CreateProjectFailure.ClientNotFound => ProjectProblem(
                StatusCodes.Status404NotFound,
                "Cliente no encontrado",
                "No existe el cliente indicado."),
            CreateProjectFailure.InactiveClient => ProjectProblem(
                StatusCodes.Status409Conflict,
                "Cliente inactivo",
                "No se puede crear un proyecto para un cliente inactivo."),
            CreateProjectFailure.DuplicateCode => ProjectProblem(
                StatusCodes.Status409Conflict,
                "Código de proyecto duplicado",
                "Ya existe un proyecto con el código indicado."),
            _ => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                "Error al crear el proyecto",
                "No fue posible guardar el proyecto.")
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
