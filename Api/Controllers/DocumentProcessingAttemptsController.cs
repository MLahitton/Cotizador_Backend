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
        StatusCodes.Status500InternalServerError)]
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
}
