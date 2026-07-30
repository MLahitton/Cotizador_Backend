using Application.PreQuotes.GetStructuredDocumentExtraction;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequote-documents/{documentId:guid}/structured-extraction")]
public sealed class PreQuoteDocumentStructuredExtractionController(
    GetStructuredDocumentExtractionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<StructuredDocumentExtractionDetailsResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid documentId,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new GetStructuredDocumentExtractionQuery(documentId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.Failure switch
            {
                GetStructuredDocumentExtractionFailure.InvalidRequest =>
                    Problem(statusCode: 400, title: "Solicitud inválida",
                        detail: "El identificador del documento no es válido."),
                GetStructuredDocumentExtractionFailure.Unauthorized =>
                    Problem(statusCode: 401, title: "No autorizado",
                        detail: "No fue posible identificar al usuario autenticado."),
                GetStructuredDocumentExtractionFailure.InactiveUser =>
                    Problem(statusCode: 403, title: "Usuario inactivo",
                        detail: "El usuario no tiene acceso para consultar documentos."),
                GetStructuredDocumentExtractionFailure.NotFound =>
                    Problem(statusCode: 404, title: "Documento no encontrado",
                        detail: "No existe el documento de precotización indicado."),
                _ => Problem(statusCode: 500,
                    title: "Error al consultar la extracción estructurada",
                    detail: "No fue posible consultar la extracción estructurada del documento.")
            };
        }

        var details = result.Details!;
        return Ok(new StructuredDocumentExtractionDetailsResponse(
            new PreQuoteDocumentResponse(
                details.Document.DocumentId,
                details.Document.PreQuoteId,
                details.Document.OriginalFileName,
                details.Document.ContentType,
                details.Document.SizeBytes,
                details.Document.CreatedAtUtc),
            PreQuoteDocumentResponseMapper.Map(
                details.ProcessingAvailability),
            PreQuoteDocumentResponseMapper.Map(details.LatestAttempt),
            details.StructuredExtraction is null
                ? null
                : PreQuoteDocumentResponseMapper.Map(
                    details.StructuredExtraction)));
    }
}
