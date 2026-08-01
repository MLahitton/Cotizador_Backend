using Application.PreQuotes.CreatePreQuoteDocument;
using Application.PreQuotes.GetPreQuoteDocuments;
using Contracts.PreQuotes;
using Contracts.Common;
using Api.ErrorHandling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequotes/{preQuoteId}/documents")]
public sealed class PreQuoteDocumentsController(
    CreatePreQuoteDocumentService createPreQuoteDocumentService,
    GetPreQuoteDocumentsService getPreQuoteDocumentsService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetPreQuoteDocumentsResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid preQuoteId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await getPreQuoteDocumentsService.ExecuteAsync(
            new GetPreQuoteDocumentsQuery(preQuoteId, page, pageSize),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return result.Failure switch
            {
                GetPreQuoteDocumentsFailure.InvalidRequest =>
                    Problem(statusCode: 400, title: "Solicitud inválida",
                        detail: "Los parámetros de consulta no son válidos."),
                GetPreQuoteDocumentsFailure.Unauthorized =>
                    Problem(statusCode: 401, title: "No autorizado",
                        detail: "No fue posible identificar al usuario autenticado."),
                GetPreQuoteDocumentsFailure.InactiveUser =>
                    Problem(statusCode: 403, title: "Usuario inactivo",
                        detail: "El usuario no tiene acceso para consultar documentos."),
                GetPreQuoteDocumentsFailure.NotFound =>
                    Problem(statusCode: 404,
                        title: "Precotización no encontrada",
                        detail: "No existe la precotización indicada."),
                _ => Problem(statusCode: 500,
                    title: "Error al consultar los documentos",
                    detail: "No fue posible consultar los documentos de la precotización.")
            };
        }

        var documents = result.Documents!;
        var totalPages = documents.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(documents.TotalCount / (double)pageSize);
        var items = documents.Items.Select(item =>
            new PreQuoteDocumentListItemResponse(
                item.DocumentId,
                item.PreQuoteId,
                item.OriginalFileName,
                item.ContentType,
                item.SizeBytes,
                item.CreatedAtUtc,
                PreQuoteDocumentResponseMapper.Map(
                    item.ProcessingAvailability),
                PreQuoteDocumentResponseMapper.Map(item.LatestAttempt),
                item.StructuredExtractionSummary is null
                    ? null
                    : new StructuredExtractionSummaryResponse(
                        item.StructuredExtractionSummary.StructuredExtractionId,
                        item.StructuredExtractionSummary.SourceProcessingAttemptId,
                        item.StructuredExtractionSummary.IsFromLatestAttempt,
                        PreQuoteDocumentResponseMapper.Map(
                            item.StructuredExtractionSummary.Status),
                        item.StructuredExtractionSummary.ProjectName,
                        item.StructuredExtractionSummary.ClientName,
                        item.StructuredExtractionSummary.Location,
                        item.StructuredExtractionSummary.ItemCount,
                        item.StructuredExtractionSummary.DocumentReferenceCount,
                        item.StructuredExtractionSummary.ItemsRequiringReview,
                        item.StructuredExtractionSummary.KnownQuoteableUnitCount,
                        item.StructuredExtractionSummary.IssueCount,
                        item.StructuredExtractionSummary.ConflictCount,
                        item.StructuredExtractionSummary.ProcessingMethod,
                        item.StructuredExtractionSummary.DurationMs,
                        item.StructuredExtractionSummary.CreatedAtUtc)))
            .ToArray();

        return Ok(new GetPreQuoteDocumentsResponse(
            items, page, pageSize, documents.TotalCount, totalPages));
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(CreatePreQuoteDocumentResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiProblemDetailsResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create(
        [FromRoute] Guid preQuoteId,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
        {
            return InvalidMultipartRequest();
        }

        IFormCollection form;

        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return InvalidMultipartRequest();
        }
        catch (IOException)
        {
            return InvalidMultipartRequest();
        }
        if (form.Count != 0
            || form.Files.Count != 1
            || !string.Equals(
                form.Files[0].Name,
                "file",
                StringComparison.Ordinal)
            || file is null)
        {
            return InvalidMultipartRequest();
        }

        var originalFileName = NormalizeFileName(file.FileName);

        await using var content = file.OpenReadStream();

        var result = await createPreQuoteDocumentService.ExecuteAsync(
            new CreatePreQuoteDocumentCommand(
                preQuoteId,
                originalFileName,
                file.ContentType,
                file.Length,
                content),
            cancellationToken);

        if (result.IsSuccess && result.Document is not null)
        {
            return StatusCode(
                StatusCodes.Status201Created,
                new CreatePreQuoteDocumentResponse(
                    result.Document.Id,
                    result.Document.PreQuoteId,
                    result.Document.OriginalFileName,
                    result.Document.ContentType,
                    result.Document.SizeBytes,
                    result.Document.CreatedAtUtc));
        }

        return result.Failure switch
        {
            CreatePreQuoteDocumentFailure.InvalidRequest => DocumentProblem(
                statusCode: StatusCodes.Status400BadRequest,
                errorCode: DocumentErrorCodes.InvalidRequest,
                title: "Solicitud inválida.",
                detail: "Los datos enviados no son válidos."),
            CreatePreQuoteDocumentFailure.InvalidFileName => DocumentProblem(
                statusCode: StatusCodes.Status400BadRequest,
                errorCode: DocumentErrorCodes.InvalidRequest,
                title: "Nombre de archivo inválido.",
                detail: "El archivo debe tener un nombre válido de hasta 255 caracteres."),
            CreatePreQuoteDocumentFailure.UnsupportedFileType => DocumentProblem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                errorCode: DocumentErrorCodes.UnsupportedFileType,
                title: "Tipo de archivo no soportado.",
                detail: "Únicamente se permiten documentos PDF."),
            CreatePreQuoteDocumentFailure.EmptyFile => DocumentProblem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                errorCode: DocumentErrorCodes.EmptyFile,
                title: "Archivo vacío.",
                detail: "El documento PDF debe contener información."),
            CreatePreQuoteDocumentFailure.FileTooLarge => DocumentProblem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                errorCode: DocumentErrorCodes.FileTooLarge,
                title: "Archivo demasiado grande.",
                detail: "El documento PDF no puede superar 20 MiB."),
            CreatePreQuoteDocumentFailure.Unauthorized => DocumentProblem(
                statusCode: StatusCodes.Status401Unauthorized,
                errorCode: PreQuoteErrorCodes.Unauthorized,
                title: "No autorizado.",
                detail: "No fue posible identificar al usuario autenticado."),
            CreatePreQuoteDocumentFailure.InactiveUser => DocumentProblem(
                statusCode: StatusCodes.Status403Forbidden,
                errorCode: PreQuoteErrorCodes.InactiveUser,
                title: "Usuario inactivo.",
                detail: "El usuario autenticado se encuentra inactivo."),
            CreatePreQuoteDocumentFailure.PreQuoteNotFound => DocumentProblem(
                statusCode: StatusCodes.Status404NotFound,
                errorCode: DocumentErrorCodes.PreQuoteNotFound,
                title: "Precotización no encontrada.",
                detail: "La precotización indicada no existe."),
            CreatePreQuoteDocumentFailure.ProjectNotFound => DocumentProblem(
                statusCode: StatusCodes.Status404NotFound,
                errorCode: DocumentErrorCodes.PreQuoteNotFound,
                title: "Proyecto no encontrado.",
                detail: "El proyecto asociado a la precotización no existe."),
            CreatePreQuoteDocumentFailure.InactiveProject => DocumentProblem(
                statusCode: StatusCodes.Status409Conflict,
                errorCode: DocumentErrorCodes.ProjectInactive,
                title: "Proyecto inactivo.",
                detail: "No se pueden agregar documentos a un proyecto inactivo."),
            CreatePreQuoteDocumentFailure.ClientNotFound => DocumentProblem(
                statusCode: StatusCodes.Status404NotFound,
                errorCode: DocumentErrorCodes.PreQuoteNotFound,
                title: "Cliente no encontrado.",
                detail: "El cliente asociado al proyecto no existe."),
            CreatePreQuoteDocumentFailure.InactiveClient => DocumentProblem(
                statusCode: StatusCodes.Status409Conflict,
                errorCode: DocumentErrorCodes.ClientInactive,
                title: "Cliente inactivo.",
                detail: "No se pueden agregar documentos para un cliente inactivo."),
            CreatePreQuoteDocumentFailure.QueryError => DocumentProblem(
                statusCode: StatusCodes.Status500InternalServerError,
                errorCode: DocumentErrorCodes.PersistenceError,
                title: "Error de consulta.",
                detail: "No fue posible consultar la información requerida."),
            CreatePreQuoteDocumentFailure.StorageError => DocumentProblem(
                statusCode: StatusCodes.Status500InternalServerError,
                errorCode: DocumentErrorCodes.StorageError,
                title: "Error de almacenamiento.",
                detail: "No fue posible almacenar el documento PDF."),
            CreatePreQuoteDocumentFailure.PersistenceError => DocumentProblem(
                statusCode: StatusCodes.Status500InternalServerError,
                errorCode: DocumentErrorCodes.PersistenceError,
                title: "Error de persistencia.",
                detail: "No fue posible registrar el documento PDF."),
            CreatePreQuoteDocumentFailure.CompensationError => DocumentProblem(
                statusCode: StatusCodes.Status500InternalServerError,
                errorCode: DocumentErrorCodes.PersistenceError,
                title: "Error de compensación.",
                detail: "No fue posible revertir el archivo después del error de persistencia."),
            _ => DocumentProblem(
                statusCode: StatusCodes.Status500InternalServerError,
                errorCode: DocumentErrorCodes.PersistenceError,
                title: "Error de persistencia.",
                detail: "No fue posible registrar el documento PDF.")
        };
    }

    private ObjectResult InvalidMultipartRequest()
    {
        return DocumentProblem(
            statusCode: StatusCodes.Status400BadRequest,
            errorCode: DocumentErrorCodes.InvalidRequest,
            title: "Solicitud multipart inválida.",
            detail: "La solicitud debe contener únicamente un archivo en el campo 'file'.");
    }

    private ObjectResult DocumentProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) => ApiProblemDetailsFactory.Create(
            HttpContext, statusCode, errorCode, title, detail);

    private static string NormalizeFileName(string fileName)
    {
        var normalizedPath = fileName.Replace('\\', '/');
        var separatorIndex = normalizedPath.LastIndexOf('/');

        return (separatorIndex >= 0
                ? normalizedPath[(separatorIndex + 1)..]
                : normalizedPath)
            .Trim();
    }
}
