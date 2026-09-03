using Api.ErrorHandling;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Application.PreQuotes.TechnicalProposalReadiness;
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
[Route("api/v2/requirements/{requirementId}/technical-proposal")]
public sealed class RequirementTechnicalProposalController(
    GetRequirementTechnicalProposalService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(
        typeof(RequirementTechnicalProposalResponse),
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
    public async Task<IActionResult> Get(
        [FromRoute] Guid requirementId,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(requirementId),
            cancellationToken);

        if (result.IsSuccess && result.Proposal is { } proposal)
        {
            return Ok(Map(proposal));
        }

        return MapFailure(result.Failure);
    }

    private IActionResult MapFailure(
        GetRequirementTechnicalProposalFailure failure)
    {
        return failure switch
        {
            GetRequirementTechnicalProposalFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El requerimiento indicado no es valido."),
            GetRequirementTechnicalProposalFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            GetRequirementTechnicalProposalFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            GetRequirementTechnicalProposalFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            GetRequirementTechnicalProposalFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            GetRequirementTechnicalProposalFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            GetRequirementTechnicalProposalFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede consultar la propuesta tecnica de un proyecto inactivo."),
            GetRequirementTechnicalProposalFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            GetRequirementTechnicalProposalFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede consultar la propuesta tecnica de un cliente inactivo."),
            GetRequirementTechnicalProposalFailure.TechnicalProposalNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "El requerimiento todavia no tiene una propuesta tecnica vigente."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de consulta",
                "No fue posible consultar la propuesta tecnica.")
        };
    }

    private static RequirementTechnicalProposalResponse Map(
        RequirementTechnicalProposalReadModel proposal) =>
        new(
            proposal.RequirementId,
            proposal.TechnicalProposalId,
            proposal.ProcessingAttemptId,
            proposal.ExtractionResultId,
            proposal.Status,
            proposal.CommercialLine,
            new RequirementTechnicalProposalCommercialConfirmationResponse(
                proposal.CommercialConfirmation.State,
                proposal.CommercialConfirmation.ConfirmedAtUtc,
                proposal.CommercialConfirmation.ConfirmedByUserId),
            proposal.CreatedAtUtc,
            proposal.ItemCount,
            proposal.ItemsRequiringReview,
            proposal.TechnicallyCompleteItems,
            proposal.PriceableItems,
            Map(proposal.Readiness),
            proposal.Items.Select(MapItem).ToArray());

    private static RequirementTechnicalProposalItemResponse MapItem(
        RequirementTechnicalProposalItemReadModel item) =>
        new(
            item.ItemId,
            item.ExtractedItemId,
            item.ElementId,
            item.Sequence,
            item.Reference,
            item.Description,
            item.ElementType,
            item.Quantity,
            item.WidthMm,
            item.HeightMm,
            item.ManualQuantityOverride,
            item.ManualWidthMmOverride,
            item.ManualHeightMmOverride,
            item.EffectiveQuantity,
            item.EffectiveWidthMm,
            item.EffectiveHeightMm,
            item.AreaM2,
            item.IsIncluded,
            item.ExcludedAtUtc,
            item.ExcludedByUserId,
            item.ExclusionReason,
            item.ExtractionConfidence,
            item.ExtractionStatus,
            new RequirementTechnicalProposalSuggestedResponse(
                Map(item.Suggested.System),
                Map(item.Suggested.Glass),
                Map(item.Suggested.Finish)),
            item.Selected is null
                ? null
                : new RequirementTechnicalProposalSelectedResponse(
                    Map(item.Selected.System),
                    Map(item.Selected.Glass),
                    Map(item.Selected.Finish),
                    item.Selected.SelectedAtUtc,
                    item.Selected.SelectedByUserId),
            item.SelectionState,
            new RequirementTechnicalProposalAlternativesResponse(
                item.Alternatives.Systems
                    .Select(alternative => new
                        RequirementTechnicalProposalSystemAlternativeResponse(
                            MapRequired(alternative.Option),
                            alternative.Rank,
                            alternative.Confidence,
                            alternative.Reasons))
                    .ToArray(),
                item.Alternatives.Glass
                    .Select(alternative => new
                        RequirementTechnicalProposalGlassAlternativeResponse(
                            MapRequired(alternative.Option),
                            alternative.Rank,
                            alternative.Confidence,
                            alternative.Reasons))
                    .ToArray(),
                item.Alternatives.Finishes
                    .Select(alternative => new
                        RequirementTechnicalProposalFinishAlternativeResponse(
                            MapRequired(alternative.Option),
                            alternative.Rank,
                            alternative.Confidence,
                            alternative.Reasons))
                    .ToArray()),
            new RequirementTechnicalProposalConfidenceResponse(
                item.Confidence.Overall,
                item.Confidence.System,
                item.Confidence.Glass,
                item.Confidence.Finish),
            item.RequiresReview,
            item.ReviewReasons,
            item.SystemResolutionReasons,
            item.GlassResolutionReasons,
            item.FinishResolutionReasons,
            item.IsTechnicallyComplete,
            item.IsPriceable,
            Map(item.Readiness),
            new RequirementTechnicalProposalHistoricalEvidenceResponse(
                item.HistoricalEvidence.Status,
                item.HistoricalEvidence.SupportCount,
                item.HistoricalEvidence.BestSimilarity,
                item.HistoricalEvidence.AverageSimilarity,
                item.HistoricalEvidence.Examples.Select(example => new
                    RequirementTechnicalProposalHistoricalExampleResponse(
                        example.CandidateId,
                        example.QuoteId,
                        example.HistoricalReference,
                        example.SimilarityScore,
                        example.MatchedFeatures,
                        example.Differences,
                        example.TechnicalExplanation)).ToArray()),
            new RequirementTechnicalProposalTraceResponse(
                item.Trace.RequestedSystemRaw,
                item.Trace.RequestedProfileRaw,
                item.Trace.FunctionalType,
                item.Trace.Operation,
                item.Trace.GlassRawSpecification,
                item.Trace.GlassTypeRaw,
                item.Trace.GlassTypeNormalized,
                item.Trace.GlassThicknessMm,
                item.Trace.FinishRawDescription,
                item.Trace.FinishNormalizedType,
                item.Trace.FinishColorRaw,
                item.Trace.FinishColorNormalized,
                item.Trace.SpecialFeatures,
                item.Trace.GeometryType),
            item.Evidence.Select(evidence => new
                RequirementTechnicalProposalEvidenceResponse(
                    evidence.PageNumber,
                    evidence.SourceType,
                    evidence.Text,
                    evidence.SheetName,
                    evidence.CellRange,
                    evidence.SourceId,
                    evidence.SourceFileName,
                    evidence.ContextLabel,
                    evidence.Confidence,
                    evidence.Status)).ToArray());

    private static RequirementTechnicalProposalReadinessResponse Map(
        RequirementTechnicalProposalReadinessReadModel readiness) =>
        new(
            readiness.State,
            readiness.IsReadyForConfirmation,
            readiness.IsReadyForPricing,
            readiness.BlockingItems,
            readiness.WarningItems,
            readiness.BlockingDefinitions,
            readiness.WarningDefinitions,
            readiness.PricingBlockingItems,
            readiness.PricingBlockingDefinitions,
            readiness.Categories);

    private static RequirementTechnicalProposalItemReadinessResponse Map(
        RequirementTechnicalProposalItemReadinessReadModel readiness) =>
        new(
            readiness.State,
            readiness.BlockingCount,
            readiness.WarningCount,
            readiness.PendingDefinitions.Select(Map).ToArray());

    private static TechnicalProposalPendingDefinitionResponse Map(
        TechnicalProposalPendingDefinitionReadModel definition) =>
        new(
            definition.Code,
            definition.Category,
            definition.Severity,
            definition.Field,
            definition.Title,
            definition.Message,
            definition.CurrentValue,
            definition.RequiredAction,
            definition.BlocksConfirmation,
            definition.BlocksPricing,
            definition.RelatedReasonCodes);

    private static RequirementTechnicalProposalSystemOptionResponse? Map(
        RequirementTechnicalProposalSystemOptionReadModel? option) =>
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

    private static RequirementTechnicalProposalSystemOptionResponse MapRequired(
        RequirementTechnicalProposalSystemOptionReadModel option) =>
        Map((RequirementTechnicalProposalSystemOptionReadModel?)option)!;

    private static RequirementTechnicalProposalGlassOptionResponse? Map(
        RequirementTechnicalProposalGlassOptionReadModel? option) =>
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

    private static RequirementTechnicalProposalGlassOptionResponse MapRequired(
        RequirementTechnicalProposalGlassOptionReadModel option) =>
        Map((RequirementTechnicalProposalGlassOptionReadModel?)option)!;

    private static RequirementTechnicalProposalFinishOptionResponse? Map(
        RequirementTechnicalProposalFinishOptionReadModel? option) =>
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

    private static RequirementTechnicalProposalFinishOptionResponse MapRequired(
        RequirementTechnicalProposalFinishOptionReadModel option) =>
        Map((RequirementTechnicalProposalFinishOptionReadModel?)option)!;

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
