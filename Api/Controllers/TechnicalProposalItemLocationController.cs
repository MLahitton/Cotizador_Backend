using Api.ErrorHandling;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemLocation;
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
[Route("api/v2/technical-proposals/{technicalProposalId:guid}/items/{itemId:guid}/location")]
public sealed class TechnicalProposalItemLocationController(
    UpdateRequirementTechnicalProposalItemLocationService service)
    : ControllerBase
{
    [HttpPut]
    [ProducesResponseType(
        typeof(UpdateRequirementTechnicalProposalItemLocationResponse),
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
        [FromBody] UpdateRequirementTechnicalProposalItemLocationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemLocationCommand(
                technicalProposalId,
                itemId,
                request.Location),
            cancellationToken);

        if (result.IsSuccess && result.Location is { } location)
        {
            return Ok(Map(location));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(UpdateRequirementTechnicalProposalItemLocationFailure failure) =>
        failure switch
        {
            UpdateRequirementTechnicalProposalItemLocationFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "La ubicacion indicada no es valida."),
            UpdateRequirementTechnicalProposalItemLocationFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            UpdateRequirementTechnicalProposalItemLocationFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            UpdateRequirementTechnicalProposalItemLocationFailure.TechnicalProposalNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "No existe la propuesta tecnica indicada."),
            UpdateRequirementTechnicalProposalItemLocationFailure.TechnicalProposalItemNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Item de propuesta no encontrado",
                    "No existe el item indicado en la propuesta tecnica."),
            UpdateRequirementTechnicalProposalItemLocationFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento asociado a la propuesta tecnica."),
            UpdateRequirementTechnicalProposalItemLocationFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            UpdateRequirementTechnicalProposalItemLocationFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemLocationFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede modificar la ubicacion de un proyecto inactivo."),
            UpdateRequirementTechnicalProposalItemLocationFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemLocationFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede modificar la ubicacion de un cliente inactivo."),
            UpdateRequirementTechnicalProposalItemLocationFailure.QueryError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de consulta",
                    "No fue posible consultar la propuesta tecnica."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible guardar la ubicacion.")
        };

    private static UpdateRequirementTechnicalProposalItemLocationResponse Map(
        RequirementTechnicalProposalItemLocationReadModel location) =>
        new(
            location.TechnicalProposalId,
            location.ItemId,
            location.ExtractedLocation,
            location.ManualLocationOverride,
            location.EffectiveLocation);

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