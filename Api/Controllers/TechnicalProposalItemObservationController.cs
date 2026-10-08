using Api.ErrorHandling;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemObservation;
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
[Route("api/v2/technical-proposals/{technicalProposalId:guid}/items/{itemId:guid}/observation")]
public sealed class TechnicalProposalItemObservationController(
    UpdateRequirementTechnicalProposalItemObservationService service)
    : ControllerBase
{
    [HttpPut]
    [ProducesResponseType(
        typeof(UpdateRequirementTechnicalProposalItemObservationResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Put(
        [FromRoute] Guid technicalProposalId,
        [FromRoute] Guid itemId,
        [FromBody] UpdateRequirementTechnicalProposalItemObservationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemObservationCommand(
                technicalProposalId,
                itemId,
                request.Observation),
            cancellationToken);

        if (result.IsSuccess && result.Observation is { } observation)
        {
            return Ok(Map(observation));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(UpdateRequirementTechnicalProposalItemObservationFailure failure) =>
        failure switch
        {
            UpdateRequirementTechnicalProposalItemObservationFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "La observacion indicada no es valida."),
            UpdateRequirementTechnicalProposalItemObservationFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            UpdateRequirementTechnicalProposalItemObservationFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            UpdateRequirementTechnicalProposalItemObservationFailure.TechnicalProposalNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "No existe la propuesta tecnica indicada."),
            UpdateRequirementTechnicalProposalItemObservationFailure.TechnicalProposalItemNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Item de propuesta no encontrado",
                    "No existe el item indicado en la propuesta tecnica."),
            UpdateRequirementTechnicalProposalItemObservationFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento asociado a la propuesta tecnica."),
            UpdateRequirementTechnicalProposalItemObservationFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            UpdateRequirementTechnicalProposalItemObservationFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemObservationFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede modificar la observacion de un proyecto inactivo."),
            UpdateRequirementTechnicalProposalItemObservationFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemObservationFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede modificar la observacion de un cliente inactivo."),
            UpdateRequirementTechnicalProposalItemObservationFailure.QueryError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de consulta",
                    "No fue posible consultar la propuesta tecnica."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible guardar la observacion.")
        };

    private static UpdateRequirementTechnicalProposalItemObservationResponse Map(
        RequirementTechnicalProposalItemObservationReadModel observation) =>
        new(
            observation.TechnicalProposalId,
            observation.ItemId,
            observation.ManualObservation);

    private ObjectResult RequirementProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) =>
        ApiProblemDetailsFactory.Create(
            HttpContext,
            statusCode,
            errorCode,
            title,
            detail);
}