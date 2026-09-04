using Api.ErrorHandling;
using Application.PreQuotes.UpdateRequirementTechnicalProposalItemSelection;
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
[Route("api/v2/technical-proposals/{technicalProposalId:guid}/items/{itemId:guid}/selection")]
public sealed class TechnicalProposalItemSelectionController(
    UpdateRequirementTechnicalProposalItemSelectionService service)
    : ControllerBase
{
    [HttpPut]
    [ProducesResponseType(
        typeof(UpdateRequirementTechnicalProposalItemSelectionResponse),
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
    public async Task<IActionResult> Put(
        [FromRoute] Guid technicalProposalId,
        [FromRoute] Guid itemId,
        [FromBody] UpdateRequirementTechnicalProposalItemSelectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new UpdateRequirementTechnicalProposalItemSelectionCommand(
                technicalProposalId,
                itemId,
                request.ConfirmSuggested,
                request.SystemId,
                request.GlassId,
                request.FinishId,
                request.Quantity,
                request.WidthMm,
                request.HeightMm),
            cancellationToken);

        if (result.IsSuccess && result.Selection is { } selection)
        {
            return Ok(Map(selection));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(
        UpdateRequirementTechnicalProposalItemSelectionFailure failure)
    {
        return failure switch
        {
            UpdateRequirementTechnicalProposalItemSelectionFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "La seleccion indicada no es valida."),
            UpdateRequirementTechnicalProposalItemSelectionFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            UpdateRequirementTechnicalProposalItemSelectionFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .TechnicalProposalNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "No existe la propuesta tecnica indicada."),
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .TechnicalProposalItemNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Item de propuesta no encontrado",
                    "No existe el item indicado en la propuesta tecnica."),
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento asociado a la propuesta tecnica."),
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            UpdateRequirementTechnicalProposalItemSelectionFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemSelectionFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede modificar una seleccion de un proyecto inactivo."),
            UpdateRequirementTechnicalProposalItemSelectionFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            UpdateRequirementTechnicalProposalItemSelectionFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede modificar una seleccion de un cliente inactivo."),
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .InvalidSystemSelection =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Sistema invalido",
                    "El sistema seleccionado no existe o no esta activo para seleccion."),
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .FunctionalTypeMismatch =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.TechnicalProposalFunctionalTypeMismatch,
                    "Sistema incompatible",
                    "El sistema seleccionado no pertenece a la funcion del requerimiento."),
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .InvalidGlassSelection =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Cristal invalido",
                    "El cristal seleccionado no existe o no esta activo para seleccion."),
            UpdateRequirementTechnicalProposalItemSelectionFailure
                .InvalidFinishSelection =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Acabado invalido",
                    "El acabado seleccionado no existe o no esta activo para seleccion."),
            UpdateRequirementTechnicalProposalItemSelectionFailure.QueryError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de consulta",
                    "No fue posible consultar la propuesta tecnica."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible guardar la seleccion.")
        };
    }

    private static UpdateRequirementTechnicalProposalItemSelectionResponse Map(
        RequirementTechnicalProposalItemSelectionReadModel selection) =>
        new(
            selection.TechnicalProposalId,
            selection.ItemId,
            selection.SelectionState,
            selection.SelectedAtUtc,
            selection.SelectedByUserId,
            Map(selection.System),
            Map(selection.Glass),
            Map(selection.Finish));

    private static RequirementTechnicalProposalSystemOptionResponse? Map(
        Application.PreQuotes.GetRequirementTechnicalProposal
            .RequirementTechnicalProposalSystemOptionReadModel? option) =>
        option is null
            ? null
            : new(
                option.Id,
                option.Code,
                option.DisplayName,
                option.TechnicalName,
                option.CommercialName,
                option.FunctionalType,
                option.Family,
                option.Series,
                option.CommercialLine,
                option.Variant);

    private static RequirementTechnicalProposalGlassOptionResponse? Map(
        Application.PreQuotes.GetRequirementTechnicalProposal
            .RequirementTechnicalProposalGlassOptionReadModel? option) =>
        option is null
            ? null
            : new(
                option.Id,
                option.Code,
                option.DisplayName,
                option.Family,
                option.Composition,
                option.Treatment,
                option.OuterThicknessMm,
                option.InnerThicknessMm,
                option.PvbThicknessMm,
                option.PvbType,
                option.PvbColor,
                option.ChamberThicknessMm,
                option.ProductLine,
                option.ProductToken,
                option.Pattern,
                option.Color);

    private static RequirementTechnicalProposalFinishOptionResponse? Map(
        Application.PreQuotes.GetRequirementTechnicalProposal
            .RequirementTechnicalProposalFinishOptionReadModel? option) =>
        option is null
            ? null
            : new(
                option.Id,
                option.Code,
                option.DisplayName,
                option.NormalizedType,
                option.Color,
                option.Texture,
                option.Process,
                option.CommercialCode,
                option.Material);

    private ObjectResult RequirementProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail)
    {
        return ApiProblemDetailsFactory.Create(
            HttpContext,
            statusCode,
            errorCode,
            title,
            detail);
    }
}
