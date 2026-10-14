using Api.ErrorHandling;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemPhysicalData;
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
[Route("api/v2/technical-proposals/{technicalProposalId:guid}/items/{itemId:guid}/physical-data")]
public sealed class TechnicalProposalItemPhysicalDataController(
    UpdateRequirementTechnicalProposalItemPhysicalDataService service)
    : ControllerBase
{
    [HttpPut]
    [ProducesResponseType(
        typeof(UpdateRequirementTechnicalProposalItemPhysicalDataResponse),
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
        [FromBody] UpdateRequirementTechnicalProposalItemPhysicalDataRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemPhysicalDataCommand(
                technicalProposalId,
                itemId,
                request.Quantity,
                request.WidthMm,
                request.HeightMm),
            cancellationToken);

        if (result.IsSuccess && result.PhysicalData is { } location)
        {
            return Ok(Map(location));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(UpdateRequirementTechnicalProposalItemPhysicalDataFailure failure) =>
        failure switch
        {
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.Forbidden =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    "REQUIREMENT_PHYSICAL_DATA_FORBIDDEN",
                    "Acceso denegado",
                    "Solo el propietario puede modificar el item."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "La informacion fisica indicada no es valida."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.TechnicalProposalNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "No existe la propuesta tecnica indicada."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.TechnicalProposalItemNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Item de propuesta no encontrado",
                    "No existe el item indicado en la propuesta tecnica."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento asociado a la propuesta tecnica."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede modificar la informacion fisica de un proyecto inactivo."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede modificar la informacion fisica de un cliente inactivo."),
            UpdateRequirementTechnicalProposalItemPhysicalDataFailure.QueryError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de consulta",
                    "No fue posible consultar la propuesta tecnica."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible guardar la informacion fisica.")
        };

    private static UpdateRequirementTechnicalProposalItemPhysicalDataResponse Map(
        RequirementTechnicalProposalItemPhysicalDataReadModel location) =>
        new(
            location.TechnicalProposalId,
            location.ItemId,
            location.Quantity,
            location.WidthMm,
            location.HeightMm,
            location.CommercialRevision,
            new RequirementTechnicalProposalItemDataCompletenessResponse(
                location.DataCompleteness.State,
                location.DataCompleteness.IsComplete,
                location.DataCompleteness.MissingFields));

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
