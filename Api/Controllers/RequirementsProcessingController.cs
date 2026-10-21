using Api.ErrorHandling;
using Application.PreQuotes.ProcessRequirement;
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
[Route("api/v2/requirements/{requirementId}/process")]
public sealed class RequirementsProcessingController(
    ProcessRequirementService processRequirementService,
    CancelRequirementProcessingAttemptService cancelService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(
        typeof(ProcessRequirementResponse),
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
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status504GatewayTimeout)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Process(
        [FromRoute] Guid requirementId,
        CancellationToken cancellationToken)
    {
        var result = await processRequirementService.ExecuteAsync(
            new ProcessRequirementCommand(requirementId),
            cancellationToken);

        if (result.IsSuccess && result.Attempt is { } attempt)
        {
            return Ok(MapAttempt(attempt));
        }

        return MapFailure(result.Failure);
    }

    [HttpPost("/api/v2/processing-attempts/{processingAttemptId:guid}/cancel")]
    [ProducesResponseType(
        typeof(ProcessRequirementResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid processingAttemptId,
        CancellationToken cancellationToken)
    {
        var result = await cancelService.ExecuteAsync(
            new CancelRequirementProcessingAttemptCommand(
                processingAttemptId),
            cancellationToken);

        if (result.IsSuccess && result.Attempt is { } attempt)
        {
            return Ok(MapAttempt(attempt));
        }

        return MapCancelFailure(result.Failure);
    }

    [HttpPost("cancel")]
    [ProducesResponseType(
        typeof(ProcessRequirementResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CancelActiveForRequirement(
        [FromRoute] Guid requirementId,
        CancellationToken cancellationToken)
    {
        var result = await cancelService.ExecuteAsync(
            new CancelRequirementProcessingByRequirementCommand(
                requirementId),
            cancellationToken);

        if (result.IsSuccess && result.Attempt is { } attempt)
        {
            return Ok(MapAttempt(attempt));
        }

        return MapCancelFailure(result.Failure);
    }

    private IActionResult MapFailure(ProcessRequirementFailure failure) =>
        ProcessRequirementProblemMapper.Create(HttpContext, failure);

    private IActionResult MapCancelFailure(
        CancelRequirementProcessingAttemptFailure failure)
    {
        return failure switch
        {
            CancelRequirementProcessingAttemptFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El intento de procesamiento indicado no es valido."),
            CancelRequirementProcessingAttemptFailure.InactiveUser =>
                ProcessRequirementProblemMapper.Create(
                    HttpContext, ProcessRequirementFailure.InactiveUser),
            CancelRequirementProcessingAttemptFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            CancelRequirementProcessingAttemptFailure
                .ProcessingAttemptNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.ProcessingAttemptNotFound,
                    "Intento no encontrado",
                    "No existe el intento de procesamiento indicado."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible cancelar el procesamiento del requerimiento.")
        };
    }

    private static ProcessRequirementResponse MapAttempt(
        ProcessedRequirementAttemptResult attempt)
    {
        return new ProcessRequirementResponse(
            attempt.RequirementId,
            attempt.ProcessingAttemptId,
            attempt.CorrelationId,
            attempt.ProcessingState.ToString(),
            attempt.Outcome.ToString(),
            attempt.ErrorCode,
            attempt.StartedAtUtc,
            attempt.CompletedAtUtc,
            attempt.Summary is null
                ? null
                : new ProcessRequirementSummaryResponse(
                    attempt.Summary.ItemCount,
                    attempt.Summary.ItemsRequiringReview,
                    attempt.Summary.IssueCount,
                    attempt.Summary.ConflictCount,
                    attempt.Summary.ProcessingMethod,
                    attempt.Summary.DurationMs));
    }

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
