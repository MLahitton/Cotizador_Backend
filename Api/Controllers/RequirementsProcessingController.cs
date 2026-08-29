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

    private IActionResult MapFailure(ProcessRequirementFailure failure)
    {
        return failure switch
        {
            ProcessRequirementFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El requerimiento indicado no es valido."),
            ProcessRequirementFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            ProcessRequirementFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            ProcessRequirementFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            ProcessRequirementFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            ProcessRequirementFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "No existe el proyecto asociado al requerimiento."),
            ProcessRequirementFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se pueden procesar requerimientos en un proyecto inactivo."),
            ProcessRequirementFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "No existe el cliente asociado al requerimiento."),
            ProcessRequirementFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se pueden procesar requerimientos para un cliente inactivo."),
            ProcessRequirementFailure.AlreadyProcessing =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProcessingAlreadyActive,
                    "Requerimiento en procesamiento",
                    "El requerimiento ya tiene un procesamiento activo."),
            ProcessRequirementFailure.NoFiles =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.NoFiles,
                    "Requerimiento sin archivos",
                    "El requerimiento no tiene archivos asociados para procesar."),
            ProcessRequirementFailure.StorageError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.StorageError,
                    "Error de almacenamiento",
                    "No fue posible leer los archivos del requerimiento."),
            ProcessRequirementFailure.AiTimeout =>
                RequirementProblem(
                    StatusCodes.Status504GatewayTimeout,
                    RequirementErrorCodes.AiTimeout,
                    "Tiempo agotado en AI2",
                    "Cotizador_AI2 no respondio dentro del tiempo esperado."),
            ProcessRequirementFailure.AiServiceUnavailable =>
                RequirementProblem(
                    StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiServiceUnavailable,
                    "Servicio de extraccion no disponible",
                    "No fue posible contactar Cotizador_AI2."),
            ProcessRequirementFailure.AiRemoteRejected =>
                RequirementProblem(
                    StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiRemoteRejected,
                    "Solicitud rechazada por AI2",
                    "Cotizador_AI2 rechazo los archivos del requerimiento."),
            ProcessRequirementFailure.AiInvalidResponse =>
                RequirementProblem(
                    StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiInvalidResponse,
                    "Extraccion invalida",
                    "Cotizador_AI2 respondio con una extraccion no valida."),
            ProcessRequirementFailure.AiServiceError =>
                RequirementProblem(
                    StatusCodes.Status502BadGateway,
                    RequirementErrorCodes.AiServiceError,
                    "Error de AI2",
                    "Cotizador_AI2 no pudo completar la extraccion."),
            ProcessRequirementFailure.PersistenceError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de persistencia",
                    "No fue posible registrar el procesamiento del requerimiento."),
            ProcessRequirementFailure.Cancelled =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProcessingCancelled,
                    "Procesamiento cancelado",
                    "El procesamiento del requerimiento fue cancelado."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible registrar el procesamiento del requerimiento.")
        };
    }

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
