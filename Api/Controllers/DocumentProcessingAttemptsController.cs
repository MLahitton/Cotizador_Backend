using Application.PreQuotes.CreateDocumentProcessingAttempt;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequote-documents/{documentId:guid}/processing-attempts")]
public sealed class DocumentProcessingAttemptsController(
    CreateDocumentProcessingAttemptService service)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(
        typeof(CreateDocumentProcessingAttemptResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status502BadGateway)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status504GatewayTimeout)]
    public async Task<IActionResult> Create(
        [FromRoute] Guid documentId,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new CreateDocumentProcessingAttemptCommand(documentId),
            cancellationToken);

        if (result.IsSuccess && result.Attempt is not null)
        {
            return StatusCode(
                StatusCodes.Status201Created,
                new CreateDocumentProcessingAttemptResponse(
                    result.Attempt.Id,
                    result.Attempt.DocumentId,
                    result.Attempt.CorrelationId,
                    result.Attempt.Outcome.ToString(),
                    result.Attempt.ErrorCode,
                    result.Attempt.SchemaVersion,
                    result.Attempt.Classification?.ToString(),
                    result.Attempt.RequiresOcr,
                    result.Attempt.PageCount,
                    result.Attempt.WarningCount,
                    result.Attempt.ProcessingMethod,
                    result.Attempt.DurationMs,
                    result.Attempt.CreatedAtUtc,
                    result.Attempt.CompletedAtUtc));
        }

        if (result.IsProcessingFailure
            && result.Attempt is { } failedAttempt)
        {
            return CreateProcessingFailureResponse(failedAttempt);
        }

        return result.Failure switch
        {
            CreateDocumentProcessingAttemptFailure.InvalidRequest => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud inválida",
                detail: "El identificador del documento no es válido."),
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
                detail: "No existe el documento de precotización indicado."),
            CreateDocumentProcessingAttemptFailure.InactiveProject => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Proyecto inactivo",
                detail: "No se pueden procesar documentos de un proyecto inactivo."),
            CreateDocumentProcessingAttemptFailure.InactiveClient => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cliente inactivo",
                detail: "No se pueden procesar documentos para un cliente inactivo."),
            CreateDocumentProcessingAttemptFailure.QueryError => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error al consultar el documento",
                detail: "No fue posible consultar el documento y su contexto."),
            CreateDocumentProcessingAttemptFailure.InitialPersistenceError =>
                Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Error al crear el intento",
                    detail: "No fue posible registrar el intento de procesamiento."),
            CreateDocumentProcessingAttemptFailure.FinalPersistenceError =>
                Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Error al finalizar el intento",
                    detail: "No fue posible guardar el resultado del procesamiento."),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error al procesar el documento",
                detail: "No fue posible completar el procesamiento del documento.")
        };
    }

    private static ObjectResult CreateProcessingFailureResponse(
        CreatedDocumentProcessingAttemptResult attempt)
    {
        var errorCode = attempt.ErrorCode
            ?? throw new InvalidOperationException(
                "El intento fallido debe contener un código de error.");
        var (statusCode, detail) = MapProcessingFailure(errorCode);
        var problemDetails = new ProblemDetails
        {
            Title = "No fue posible procesar el documento",
            Status = statusCode,
            Detail = detail
        };

        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["documentId"] = attempt.DocumentId;
        problemDetails.Extensions["processingAttemptId"] = attempt.Id;
        problemDetails.Extensions["correlationId"] = attempt.CorrelationId;

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode
        };
    }

    private static (int StatusCode, string Detail) MapProcessingFailure(
        string errorCode)
    {
        return errorCode switch
        {
            "INVALID_REQUEST" => (
                StatusCodes.Status422UnprocessableEntity,
                "La solicitud enviada al servicio de procesamiento no fue válida."),
            "INVALID_CORRELATION_ID" => (
                StatusCodes.Status422UnprocessableEntity,
                "La correlación del procesamiento no fue válida."),
            "EMPTY_FILE" => (
                StatusCodes.Status422UnprocessableEntity,
                "El documento almacenado está vacío."),
            "INVALID_PDF" => (
                StatusCodes.Status422UnprocessableEntity,
                "El documento almacenado no es un PDF válido."),
            "PDF_PASSWORD_REQUIRED" => (
                StatusCodes.Status422UnprocessableEntity,
                "El documento PDF requiere una contraseña."),
            "PDF_PAGE_LIMIT_EXCEEDED" => (
                StatusCodes.Status422UnprocessableEntity,
                "El documento supera la cantidad máxima de páginas permitida."),
            "FILE_TOO_LARGE" => (
                StatusCodes.Status413PayloadTooLarge,
                "El documento PDF supera el tamaño máximo permitido."),
            "UNSUPPORTED_FILE_TYPE" => (
                StatusCodes.Status415UnsupportedMediaType,
                "El tipo de archivo no es compatible con el procesamiento."),
            "AI_INVALID_RESPONSE" => (
                StatusCodes.Status502BadGateway,
                "El servicio de procesamiento devolvió una respuesta inválida."),
            "AI_SERVICE_ERROR" => (
                StatusCodes.Status502BadGateway,
                "El servicio de procesamiento presentó un error."),
            "AI_SERVICE_UNAVAILABLE" => (
                StatusCodes.Status503ServiceUnavailable,
                "El servicio de procesamiento no está disponible."),
            "AI_SERVICE_TIMEOUT" => (
                StatusCodes.Status504GatewayTimeout,
                "El servicio de procesamiento tardó demasiado en responder."),
            "DOCUMENT_STORAGE_ERROR" => (
                StatusCodes.Status500InternalServerError,
                "No fue posible leer el documento almacenado."),
            _ => (
                StatusCodes.Status500InternalServerError,
                "No fue posible completar el procesamiento del documento.")
        };
    }
}
