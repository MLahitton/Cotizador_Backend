using Api.ErrorHandling;
using Application.Common.Abstractions.HistoricalPricing;
using Application.PreQuotes.PriceRequirementTechnicalProposal;
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
[Route("api/v2/requirements/{requirementId}/pricing")]
public sealed class RequirementPricingController(
    PriceRequirementTechnicalProposalService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(RequirementPricingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid requirementId,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new PriceRequirementTechnicalProposalCommand(requirementId),
            cancellationToken);

        if (result.IsSuccess && result.Pricing is { } pricing)
        {
            return Ok(Map(pricing));
        }

        return MapFailure(result.Failure);
    }

    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid requirementId,
        CancellationToken cancellationToken)
    {
        if (requirementId == Guid.Empty)
        {
            return RequirementProblem(
                StatusCodes.Status400BadRequest,
                RequirementErrorCodes.InvalidRequest,
                "Solicitud invalida",
                "El requerimiento indicado no es valido.");
        }

        await service.CancelAsync(requirementId, cancellationToken);
        return Accepted();
    }

    [HttpPost("items/{technicalProposalItemId:guid}/reprice")]
    [ProducesResponseType(typeof(RepriceRequirementPricingItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RepriceItem(
        [FromRoute] Guid requirementId,
        [FromRoute] Guid technicalProposalItemId,
        [FromBody] RepriceRequirementPricingItemRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.RepriceItemAsync(
            new RepriceRequirementTechnicalProposalItemCommand(
                requirementId,
                technicalProposalItemId,
                request.SystemId,
                request.GlassTypeId,
                request.FinishTypeId,
                request.Quantity,
                request.WidthMm,
                request.HeightMm),
            cancellationToken);

        if (result.IsSuccess && result.Pricing is { } pricing)
        {
            return Ok(Map(pricing));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(PriceRequirementTechnicalProposalFailure failure) =>
        failure switch
        {
            PriceRequirementTechnicalProposalFailure.InvalidRequest =>
                RequirementProblem(StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El requerimiento indicado no es valido."),
            PriceRequirementTechnicalProposalFailure.Unauthorized =>
                RequirementProblem(StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            PriceRequirementTechnicalProposalFailure.InactiveUser =>
                RequirementProblem(StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            PriceRequirementTechnicalProposalFailure.RequirementNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            PriceRequirementTechnicalProposalFailure.PreQuoteNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            PriceRequirementTechnicalProposalFailure.ProjectNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            PriceRequirementTechnicalProposalFailure.InactiveProject =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede estimar pricing de un proyecto inactivo."),
            PriceRequirementTechnicalProposalFailure.ClientNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al proyecto."),
            PriceRequirementTechnicalProposalFailure.InactiveClient =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede estimar pricing de un cliente inactivo."),
            PriceRequirementTechnicalProposalFailure.TechnicalProposalNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "El requerimiento todavia no tiene una propuesta tecnica vigente."),
            PriceRequirementTechnicalProposalFailure.TechnicalProposalNotConfirmed =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.TechnicalProposalNotConfirmed,
                    "Propuesta tecnica no lista para pricing",
                    "Confirma la propuesta y resuelve definiciones bloqueantes antes de calcular el precio. Consulta readiness.pendingDefinitions en la propuesta tecnica."),
            PriceRequirementTechnicalProposalFailure.TechnicalProposalNoIncludedItems =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.TechnicalProposalNoIncludedItems,
                    "Propuesta tecnica sin items incluidos",
                    "No hay elementos incluidos en la propuesta tecnica para calcular pricing."),
            PriceRequirementTechnicalProposalFailure.FunctionalTypeMismatch =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.TechnicalProposalFunctionalTypeMismatch,
                    "Sistema incompatible",
                    "Hay un sistema seleccionado que no pertenece a la funcion del requerimiento."),
            PriceRequirementTechnicalProposalFailure.Cancelled =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.PricingCancelled,
                    "Pricing cancelado",
                    "El pricing del requerimiento fue cancelado."),
            _ => RequirementProblem(StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de consulta",
                "No fue posible estimar el pricing del requerimiento.")
        };

    private static RequirementPricingResponse Map(
        RequirementTechnicalProposalPricingReadModel pricing) =>
        new(
            pricing.RequirementId,
            pricing.TechnicalProposalId,
            pricing.Currency,
            pricing.PricingBasis,
            pricing.ItemCount,
            pricing.PricedItemCount,
            pricing.NotPriceableItemCount,
            pricing.ItemsRequiringReview,
            Map(pricing.EstimatedSubtotal),
            pricing.IsCompleteTotal,
            pricing.RequiresReview,
            pricing.Assumptions,
            pricing.MissingData,
            pricing.Items.Select(Map).ToArray(),
            pricing.OriginalGrandTotal,
            pricing.CurrentGrandTotal,
            pricing.DeltaGrandTotal);

    private static RequirementPricingItemResponse Map(
        TechnicalProposalPricingItemReadModel item) =>
        new(
            item.ProposalItemId,
            item.ExtractedItemId,
            item.Source,
            item.ElementId,
            item.Sequence,
            item.Reference,
            item.Description,
            item.Status,
            item.ConfigurationSource,
            item.Quantity,
            item.PricingAreaM2,
            Map(item.Unit),
            Map(item.Line),
            item.ConfidenceScore,
            item.ConfidenceLevel,
            item.RequiresReview,
            item.MappingWarnings,
            item.Assumptions,
            item.MissingData,
            item.Comparables.Select(comparable => new RequirementPricingComparableResponse(
                comparable.CandidateId,
                comparable.HistoricalReference,
                comparable.PublicUnitPrice,
                comparable.ProjectedPrice,
                comparable.BackendScore,
                comparable.Ai2Similarity,
                comparable.SimilarityLevel,
                comparable.FinalWeight,
                comparable.MatchingTier,
                comparable.MatchedSystem,
                comparable.MatchedGlass,
                comparable.MatchedFinish,
                comparable.MatchedCommercialLine,
                comparable.FallbackReasons)).ToArray(),
            item.OriginalUnit is null ? null : Map(item.OriginalUnit),
            item.CurrentUnit is null ? null : Map(item.CurrentUnit),
            item.DeltaUnit is null ? null : Map(item.DeltaUnit),
            item.OriginalLine is null ? null : Map(item.OriginalLine),
            item.CurrentLine is null ? null : Map(item.CurrentLine),
            item.DeltaLine is null ? null : Map(item.DeltaLine),
            item.PriceSource,
            item.RepriceAttemptState,
            item.RepriceAttemptReason);

    private static RepriceRequirementPricingItemResponse Map(
        RepriceRequirementTechnicalProposalItemReadModel pricing) =>
        new(
            pricing.RequirementId,
            pricing.TechnicalProposalId,
            pricing.TechnicalProposalItemId,
            new RequirementPricingItemConfigurationResponse(
                pricing.SystemId,
                pricing.GlassTypeId,
                pricing.FinishTypeId),
            new RepriceRequirementPricingItemPriceResponse(
                pricing.Item.OriginalUnit?.Expected,
                pricing.Item.CurrentUnit?.Expected,
                pricing.Item.DeltaUnit?.Expected,
                pricing.Item.OriginalLine?.Expected,
                pricing.Item.CurrentLine?.Expected,
                pricing.Item.DeltaLine?.Expected,
                pricing.Item.OriginalUnit is null ? null : Map(pricing.Item.OriginalUnit),
                pricing.Item.CurrentUnit is null ? null : Map(pricing.Item.CurrentUnit),
                pricing.Item.DeltaUnit is null ? null : Map(pricing.Item.DeltaUnit),
                pricing.Item.OriginalLine is null ? null : Map(pricing.Item.OriginalLine),
                pricing.Item.CurrentLine is null ? null : Map(pricing.Item.CurrentLine),
                pricing.Item.DeltaLine is null ? null : Map(pricing.Item.DeltaLine),
                pricing.Item.Status,
                pricing.Item.PriceSource,
                pricing.Item.RepriceAttemptState,
                pricing.Item.RepriceAttemptReason),
            new RepriceRequirementPricingSummaryResponse(
                pricing.OriginalGrandTotal,
                pricing.CurrentGrandTotal,
                pricing.DeltaGrandTotal),
            pricing.Item.Comparables.Select(comparable =>
                new RequirementPricingComparableResponse(
                    comparable.CandidateId,
                    comparable.HistoricalReference,
                    comparable.PublicUnitPrice,
                    comparable.ProjectedPrice,
                    comparable.BackendScore,
                    comparable.Ai2Similarity,
                    comparable.SimilarityLevel,
                    comparable.FinalWeight,
                    comparable.MatchingTier,
                    comparable.MatchedSystem,
                    comparable.MatchedGlass,
                    comparable.MatchedFinish,
                    comparable.MatchedCommercialLine,
                    comparable.FallbackReasons)).ToArray());

    private static RequirementPricingRangeResponse Map(
        TechnicalProposalPricingMoneyRange range) =>
        new(range.Minimum, range.Expected, range.Maximum);

    private ObjectResult RequirementProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) =>
        ApiProblemDetailsFactory.Create(HttpContext, statusCode, errorCode, title, detail);

    private IActionResult MapFailure(
        RepriceRequirementTechnicalProposalItemFailure failure) =>
        failure switch
        {
            RepriceRequirementTechnicalProposalItemFailure.InvalidRequest =>
                RequirementProblem(StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El item o la configuracion indicada no son validos."),
            RepriceRequirementTechnicalProposalItemFailure.Unauthorized =>
                RequirementProblem(StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            RepriceRequirementTechnicalProposalItemFailure.InactiveUser =>
                RequirementProblem(StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            RepriceRequirementTechnicalProposalItemFailure.RequirementNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            RepriceRequirementTechnicalProposalItemFailure.TechnicalProposalItemNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Item tecnico no encontrado",
                    "El item indicado no pertenece a la propuesta tecnica vigente."),
            RepriceRequirementTechnicalProposalItemFailure.TechnicalProposalNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "El requerimiento todavia no tiene una propuesta tecnica vigente."),
            RepriceRequirementTechnicalProposalItemFailure.TechnicalProposalNotConfirmed =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.TechnicalProposalNotConfirmed,
                    "Propuesta tecnica no lista para pricing",
                    "Confirma la propuesta tecnica antes de repricing."),
            RepriceRequirementTechnicalProposalItemFailure.TechnicalProposalItemExcluded =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.TechnicalProposalItemExcluded,
                    "Item excluido",
                    "El item indicado esta excluido del alcance comercial actual."),
            RepriceRequirementTechnicalProposalItemFailure.InvalidSystemSelection =>
                RequirementProblem(StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Sistema invalido",
                    "El sistema seleccionado no existe o no es seleccionable."),
            RepriceRequirementTechnicalProposalItemFailure.FunctionalTypeMismatch =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.TechnicalProposalFunctionalTypeMismatch,
                    "Sistema incompatible",
                    "El sistema seleccionado no pertenece a la funcion del requerimiento."),
            RepriceRequirementTechnicalProposalItemFailure.InvalidGlassSelection =>
                RequirementProblem(StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Cristal invalido",
                    "El cristal seleccionado no existe o no es seleccionable."),
            RepriceRequirementTechnicalProposalItemFailure.InvalidFinishSelection =>
                RequirementProblem(StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Acabado invalido",
                    "El acabado seleccionado no existe o no es seleccionable."),
            _ => RequirementProblem(StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de repricing",
                "No fue posible recalcular el item indicado.")
        };
}
