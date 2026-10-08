using Api.ErrorHandling;
using Application.Projects.GetProjectWorkspace;
using Contracts.Common;
using Contracts.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[ContractualErrors(InvalidRequestErrorCode = ProjectErrorCodes.InvalidRequest)]
[Route("api/v2/projects")]
public sealed class ProjectWorkspaceController(
    GetProjectWorkspaceService getProjectWorkspaceService)
    : ControllerBase
{
    [HttpGet("{projectId:guid}/workspace")]
    [ProducesResponseType<ProjectWorkspaceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProjectWorkspaceResponse>> Get(
        [FromRoute] Guid projectId,
        CancellationToken cancellationToken)
    {
        var result = await getProjectWorkspaceService.ExecuteAsync(
            new GetProjectWorkspaceQuery(projectId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return MapFailure(result.Failure);
        }

        var workspace = result.Workspace!;

        return Ok(new ProjectWorkspaceResponse(
            new ProjectWorkspaceProjectResponse(
                workspace.Project.Id,
                workspace.Project.ClientId,
                workspace.Project.Code,
                workspace.Project.Name,
                workspace.Project.Location,
                workspace.Project.IsActive,
                workspace.Project.CreatedAtUtc,
                workspace.Project.UpdatedAtUtc),
            new ProjectWorkspaceClientResponse(
                workspace.Client.Id,
                workspace.Client.ClientType,
                workspace.Client.LegalName,
                workspace.Client.TradeName,
                workspace.Client.Email,
                workspace.Client.Phone,
                workspace.Client.City),
            new ProjectWorkspaceWorkflowResponse(
                ToContract(workspace.Workflow.ResolutionState),
                workspace.Workflow.PreQuoteId,
                workspace.Workflow.RequirementId,
                workspace.Workflow.RequirementStatus,
                workspace.Workflow.TechnicalProposalId,
                workspace.Workflow.HasTechnicalProposal)));
    }

    private ActionResult<ProjectWorkspaceResponse> MapFailure(
        GetProjectWorkspaceFailure failure)
    {
        return failure switch
        {
            GetProjectWorkspaceFailure.InvalidRequest => ProjectProblem(
                StatusCodes.Status400BadRequest,
                ProjectErrorCodes.InvalidRequest,
                "Solicitud invalida",
                "El identificador del proyecto no es valido."),
            GetProjectWorkspaceFailure.Unauthorized => ProjectProblem(
                StatusCodes.Status401Unauthorized,
                ProjectErrorCodes.Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            GetProjectWorkspaceFailure.InactiveUser => ProjectProblem(
                StatusCodes.Status403Forbidden,
                ProjectErrorCodes.InactiveUser,
                "Usuario inactivo",
                "El usuario no tiene acceso para consultar proyectos."),
            GetProjectWorkspaceFailure.NotFound => ProjectProblem(
                StatusCodes.Status404NotFound,
                ProjectErrorCodes.ProjectNotFound,
                "Proyecto no encontrado",
                "No existe un proyecto con el identificador indicado."),
            _ => ProjectProblem(
                StatusCodes.Status500InternalServerError,
                ProjectErrorCodes.QueryError,
                "Error al consultar el workspace del proyecto",
                "No fue posible consultar el contexto del proyecto.")
        };
    }

    private ObjectResult ProjectProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) => ApiProblemDetailsFactory.Create(
        HttpContext,
        statusCode,
        errorCode,
        title,
        detail);

    private static string ToContract(
        ProjectWorkspaceResolutionState resolutionState) =>
        resolutionState switch
        {
            ProjectWorkspaceResolutionState.Empty => "EMPTY",
            ProjectWorkspaceResolutionState.Resolved => "RESOLVED",
            ProjectWorkspaceResolutionState.Ambiguous => "AMBIGUOUS",
            _ => throw new ArgumentOutOfRangeException(nameof(resolutionState))
        };
}
