using Api.ErrorHandling;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemInclusion;
using Contracts.Common;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[ContractualErrors(
    InvalidRequestErrorCode = RequirementErrorCodes.InvalidRequest,
    UnsupportedMediaTypeErrorCode = RequirementErrorCodes.InvalidRequest)]
[Route("api/v2/requirements/{requirementId:guid}/technical-proposal/items/{itemId:guid}/inclusion")]
public sealed class TechnicalProposalItemInclusionController(
    UpdateRequirementTechnicalProposalItemInclusionService service)
    : ControllerBase
{
    [HttpPatch]
    [ProducesResponseType(
        typeof(UpdateRequirementTechnicalProposalItemInclusionResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Patch(
        [FromRoute] Guid requirementId,
        [FromRoute] Guid itemId,
        [FromBody] UpdateRequirementTechnicalProposalItemInclusionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemInclusionCommand(
                requirementId,
                itemId,
                request.IsIncluded,
                request.Reason),
            cancellationToken);

        if (result.IsSuccess && result.Inclusion is { } inclusion)
        {
            return Ok(new UpdateRequirementTechnicalProposalItemInclusionResponse(
                inclusion.TechnicalProposalId,
                inclusion.ItemId,
                inclusion.IsIncluded,
                inclusion.ExcludedAtUtc,
                inclusion.ExcludedByUserId,
                inclusion.ExclusionReason,
                inclusion.CommercialRevision));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(
        UpdateRequirementTechnicalProposalItemInclusionFailure failure) =>
        failure switch
        {
            UpdateRequirementTechnicalProposalItemInclusionFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El cambio de inclusion indicado no es valido."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.TechnicalProposalNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "El requerimiento todavia no tiene una propuesta tecnica vigente."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.TechnicalProposalItemNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Item tecnico no encontrado",
                    "El item indicado no pertenece a la propuesta tecnica vigente."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede modificar una propuesta de un proyecto inactivo."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede modificar una propuesta de un cliente inactivo."),
            UpdateRequirementTechnicalProposalItemInclusionFailure.QueryError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de consulta",
                    "No fue posible consultar la propuesta tecnica."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible guardar el cambio de inclusion.")
        };

    private ObjectResult RequirementProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) =>
        ApiProblemDetailsFactory.Create(HttpContext, statusCode, errorCode, title, detail);
}
