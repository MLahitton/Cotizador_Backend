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
            pricing.Items.Select(Map).ToArray());

    private static RequirementPricingItemResponse Map(
        TechnicalProposalPricingItemReadModel item) =>
        new(
            item.ProposalItemId,
            item.ExtractedItemId,
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
                comparable.FinalWeight)).ToArray());

    private static RequirementPricingRangeResponse Map(
        TechnicalProposalPricingMoneyRange range) =>
        new(range.Minimum, range.Expected, range.Maximum);

    private ObjectResult RequirementProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) =>
        ApiProblemDetailsFactory.Create(HttpContext, statusCode, errorCode, title, detail);
}
