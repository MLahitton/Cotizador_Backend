using Application.Projects.CreateProject;
using Application.Projects.GetProjectById;
using Application.Projects.GetProjects;
using Application.Projects.SetProjectActivation;
using Application.Projects.UpdateProject;
using Contracts.Common;
using Contracts.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsController(
    CreateProjectService createProjectService,
    GetProjectsService getProjectsService,
    GetProjectByIdService getProjectByIdService,
    UpdateProjectService updateProjectService,
    SetProjectActivationService setProjectActivationService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetProjectsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetProjectsResponse>> Get(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] Guid? clientId = null,
        [FromQuery] string? clientType = null,
        [FromQuery] string? documentType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await getProjectsService.ExecuteAsync(
            new GetProjectsQuery(
                search,
                status,
                clientId,
                clientType,
                documentType,
                page,
                pageSize),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapGetProjectsFailure(result.Failure);
        }

        var projects = result.Page!;
        var items = projects.Items
            .Select(project =>
                new AdministrativeProjectListItemResponse(
                    project.Id,
                    project.ClientId,
                    project.Code,
                    project.Name,
                    project.Description,
                    project.Location,
                    project.IsActive,
                    project.CreatedAtUtc,
                    project.UpdatedAtUtc,
                    new ProjectClientSummaryResponse(
                        project.Client.Id,
                        project.Client.ClientType.ToString(),
                        project.Client.LegalName,
                        project.Client.TradeName,
                        project.Client.DocumentType?.ToString(),
                        project.Client.DocumentNumber)))
            .ToArray();

        return Ok(new GetProjectsResponse(
            items,
            projects.Page,
            projects.PageSize,
            projects.TotalCount,
            projects.TotalPages));
    }

    [HttpPost]
    [ProducesResponseType<CreateProjectResponse>(
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

    [HttpPatch("{projectId:guid}/activation")]
    [ProducesResponseType<ProjectDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProjectDetailsResponse>> SetActivation(
        Guid projectId,
        [FromBody] SetProjectActivationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await setProjectActivationService.ExecuteAsync(
            new SetProjectActivationCommand(
                projectId,
                request.IsActive),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapSetProjectActivationFailure(result.Failure);
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

    private ActionResult<GetProjectsResponse> MapGetProjectsFailure(
        GetProjectsFailure failure)
    {
        return failure switch
        {
            GetProjectsFailure.InvalidRequest => ProjectProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "Los parámetros de consulta de proyectos no son válidos."),
            GetProjectsFailure.Unauthorized => ProjectProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            GetProjectsFailure.InactiveUser => ProjectProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no tiene acceso para consultar proyectos."),
            _ => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                "Error al consultar proyectos",
                "No fue posible consultar los proyectos.")
        };
    }

    private ActionResult<ProjectDetailsResponse>
        MapSetProjectActivationFailure(
            SetProjectActivationFailure failure)
    {
        return failure switch
        {
            SetProjectActivationFailure.InvalidRequest => ProjectProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud inválida",
                "Los datos para cambiar el estado no son válidos."),
            SetProjectActivationFailure.Unauthorized => ProjectProblem(
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            SetProjectActivationFailure.InactiveUser => ProjectProblem(
                StatusCodes.Status403Forbidden,
                "Usuario inactivo",
                "El usuario no puede cambiar el estado de proyectos."),
            SetProjectActivationFailure.NotFound => ProjectProblem(
                StatusCodes.Status404NotFound,
                "Proyecto no encontrado",
                "No existe un proyecto con el identificador indicado."),
            SetProjectActivationFailure.QueryError => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                "Error al consultar el proyecto",
                "No fue posible consultar el proyecto."),
            _ => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                "Error al cambiar el estado",
                "No fue posible guardar el nuevo estado del proyecto.")
        };
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
                ProjectErrorCodes.InvalidRequest,
                "Solicitud inválida",
                "Los datos enviados para crear el proyecto no son válidos."),
            CreateProjectFailure.Unauthorized => ProjectProblem(
                StatusCodes.Status401Unauthorized,
                ProjectErrorCodes.Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            CreateProjectFailure.InactiveUser => ProjectProblem(
                StatusCodes.Status403Forbidden,
                ProjectErrorCodes.InactiveUser,
                "Usuario inactivo",
                "El usuario no tiene acceso para crear proyectos."),
            CreateProjectFailure.ClientNotFound => ProjectProblem(
                StatusCodes.Status404NotFound,
                ProjectErrorCodes.ClientNotFound,
                "Cliente no encontrado",
                "No existe el cliente indicado."),
            CreateProjectFailure.InactiveClient => ProjectProblem(
                StatusCodes.Status409Conflict,
                ProjectErrorCodes.ClientInactive,
                "Cliente inactivo",
                "No se puede crear un proyecto para un cliente inactivo."),
            CreateProjectFailure.DuplicateCode => ProjectProblem(
                StatusCodes.Status409Conflict,
                ProjectErrorCodes.DuplicateCode,
                "Código de proyecto duplicado",
                "Ya existe un proyecto con el código indicado."),
            _ => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                ProjectErrorCodes.PersistenceError,
                "Error al crear el proyecto",
                "No fue posible guardar el proyecto.")
        };
    }

    private ObjectResult ProjectProblem(
        int statusCode,
        string code,
        string title,
        string detail)
    {
        var result = ProjectProblem(statusCode, title, detail);

        if (result.Value is ProblemDetails problemDetails)
        {
            problemDetails.Extensions["code"] = code;

            if (!problemDetails.Extensions.TryGetValue(
                    "traceId",
                    out var existingTraceId)
                || existingTraceId is not string traceId
                || string.IsNullOrWhiteSpace(traceId))
            {
                problemDetails.Extensions["traceId"] =
                    HttpContext.TraceIdentifier;
            }
        }

        return result;
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
