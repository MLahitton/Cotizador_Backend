using Api.ErrorHandling;
using Application.Projects.GetProjectDiagnosis;
using Contracts.Common;
using Contracts.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[ContractualErrors(InvalidRequestErrorCode = ProjectErrorCodes.InvalidRequest)]
[Route("api/v2/projects/{projectId:guid}/diagnosis")]
public sealed class ProjectDiagnosisController(GetProjectDiagnosisService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProjectDiagnosisResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetailsResponse>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProjectDiagnosisResponse>> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(projectId, cancellationToken);
        if (result.Failure != GetProjectDiagnosisFailure.None)
        {
            var (status, code, title) = result.Failure switch
            {
                GetProjectDiagnosisFailure.InvalidRequest => (400, ProjectErrorCodes.InvalidRequest, "Solicitud invalida"),
                GetProjectDiagnosisFailure.Unauthorized => (401, ProjectErrorCodes.Unauthorized, "No autorizado"),
                GetProjectDiagnosisFailure.InactiveUser => (403, ProjectErrorCodes.InactiveUser, "Usuario inactivo"),
                GetProjectDiagnosisFailure.NotFound => (404, ProjectErrorCodes.ProjectNotFound, "Proyecto no encontrado"),
                _ => (500, ProjectErrorCodes.QueryError, "Error al consultar el diagnostico")
            };
            return ApiProblemDetailsFactory.Create(HttpContext, status, code, title,
                "No fue posible consultar el diagnostico del proyecto.");
        }

        var value = result.Result!;
        var diagnosis = value.Diagnosis;
        return Ok(new ProjectDiagnosisResponse(value.Status, diagnosis is null ? null : new(
            diagnosis.Source, diagnosis.SubmissionId, diagnosis.SchemaVersion, diagnosis.InterpretationVersion,
            Map(diagnosis.General), Map(diagnosis.Priorities), Map(diagnosis.Preferences),
            Map(diagnosis.LivingContext), Map(diagnosis.Needs), Map(diagnosis.Decision), Map(diagnosis.SupplementalNotes))));
    }

    private static IReadOnlyList<ProjectDiagnosisEntryResponse> Map(IReadOnlyList<ProjectDiagnosisEntry> entries) =>
        entries.Select(x => new ProjectDiagnosisEntryResponse(x.QuestionId, x.DisplayLabel, x.Availability,
            x.Code, x.Codes, x.NumericValue, x.TextValue, x.DisplayValue, x.DisplayValues)).ToArray();
}
