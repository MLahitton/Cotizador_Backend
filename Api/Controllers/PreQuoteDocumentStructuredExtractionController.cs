using Application.PreQuotes.GetStructuredDocumentExtraction;
using Api.ErrorHandling;
using Contracts.Common;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequote-documents/{documentId}/structured-extraction")]
public sealed class PreQuoteDocumentStructuredExtractionController(
    GetStructuredDocumentExtractionService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<StructuredDocumentExtractionDetailsResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse),
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
                    StructuredExtractionProblem(
                        statusCode: StatusCodes.Status400BadRequest,
                        errorCode: StructuredExtractionErrorCodes.InvalidRequest,
                        title: "Solicitud invalida",
                        detail: "El identificador del documento no es valido."),
                GetStructuredDocumentExtractionFailure.Unauthorized =>
                    StructuredExtractionProblem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        errorCode: PreQuoteErrorCodes.Unauthorized,
                        title: "No autorizado",
                        detail: "No fue posible identificar al usuario autenticado."),
                GetStructuredDocumentExtractionFailure.InactiveUser =>
                    StructuredExtractionProblem(
                        statusCode: StatusCodes.Status403Forbidden,
                        errorCode: PreQuoteErrorCodes.InactiveUser,
                        title: "Usuario inactivo",
                        detail: "El usuario no tiene acceso para consultar documentos."),
                GetStructuredDocumentExtractionFailure.NotFound =>
                    StructuredExtractionProblem(
                        statusCode: StatusCodes.Status404NotFound,
                        errorCode: DocumentProcessingAttemptErrorCodes.DocumentNotFound,
                        title: "Documento no encontrado",
                        detail: "No existe el documento de precotizacion indicado."),
                _ => StructuredExtractionProblem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    errorCode: StructuredExtractionErrorCodes.QueryError,
                    title: "Error al consultar la extraccion estructurada",
                    detail: "No fue posible consultar la extraccion estructurada del documento.")
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

    private ObjectResult StructuredExtractionProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) => ApiProblemDetailsFactory.Create(
            HttpContext,
            statusCode,
            errorCode,
            title,
            detail);
}
