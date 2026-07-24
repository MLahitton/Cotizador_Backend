using Application.PreQuotes.CreatePreQuoteDocument;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequotes/{preQuoteId:guid}/documents")]
public sealed class PreQuoteDocumentsController(
    CreatePreQuoteDocumentService createPreQuoteDocumentService) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(
        typeof(CreatePreQuoteDocumentResponse),
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
            CreatePreQuoteDocumentFailure.InvalidRequest => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud inválida.",
                detail: "Los datos enviados no son válidos."),
            CreatePreQuoteDocumentFailure.InvalidFileName => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Nombre de archivo inválido.",
                detail: "El archivo debe tener un nombre válido de hasta 255 caracteres."),
            CreatePreQuoteDocumentFailure.UnsupportedFileType => Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Tipo de archivo no soportado.",
                detail: "Únicamente se permiten documentos PDF."),
            CreatePreQuoteDocumentFailure.EmptyFile => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "Archivo vacío.",
                detail: "El documento PDF debe contener información."),
            CreatePreQuoteDocumentFailure.FileTooLarge => Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Archivo demasiado grande.",
                detail: "El documento PDF no puede superar 20 MiB."),
            CreatePreQuoteDocumentFailure.Unauthorized => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No autorizado.",
                detail: "No fue posible identificar al usuario autenticado."),
            CreatePreQuoteDocumentFailure.InactiveUser => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Usuario inactivo.",
                detail: "El usuario autenticado se encuentra inactivo."),
            CreatePreQuoteDocumentFailure.PreQuoteNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Precotización no encontrada.",
                detail: "La precotización indicada no existe."),
            CreatePreQuoteDocumentFailure.ProjectNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Proyecto no encontrado.",
                detail: "El proyecto asociado a la precotización no existe."),
            CreatePreQuoteDocumentFailure.InactiveProject => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Proyecto inactivo.",
                detail: "No se pueden agregar documentos a un proyecto inactivo."),
            CreatePreQuoteDocumentFailure.ClientNotFound => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Cliente no encontrado.",
                detail: "El cliente asociado al proyecto no existe."),
            CreatePreQuoteDocumentFailure.InactiveClient => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Cliente inactivo.",
                detail: "No se pueden agregar documentos para un cliente inactivo."),
            CreatePreQuoteDocumentFailure.QueryError => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error de consulta.",
                detail: "No fue posible consultar la información requerida."),
            CreatePreQuoteDocumentFailure.StorageError => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error de almacenamiento.",
                detail: "No fue posible almacenar el documento PDF."),
            CreatePreQuoteDocumentFailure.PersistenceError => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error de persistencia.",
                detail: "No fue posible registrar el documento PDF."),
            CreatePreQuoteDocumentFailure.CompensationError => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error de compensación.",
                detail: "No fue posible revertir el archivo después del error de persistencia."),
            _ => Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Error de persistencia.",
                detail: "No fue posible registrar el documento PDF.")
        };
    }

    private ObjectResult InvalidMultipartRequest()
    {
        return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Solicitud multipart inválida.",
            detail: "La solicitud debe contener únicamente un archivo en el campo 'file'.");
    }

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
