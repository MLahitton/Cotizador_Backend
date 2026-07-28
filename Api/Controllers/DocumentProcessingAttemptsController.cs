using System.Text.Json;
using Application.PreQuotes;
using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Application.PreQuotes.GetDocumentProcessingAttempt;
using Contracts.PreQuotes;
using Domain.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequote-documents/{documentId:guid}/processing-attempts")]
public sealed class DocumentProcessingAttemptsController(
    CreateDocumentProcessingAttemptService createService,
    GetDocumentProcessingAttemptService getService)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(
        typeof(DocumentProcessingAttemptStatusResponse),
        StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ProblemDetails),
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

        return MapCreateFailure(result.Failure, documentId);
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
        CreateDocumentProcessingAttemptFailure failure,
        Guid documentId)
    {
        return failure switch
        {
            CreateDocumentProcessingAttemptFailure.InvalidRequest => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud invalida",
                detail: "El identificador del documento no es valido."),
            CreateDocumentProcessingAttemptFailure.Unauthorized => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail: "No fue posible identificar al usuario autenticado."),
            CreateDocumentProcessingAttemptFailure.InactiveUser => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Usuario inactivo",
                detail: "El usuario no tiene acceso para procesar documentos."),
            CreateDocumentProcessingAttemptFailure.DocumentNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Documento no encontrado",
                detail: "No existe el documento de precotizacion indicado."),
            CreateDocumentProcessingAttemptFailure.InactiveProject => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Proyecto inactivo",
                detail: "No se pueden procesar documentos de un proyecto inactivo."),
            CreateDocumentProcessingAttemptFailure.InactiveClient => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cliente inactivo",
                detail: "No se pueden procesar documentos para un cliente inactivo."),
            CreateDocumentProcessingAttemptFailure
                .DocumentProcessingAlreadyActive =>
                CreateActiveAttemptResponse(documentId),
            CreateDocumentProcessingAttemptFailure.QueryError => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error al consultar el documento",
                detail: "No fue posible consultar el documento y su contexto."),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error al crear el intento",
                detail: "No fue posible registrar el intento de procesamiento.")
        };
    }

    private static ObjectResult CreateActiveAttemptResponse(Guid documentId)
    {
        var problemDetails = new ProblemDetails
        {
            Title = "Procesamiento ya activo",
            Status = StatusCodes.Status409Conflict,
            Detail =
                "El documento ya tiene un intento de procesamiento activo."
        };

        problemDetails.Extensions["errorCode"] =
            "DOCUMENT_PROCESSING_ALREADY_ACTIVE";
        problemDetails.Extensions["documentId"] = documentId;

        return new ObjectResult(problemDetails)
        {
            StatusCode = StatusCodes.Status409Conflict
        };
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
