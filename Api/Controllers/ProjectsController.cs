using Application.Projects.CreateProject;
using Application.Projects.GetProjectById;
using Application.Projects.UpdateProject;
using Contracts.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController(
    CreateProjectService createProjectService,
    GetProjectByIdService getProjectByIdService,
    UpdateProjectService updateProjectService)
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

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ProjectDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProjectDetailsResponse>> GetById(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await getProjectByIdService.ExecuteAsync(
            new GetProjectByIdQuery(projectId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapGetProjectByIdFailure(result.Failure);
        }

        var project = result.Project!;

        return Ok(new ProjectDetailsResponse(
            project.Id,
            project.ClientId,
            project.Code,
            project.Name,
            project.Description,
            project.Location,
            project.IsActive,
            project.CreatedAtUtc,
            project.UpdatedAtUtc));
    }

    [HttpPut("{projectId:guid}")]
    [ProducesResponseType<ProjectDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProjectDetailsResponse>> Update(
        Guid projectId,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var result = await updateProjectService.ExecuteAsync(
            new UpdateProjectCommand(
                projectId,
                request.Code,
                request.Name,
                request.Description,
                request.Location),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapUpdateProjectFailure(result.Failure);
        }

        var project = result.Project!;

        return Ok(new ProjectDetailsResponse(
            project.Id,
            project.ClientId,
            project.Code,
            project.Name,
            project.Description,
            project.Location,
            project.IsActive,
            project.CreatedAtUtc,
            project.UpdatedAtUtc));
    }

    private ActionResult<ProjectDetailsResponse> MapGetProjectByIdFailure(
        GetProjectByIdFailure failure)
    {
        return failure switch
        {
            GetProjectByIdFailure.InvalidRequest => ProjectProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "El identificador del proyecto no es válido."),
            GetProjectByIdFailure.Unauthorized => ProjectProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            GetProjectByIdFailure.InactiveUser => ProjectProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso para consultar proyectos."),
            GetProjectByIdFailure.NotFound => ProjectProblem(
                StatusCodes.Status404NotFound,
                "Proyecto no encontrado",
                "No existe un proyecto con el identificador indicado."),
            _ => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                "Error al consultar el proyecto",
                "No fue posible consultar el proyecto.")
        };
    }

    private ActionResult<ProjectDetailsResponse> MapUpdateProjectFailure(
        UpdateProjectFailure failure)
    {
        return failure switch
        {
            UpdateProjectFailure.InvalidRequest => ProjectProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "Los datos enviados para actualizar el proyecto no son válidos."),
            UpdateProjectFailure.Unauthorized => ProjectProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            UpdateProjectFailure.InactiveUser => ProjectProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso para actualizar proyectos."),
            UpdateProjectFailure.NotFound => ProjectProblem(
                StatusCodes.Status404NotFound,
                "Proyecto no encontrado",
                "No existe un proyecto con el identificador indicado."),
            UpdateProjectFailure.DuplicateCode => ProjectProblem(
                StatusCodes.Status409Conflict,
                "Código de proyecto duplicado",
                "Ya existe otro proyecto con el código indicado."),
            UpdateProjectFailure.QueryError => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                "Error al consultar el proyecto",
                "No fue posible consultar el proyecto para actualizarlo."),
            _ => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                "Error al actualizar el proyecto",
                "No fue posible guardar los cambios del proyecto.")
        };
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
