using Api.ErrorHandling;
using Application.PreQuotes.ConfirmRequirementTechnicalProposalSelection;
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
[Route("api/v2/technical-proposals/{technicalProposalId:guid}/confirm-selection")]
public sealed class TechnicalProposalSelectionConfirmationController(
    ConfirmRequirementTechnicalProposalSelectionService service)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(
        typeof(ConfirmRequirementTechnicalProposalSelectionResponse),
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
    public async Task<IActionResult> Post(
        [FromRoute] Guid technicalProposalId,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new ConfirmRequirementTechnicalProposalSelectionCommand(
                technicalProposalId),
            cancellationToken);

        if (result.IsSuccess && result.Confirmation is { } confirmation)
        {
            return Ok(new ConfirmRequirementTechnicalProposalSelectionResponse(
                confirmation.TechnicalProposalId,
                confirmation.State,
                confirmation.ConfirmedAtUtc,
                confirmation.ConfirmedByUserId));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(
        ConfirmRequirementTechnicalProposalSelectionFailure failure) =>
        failure switch
        {
            ConfirmRequirementTechnicalProposalSelectionFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "La propuesta tecnica indicada no es valida."),
            ConfirmRequirementTechnicalProposalSelectionFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            ConfirmRequirementTechnicalProposalSelectionFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            ConfirmRequirementTechnicalProposalSelectionFailure.TechnicalProposalNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "No existe la propuesta tecnica indicada."),
            ConfirmRequirementTechnicalProposalSelectionFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento asociado a la propuesta tecnica."),
            ConfirmRequirementTechnicalProposalSelectionFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            ConfirmRequirementTechnicalProposalSelectionFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            ConfirmRequirementTechnicalProposalSelectionFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede confirmar una propuesta de un proyecto inactivo."),
            ConfirmRequirementTechnicalProposalSelectionFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            ConfirmRequirementTechnicalProposalSelectionFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede confirmar una propuesta de un cliente inactivo."),
            ConfirmRequirementTechnicalProposalSelectionFailure.IncompleteTechnicalProposal =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.TechnicalProposalIncomplete,
                    "Propuesta tecnica no lista",
                    "Hay definiciones tecnicas bloqueantes antes de confirmar. Consulta readiness.pendingDefinitions en la propuesta tecnica."),
            ConfirmRequirementTechnicalProposalSelectionFailure.QueryError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de consulta",
                    "No fue posible consultar la propuesta tecnica."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible confirmar la propuesta tecnica.")
        };

    private ObjectResult RequirementProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) =>
        ApiProblemDetailsFactory.Create(HttpContext, statusCode, errorCode, title, detail);
}
