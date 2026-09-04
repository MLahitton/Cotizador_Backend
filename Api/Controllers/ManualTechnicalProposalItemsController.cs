using Api.ErrorHandling;
using Application.PreQuotes.CreateManualRequirementTechnicalProposalItem;
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
[Route("api/v2/requirements/{requirementId:guid}/technical-proposal/items")]
public sealed class ManualTechnicalProposalItemsController(
    CreateManualRequirementTechnicalProposalItemService service)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(
        typeof(CreateManualRequirementTechnicalProposalItemResponse),
        StatusCodes.Status201Created)]
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
        [FromRoute] Guid requirementId,
        [FromBody] CreateManualRequirementTechnicalProposalItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new CreateManualRequirementTechnicalProposalItemCommand(
                requirementId,
                request.Reference,
                request.Description,
                request.ElementType,
                request.Quantity,
                request.WidthMillimeters,
                request.HeightMillimeters,
                request.SystemId,
                request.GlassTypeId,
                request.FinishTypeId,
                request.Note),
            cancellationToken);

        if (result.IsSuccess && result.Item is { } item)
        {
            return CreatedAtAction(
                nameof(RequirementTechnicalProposalController.Get),
                "RequirementTechnicalProposal",
                new { requirementId },
                new CreateManualRequirementTechnicalProposalItemResponse(
                    item.TechnicalProposalId,
                    item.ItemId,
                    ToContract(item.Source),
                    item.Sequence,
                    item.CommercialRevision));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(
        CreateManualRequirementTechnicalProposalItemFailure failure) =>
        failure switch
        {
            CreateManualRequirementTechnicalProposalItemFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El item manual indicado no es valido."),
            CreateManualRequirementTechnicalProposalItemFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            CreateManualRequirementTechnicalProposalItemFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            CreateManualRequirementTechnicalProposalItemFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            CreateManualRequirementTechnicalProposalItemFailure.TechnicalProposalNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "El requerimiento todavia no tiene una propuesta tecnica vigente."),
            CreateManualRequirementTechnicalProposalItemFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            CreateManualRequirementTechnicalProposalItemFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            CreateManualRequirementTechnicalProposalItemFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede modificar una propuesta de un proyecto inactivo."),
            CreateManualRequirementTechnicalProposalItemFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            CreateManualRequirementTechnicalProposalItemFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede modificar una propuesta de un cliente inactivo."),
            CreateManualRequirementTechnicalProposalItemFailure.InvalidSystemSelection =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Sistema invalido",
                    "El sistema seleccionado no esta disponible para la propuesta."),
            CreateManualRequirementTechnicalProposalItemFailure.InvalidGlassSelection =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Vidrio invalido",
                    "El vidrio seleccionado no esta disponible para la propuesta."),
            CreateManualRequirementTechnicalProposalItemFailure.InvalidFinishSelection =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Acabado invalido",
                    "El acabado seleccionado no esta disponible para la propuesta."),
            CreateManualRequirementTechnicalProposalItemFailure.QueryError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de consulta",
                    "No fue posible consultar la propuesta tecnica."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible agregar el item manual.")
        };

    private static string ToContract(string source) =>
        source == nameof(Domain.PreQuotes.TechnicalProposalItemSource.Manual)
            ? "MANUAL"
            : "AI_EXTRACTED";

    private ObjectResult RequirementProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) =>
        ApiProblemDetailsFactory.Create(HttpContext, statusCode, errorCode, title, detail);
}
