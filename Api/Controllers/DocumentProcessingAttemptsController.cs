using System.Text.Json;
using Api.ErrorHandling;
using Application.PreQuotes;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Application.PreQuotes.GetDocumentProcessingAttempt;
using Contracts.Common;
using Contracts.PreQuotes;
using Domain.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequote-documents/{documentId}/processing-attempts")]
public sealed class DocumentProcessingAttemptsController(
    CreateDocumentProcessingAttemptService createService,
    GetDocumentProcessingAttemptService getService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(
        typeof(DocumentProcessingAttemptStatusResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create(
        [FromRoute] Guid documentId,
        CancellationToken cancellationToken)
    {
        var result = await createService.ExecuteAsync(
            new CreateDocumentProcessingAttemptCommand(documentId),
            cancellationToken);

        if (result.IsSuccess && result.Attempt is { } attempt)
        {
            var response = MapStatus(attempt);

            return AcceptedAtAction(
                nameof(GetById),
                new
                {
                    documentId,
                    processingAttemptId = attempt.ProcessingAttemptId
                },
                response);
        }

        return MapCreateFailure(result.Failure);
    }

    [HttpGet("{processingAttemptId:guid}")]
    [ProducesResponseType(
        typeof(DocumentProcessingAttemptStatusResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetById(
        [FromRoute] Guid documentId,
        [FromRoute] Guid processingAttemptId,
        CancellationToken cancellationToken)
    {
        var result = await getService.ExecuteAsync(
            documentId,
            processingAttemptId,
            cancellationToken);

        if (result.IsSuccess && result.Attempt is { } attempt)
        {
            return Ok(MapStatus(attempt));
        }

        return result.Failure switch
        {
            GetDocumentProcessingAttemptFailure.InvalidRequest => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud invalida",
                detail: "Los identificadores indicados no son validos."),
            GetDocumentProcessingAttemptFailure.Unauthorized => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail: "No fue posible identificar al usuario autenticado."),
            GetDocumentProcessingAttemptFailure.InactiveUser => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Usuario inactivo",
                detail: "El usuario no tiene acceso para consultar procesamientos."),
            GetDocumentProcessingAttemptFailure.NotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Intento no encontrado",
                detail: "No existe un intento de procesamiento accesible con el identificador indicado."),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error al consultar el intento",
                detail: "No fue posible consultar el estado del procesamiento.")
        };
    }

    private IActionResult MapCreateFailure(
        CreateDocumentProcessingAttemptFailure failure)
    {
        return failure switch
        {
            CreateDocumentProcessingAttemptFailure.InvalidRequest =>
                CreateProblem(
                    StatusCodes.Status400BadRequest,
                    DocumentProcessingErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El identificador del documento no es valido."),
            CreateDocumentProcessingAttemptFailure.Unauthorized =>
                CreateProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            CreateDocumentProcessingAttemptFailure.InactiveUser =>
                CreateProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario no tiene acceso para procesar documentos."),
            CreateDocumentProcessingAttemptFailure.DocumentNotFound =>
                CreateProblem(
                    StatusCodes.Status404NotFound,
                    DocumentProcessingErrorCodes.DocumentNotFound,
                    "Documento no encontrado",
                    "No existe un documento de precotizacion accesible con el identificador indicado."),
            CreateDocumentProcessingAttemptFailure.InactiveProject =>
                CreateProblem(
                    StatusCodes.Status409Conflict,
                    DocumentProcessingErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se pueden procesar documentos de un proyecto inactivo."),
            CreateDocumentProcessingAttemptFailure.InactiveClient =>
                CreateProblem(
                    StatusCodes.Status409Conflict,
                    DocumentProcessingErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se pueden procesar documentos para un cliente inactivo."),
            CreateDocumentProcessingAttemptFailure
                .DocumentProcessingAlreadyActive =>
                CreateProblem(
                    StatusCodes.Status409Conflict,
                    DocumentProcessingErrorCodes.AlreadyActive,
                    "Procesamiento ya activo",
                    "El documento ya tiene un intento de procesamiento activo."),
            CreateDocumentProcessingAttemptFailure.QueryError =>
                CreateProblem(
                    StatusCodes.Status500InternalServerError,
                    DocumentProcessingErrorCodes.QueryError,
                    "Error al consultar el documento",
                    "No fue posible consultar el documento y su contexto."),
            _ => CreateProblem(
                StatusCodes.Status500InternalServerError,
                DocumentProcessingErrorCodes.PersistenceError,
                "Error al crear el intento",
                "No fue posible registrar el intento de procesamiento.")
        };
    }

    private ObjectResult CreateProblem(
        int status,
        string errorCode,
        string title,
        string detail)
    {
        return ApiProblemDetailsFactory.Create(
            HttpContext,
            status,
            errorCode,
            title,
            detail);
    }

    private static DocumentProcessingAttemptStatusResponse MapStatus(
        DocumentProcessingAttemptStatusData attempt)
    {
        JsonElement? result = null;

        if (attempt.ResultPayloadJson is { } payloadJson)
        {
            using var document = JsonDocument.Parse(payloadJson);
            result = document.RootElement.Clone();
        }

        return new DocumentProcessingAttemptStatusResponse(
            attempt.ProcessingAttemptId,
            attempt.DocumentId,
            MapProcessingState(attempt.ProcessingState),
            attempt.Outcome is { } outcome ? MapOutcome(outcome) : null,
            attempt.ErrorCode,
            attempt.CreatedAtUtc,
            attempt.StartedAtUtc,
            attempt.CompletedAtUtc,
            result);
    }

    private static string MapProcessingState(
        DocumentProcessingState processingState)
    {
        return processingState switch
        {
            DocumentProcessingState.Pending => "PENDING",
            DocumentProcessingState.Processing => "PROCESSING",
            DocumentProcessingState.Finished => "FINISHED",
            _ => throw new InvalidOperationException(
                "El estado de procesamiento no es valido.")
        };
    }

    private static string MapOutcome(DocumentProcessingOutcome outcome)
    {
        return outcome switch
        {
            DocumentProcessingOutcome.Completed => "COMPLETED",
            DocumentProcessingOutcome.RequiresReview => "REQUIRES_REVIEW",
            DocumentProcessingOutcome.Failed => "FAILED",
            _ => throw new InvalidOperationException(
                "El resultado de procesamiento no es valido.")
        };
    }
}
