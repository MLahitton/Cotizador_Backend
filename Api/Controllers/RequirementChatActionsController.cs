using Api.ErrorHandling;
using Application.PreQuotes.RequirementChatActions;
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
public sealed class RequirementChatActionsController(
    PlanRequirementChatActionService planService,
    ConfirmRequirementChatActionService confirmService) : ControllerBase
{
    [HttpPost("api/v2/requirements/{requirementId:guid}/chat/actions/plan")]
    [ProducesResponseType(typeof(ChatActionPlanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PlanRequirementAction(
        [FromRoute] Guid requirementId,
        [FromBody] PlanRequirementChatActionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await planService.ExecuteAsync(
            new PlanRequirementChatActionCommand(
                requirementId,
                null,
                null,
                null,
                request.Scope,
                request.ActionType,
                request.TargetTechnicalProposalItemId,
                request.TargetReference,
                request.RequestedValue,
                request.Quantity,
                request.WidthMm,
                request.HeightMm,
                request.RawUserMessage),
            cancellationToken);
        return result.IsSuccess && result.Plan is { } plan
            ? Ok(Map(plan))
            : MapFailure(result.Failure);
    }

    [HttpPost("api/v2/requirements/{requirementId:guid}/items/{technicalProposalItemId:guid}/chat/actions/plan")]
    [ProducesResponseType(typeof(ChatActionPlanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> PlanItemAction(
        [FromRoute] Guid requirementId,
        [FromRoute] Guid technicalProposalItemId,
        [FromBody] PlanRequirementChatActionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await planService.ExecuteAsync(
            new PlanRequirementChatActionCommand(
                requirementId,
                null,
                null,
                technicalProposalItemId,
                "ITEM",
                request.ActionType,
                request.TargetTechnicalProposalItemId,
                request.TargetReference,
                request.RequestedValue,
                request.Quantity,
                request.WidthMm,
                request.HeightMm,
                request.RawUserMessage),
            cancellationToken);
        return result.IsSuccess && result.Plan is { } plan
            ? Ok(Map(plan))
            : MapFailure(result.Failure);
    }

    [HttpPost("api/v2/requirements/{requirementId:guid}/chat/actions/{planId:guid}/confirm")]
    [ProducesResponseType(typeof(ChatActionPlanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Confirm(
        [FromRoute] Guid requirementId,
        [FromRoute] Guid planId,
        CancellationToken cancellationToken)
    {
        var result = await confirmService.ExecuteAsync(
            new ConfirmRequirementChatActionCommand(requirementId, planId),
            cancellationToken);
        return result.IsSuccess && result.Plan is { } plan
            ? Ok(Map(plan))
            : MapFailure(result.Failure);
    }

    private IActionResult MapFailure(RequirementChatActionFailure failure) =>
        failure switch
        {
            RequirementChatActionFailure.InvalidRequest => Problem(
                StatusCodes.Status400BadRequest,
                RequirementErrorCodes.InvalidRequest,
                "Solicitud invalida",
                "La accion conversacional indicada no es valida."),
            RequirementChatActionFailure.Unauthorized => Problem(
                StatusCodes.Status401Unauthorized,
                PreQuoteErrorCodes.Unauthorized,
                "No autorizado",
                "No fue posible identificar al usuario autenticado."),
            RequirementChatActionFailure.InactiveUser => Problem(
                StatusCodes.Status403Forbidden,
                PreQuoteErrorCodes.InactiveUser,
                "Usuario inactivo",
                "El usuario autenticado se encuentra inactivo."),
            RequirementChatActionFailure.RequirementNotFound => Problem(
                StatusCodes.Status404NotFound,
                RequirementErrorCodes.RequirementNotFound,
                "Requerimiento no encontrado",
                "No existe el requerimiento indicado."),
            RequirementChatActionFailure.TechnicalProposalNotFound => Problem(
                StatusCodes.Status404NotFound,
                RequirementErrorCodes.TechnicalProposalNotFound,
                "Propuesta tecnica no encontrada",
                "El requerimiento todavia no tiene una propuesta tecnica vigente."),
            RequirementChatActionFailure.PlanNotFound => Problem(
                StatusCodes.Status404NotFound,
                RequirementErrorCodes.InvalidRequest,
                "Plan no encontrado",
                "No existe el plan de accion indicado."),
            RequirementChatActionFailure.InactiveProject => Problem(
                StatusCodes.Status409Conflict,
                RequirementErrorCodes.ProjectInactive,
                "Proyecto inactivo",
                "No se puede modificar una propuesta de un proyecto inactivo."),
            RequirementChatActionFailure.InactiveClient => Problem(
                StatusCodes.Status409Conflict,
                RequirementErrorCodes.ClientInactive,
                "Cliente inactivo",
                "No se puede modificar una propuesta de un cliente inactivo."),
            _ => Problem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de accion conversacional",
                "No fue posible procesar la accion conversacional.")
        };

    private static ChatActionPlanResponse Map(ChatActionPlanReadModel plan) =>
        new(
            plan.PlanId,
            plan.RequirementId,
            plan.TechnicalProposalId,
            plan.Scope,
            plan.Status,
            plan.RequiresConfirmation,
            plan.CreatedAtUtc,
            plan.ExpiresAtUtc,
            plan.PricingStatus,
            plan.ExecutionReasons,
            plan.Actions.Select(action => new ChatActionPlanActionResponse(
                action.ActionId,
                action.ActionType,
                action.TargetTechnicalProposalItemId,
                action.TargetReference,
                action.RequestedValue,
                action.CurrentValue,
                action.ResolvedCatalogEntity is null
                    ? null
                    : new ChatActionResolvedCatalogEntityResponse(
                        action.ResolvedCatalogEntity.Id,
                        action.ResolvedCatalogEntity.Code,
                        action.ResolvedCatalogEntity.DisplayName,
                        action.ResolvedCatalogEntity.EntityType),
                action.ValidationState,
                action.ValidationReasons,
                action.RequiresConfirmation,
                action.AvailableOptions.Select(option => new ChatActionOptionResponse(
                    option.Id,
                    option.Code,
                    option.DisplayName,
                    option.OptionType)).ToArray())).ToArray());

    private ObjectResult Problem(int statusCode, string code, string title, string detail) =>
        ApiProblemDetailsFactory.Create(HttpContext, statusCode, code, title, detail);
}
