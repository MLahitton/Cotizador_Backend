using Application.PreQuotes.RequirementExperience;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v2/requirements")]
public sealed class RequirementExperienceController(
    GetRequirementExperienceCatalogService getCatalog,
    GetRequirementExperienceDraftsService getDrafts,
    UpdateRequirementExperienceDraftService updateDraft,
    GetRequirementExperienceLevel2Service getLevel2,
    UpdateRequirementExperienceLevel2SelectionService updateLevel2,
    RequirementExperienceConfigurationService configurationService)
    : ControllerBase
{
    [HttpGet("experience-catalog")]
    [ProducesResponseType(typeof(RequirementExperienceCatalogResponse), StatusCodes.Status200OK)]
    public ActionResult<RequirementExperienceCatalogResponse> GetCatalog()
    {
        return Ok(getCatalog.Execute());
    }

    [HttpGet("technical-proposals/{technicalProposalId:guid}/experience-drafts")]
    [ProducesResponseType(typeof(RequirementExperienceDraftsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDrafts(
        Guid technicalProposalId,
        CancellationToken cancellationToken)
    {
        var result = await getDrafts.ExecuteAsync(technicalProposalId, cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!)
            : MapFailure(result.Error);
    }

    [HttpPut("technical-proposals/{technicalProposalId:guid}/items/{technicalProposalItemId:guid}/experience-draft")]
    [ProducesResponseType(typeof(RequirementExperienceItemDraftResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateDraft(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        [FromBody] UpdateRequirementExperienceDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await updateDraft.ExecuteAsync(
            technicalProposalId,
            technicalProposalItemId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!)
            : MapFailure(result.Error);
    }


    [HttpGet("technical-proposals/{technicalProposalId:guid}/items/{technicalProposalItemId:guid}/experience-level2")]
    [ProducesResponseType(typeof(RequirementExperienceLevel2Response), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLevel2(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        CancellationToken cancellationToken)
    {
        var result = await getLevel2.ExecuteAsync(
            technicalProposalId,
            technicalProposalItemId,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!)
            : MapFailure(result.Error);
    }

    [HttpPut("technical-proposals/{technicalProposalId:guid}/items/{technicalProposalItemId:guid}/experience-level2/system")]
    [ProducesResponseType(typeof(RequirementExperienceLevel2Response), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLevel2System(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        [FromBody] UpdateRequirementExperienceLevel2SystemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await updateLevel2.UpdateSystemAsync(
            technicalProposalId,
            technicalProposalItemId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!)
            : MapFailure(result.Error);
    }

    [HttpPut("technical-proposals/{technicalProposalId:guid}/items/{technicalProposalItemId:guid}/experience-level2/glass")]
    [ProducesResponseType(typeof(RequirementExperienceLevel2Response), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateLevel2Glass(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        [FromBody] UpdateRequirementExperienceLevel2GlassRequest request,
        CancellationToken cancellationToken)
    {
        var result = await updateLevel2.UpdateGlassAsync(
            technicalProposalId,
            technicalProposalItemId,
            request,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!)
            : MapFailure(result.Error);
    }

    [HttpGet("technical-proposals/{technicalProposalId:guid}/experience-configuration")]
    [ProducesResponseType(typeof(RequirementExperienceConfigurationStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExperienceConfiguration(
        Guid technicalProposalId,
        CancellationToken cancellationToken)
    {
        var result = await configurationService.GetStatusAsync(
            technicalProposalId,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!)
            : MapFailure(result.Error, result.Value);
    }

    [HttpPost("technical-proposals/{technicalProposalId:guid}/experience-configuration/confirm")]
    [ProducesResponseType(typeof(RequirementExperienceConfigurationStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmExperienceConfiguration(
        Guid technicalProposalId,
        CancellationToken cancellationToken)
    {
        var result = await configurationService.ConfirmAsync(
            technicalProposalId,
            cancellationToken);
        return result.IsSuccess
            ? Ok(result.Value!)
            : MapFailure(result.Error, result.Value);
    }

    private IActionResult MapFailure(
        RequirementExperienceFailure failure,
        RequirementExperienceConfigurationStatusResponse? configuration = null)
    {
        return failure switch
        {
            RequirementExperienceFailure.Forbidden => StatusCode(
                StatusCodes.Status403Forbidden,
                CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "Acceso denegado",
                    "REQUIREMENT_EXPERIENCE_FORBIDDEN",
                    "Solo el propietario puede modificar la experiencia.")),
            RequirementExperienceFailure.Unauthorized => Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Usuario no autenticado",
                "REQUIREMENT_EXPERIENCE_UNAUTHORIZED",
                "Debe iniciar sesion para editar la experiencia.")),
            RequirementExperienceFailure.InactiveUser => StatusCode(
                StatusCodes.Status403Forbidden,
                CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "Usuario inactivo",
                    "REQUIREMENT_EXPERIENCE_INACTIVE_USER",
                    "El usuario autenticado no esta activo.")),
            RequirementExperienceFailure.NotFound or RequirementExperienceFailure.ItemNotFound => NotFound(CreateProblem(
                StatusCodes.Status404NotFound,
                "Recurso no encontrado",
                "REQUIREMENT_EXPERIENCE_NOT_FOUND",
                "No se encontro la propuesta tecnica solicitada.")),
            RequirementExperienceFailure.Conflict => Conflict(CreateProblem(
                StatusCodes.Status409Conflict,
                "Seleccion tecnica desactualizada",
                "REQUIREMENT_EXPERIENCE_CONFLICT",
                "La seleccion tecnica fue modificada por otro usuario. Recargue antes de guardar.")),
            RequirementExperienceFailure.CandidateNotAllowed => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Candidato tecnico no permitido",
                "REQUIREMENT_EXPERIENCE_LEVEL2_CANDIDATE_NOT_ALLOWED",
                "El sistema o vidrio solicitado no pertenece a los candidatos tecnicos disponibles.")),
            RequirementExperienceFailure.ConfigurationBlocked => Conflict(
                CreateConfigurationBlockedProblem(configuration)),
            RequirementExperienceFailure.PersistenceError => StatusCode(
                StatusCodes.Status500InternalServerError,
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    "No fue posible guardar la seleccion tecnica",
                    "REQUIREMENT_EXPERIENCE_PERSISTENCE_ERROR",
                    "Ocurrio un error al guardar la seleccion tecnica.")),
            _ => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud de experiencia invalida",
                "REQUIREMENT_EXPERIENCE_INVALID_REQUEST",
                "El borrador de experiencia no cumple el catalogo vigente."))
        };
    }

    private static ProblemDetails CreateConfigurationBlockedProblem(
        RequirementExperienceConfigurationStatusResponse? configuration)
    {
        var problem = CreateProblem(
            StatusCodes.Status409Conflict,
            "Configuracion Experience bloqueada",
            "REQUIREMENT_EXPERIENCE_CONFIGURATION_BLOCKED",
            "La configuracion actual no esta lista para confirmarse.");
        problem.Extensions["blockingReasons"] =
            configuration?.BlockingReasons ?? [];
        problem.Extensions["reviewReasons"] =
            configuration?.ReviewReasons ?? [];
        problem.Extensions["status"] = configuration?.Status;
        return problem;
    }

    private static ProblemDetails CreateProblem(
        int status,
        string title,
        string code,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail
        };
        problem.Extensions["code"] = code;
        return problem;
    }
}
