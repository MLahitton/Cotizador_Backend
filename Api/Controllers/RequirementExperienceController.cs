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
    UpdateRequirementExperienceDraftService updateDraft)
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

    private IActionResult MapFailure(RequirementExperienceFailure failure)
    {
        return failure switch
        {
            RequirementExperienceFailure.Unauthorized => Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Usuario no autenticado",
                "REQUIREMENT_EXPERIENCE_UNAUTHORIZED",
                "Debe iniciar sesion para editar la experiencia.")),
            RequirementExperienceFailure.NotFound or RequirementExperienceFailure.ItemNotFound => NotFound(CreateProblem(
                StatusCodes.Status404NotFound,
                "Recurso no encontrado",
                "REQUIREMENT_EXPERIENCE_NOT_FOUND",
                "No se encontro la propuesta tecnica solicitada.")),
            RequirementExperienceFailure.Conflict => Conflict(CreateProblem(
                StatusCodes.Status409Conflict,
                "Borrador desactualizado",
                "REQUIREMENT_EXPERIENCE_CONFLICT",
                "El borrador fue modificado por otro usuario. Recargue antes de guardar.")),
            _ => BadRequest(CreateProblem(
                StatusCodes.Status400BadRequest,
                "Solicitud de experiencia invalida",
                "REQUIREMENT_EXPERIENCE_INVALID_REQUEST",
                "El borrador de experiencia no cumple el catalogo vigente."))
        };
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
