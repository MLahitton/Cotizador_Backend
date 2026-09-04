using Api.ErrorHandling;
using Application.PreQuotes.RequirementChat;
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
public sealed class RequirementChatController(
    GetRequirementChatService getService,
    SendRequirementChatMessageService sendService) : ControllerBase
{
    [HttpGet("api/v2/requirements/{requirementId:guid}/chat")]
    [ProducesResponseType(typeof(RequirementChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetRequirementChat(
        [FromRoute] Guid requirementId,
        CancellationToken cancellationToken)
    {
        var result = await getService.ExecuteAsync(
            new GetRequirementChatCommand(requirementId, null),
            cancellationToken);
        return result.IsSuccess && result.Thread is { } thread
            ? Ok(Map(thread))
            : MapFailure(result.Failure);
    }

    [HttpPost("api/v2/requirements/{requirementId:guid}/chat/messages")]
    [ProducesResponseType(typeof(RequirementChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendRequirementMessage(
        [FromRoute] Guid requirementId,
        [FromBody] SendRequirementChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sendService.ExecuteAsync(
            new SendRequirementChatMessageCommand(
                requirementId,
                null,
                request.Message),
            cancellationToken);
        return result.IsSuccess && result.Thread is { } thread
            ? Ok(Map(thread, result.LastInteraction))
            : MapFailure(result.Failure);
    }

    [HttpGet("api/v2/requirements/{requirementId:guid}/items/{technicalProposalItemId:guid}/chat")]
    [ProducesResponseType(typeof(RequirementChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetItemChat(
        [FromRoute] Guid requirementId,
        [FromRoute] Guid technicalProposalItemId,
        CancellationToken cancellationToken)
    {
        var result = await getService.ExecuteAsync(
            new GetRequirementChatCommand(
                requirementId,
                technicalProposalItemId),
            cancellationToken);
        return result.IsSuccess && result.Thread is { } thread
            ? Ok(Map(thread))
            : MapFailure(result.Failure);
    }

    [HttpPost("api/v2/requirements/{requirementId:guid}/items/{technicalProposalItemId:guid}/chat/messages")]
    [ProducesResponseType(typeof(RequirementChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SendItemMessage(
        [FromRoute] Guid requirementId,
        [FromRoute] Guid technicalProposalItemId,
        [FromBody] SendRequirementChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await sendService.ExecuteAsync(
            new SendRequirementChatMessageCommand(
                requirementId,
                technicalProposalItemId,
                request.Message),
            cancellationToken);
        return result.IsSuccess && result.Thread is { } thread
            ? Ok(Map(thread, result.LastInteraction))
            : MapFailure(result.Failure);
    }

    private IActionResult MapFailure(RequirementChatFailure failure) =>
        failure switch
        {
            RequirementChatFailure.InvalidRequest =>
                RequirementProblem(StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El requerimiento o item indicado no es valido."),
            RequirementChatFailure.InvalidMessage =>
                RequirementProblem(StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Mensaje invalido",
                    "El mensaje debe tener entre 1 y 4000 caracteres."),
            RequirementChatFailure.Unauthorized =>
                RequirementProblem(StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            RequirementChatFailure.InactiveUser =>
                RequirementProblem(StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            RequirementChatFailure.RequirementNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            RequirementChatFailure.ItemNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Item no encontrado",
                    "El item indicado no pertenece al requerimiento."),
            RequirementChatFailure.TechnicalProposalNotFound =>
                RequirementProblem(StatusCodes.Status404NotFound,
                    RequirementErrorCodes.TechnicalProposalNotFound,
                    "Propuesta tecnica no encontrada",
                    "El requerimiento todavia no tiene una propuesta tecnica vigente."),
            RequirementChatFailure.Ai2Unavailable =>
                RequirementProblem(StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiServiceUnavailable,
                    "Asistente no disponible",
                    "No fue posible obtener respuesta desde AI2."),
            RequirementChatFailure.InactiveProject =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se puede consultar el chat de un proyecto inactivo."),
            RequirementChatFailure.InactiveClient =>
                RequirementProblem(StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se puede consultar el chat de un cliente inactivo."),
            _ => RequirementProblem(StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de chat",
                "No fue posible consultar el chat del requerimiento.")
        };

    private static RequirementChatResponse Map(
        RequirementChatThreadReadModel thread,
        RequirementChatInteractionReadModel? interaction = null) =>
        new(
            thread.ThreadId,
            thread.RequirementId,
            thread.TechnicalProposalItemId,
            thread.Scope,
            thread.CreatedAtUtc,
            thread.UpdatedAtUtc,
            thread.Messages.Select(message => new RequirementChatMessageResponse(
                message.MessageId,
                message.Role,
                message.Content,
                message.Sequence,
                message.CreatedAtUtc)).ToArray(),
            interaction is null
                ? null
                : new RequirementChatInteractionResponse(
                    interaction.MessageType,
                    interaction.PlanId,
                    interaction.RequiresConfirmation,
                    interaction.ActionType,
                    new RequirementChatActionTargetResponse(
                        interaction.TargetTechnicalProposalItemId,
                        interaction.TargetReference),
                    interaction.CurrentValue,
                    interaction.RequestedValue,
                    interaction.PricingImpactExpected,
                    interaction.PricingStatus,
                    interaction.Reasons));

    private ObjectResult RequirementProblem(
        int statusCode,
        string code,
        string title,
        string detail)
    {
        var result = Problem(
            statusCode: statusCode,
            title: title,
            detail: detail);
        result.Value ??= new ProblemDetails();
        if (result.Value is ProblemDetails problem)
        {
            problem.Extensions["code"] = code;
        }

        return result;
    }
}
