using Api.ErrorHandling;
using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.GetCurrentRequirement;
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
[Route("api/v2/prequotes/{preQuoteId}/requirements")]
public sealed class PreQuoteRequirementsController(
    CreateRequirementService createRequirementService) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType(
        typeof(CurrentRequirementResponse),
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
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetCurrent(
        [FromRoute] Guid preQuoteId,
        [FromServices] GetCurrentRequirementService getCurrentRequirementService,
        CancellationToken cancellationToken)
    {
        var result = await getCurrentRequirementService.ExecuteAsync(
            new GetCurrentRequirementCommand(preQuoteId),
            cancellationToken);

        if (result.IsSuccess && result.Requirement is { } requirement)
        {
            return Ok(new CurrentRequirementResponse(
                requirement.RequirementId,
                requirement.PreQuoteId,
                requirement.Status.ToString().ToUpperInvariant(),
                requirement.CreatedAtUtc,
                requirement.HasTechnicalProposal,
                requirement.TechnicalProposalId,
                requirement.LatestAttemptState?.ToString(),
                requirement.LatestAttemptOutcome?.ToString(),
                requirement.LatestAttemptErrorCode));
        }

        return MapFailure(result.Failure);
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(CreateRequirementResponse),
        StatusCodes.Status201Created)]
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
        [FromForm] CreateRequirementForm form,
        CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
        {
            return RequirementProblem(
                StatusCodes.Status415UnsupportedMediaType,
                ApiErrorCodes.ApiUnsupportedMediaType,
                "Solicitud multipart invalida",
                "La solicitud debe enviarse como multipart/form-data.");
        }

        IFormCollection formCollection;
        try
        {
            formCollection = await Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return InvalidMultipartRequest();
        }
        catch (IOException)
        {
            return InvalidMultipartRequest();
        }

        if (formCollection.Count != 0
            || formCollection.Files.Count == 0
            || form.Files.Count != formCollection.Files.Count
            || formCollection.Files.Any(file =>
                !string.Equals(file.Name, "files", StringComparison.Ordinal)))
        {
            return InvalidMultipartRequest();
        }

        var streams = new List<Stream>(form.Files.Count);
        try
        {
            foreach (var file in form.Files)
            {
                streams.Add(file.OpenReadStream());
            }

            var commandFiles = form.Files.Select((file, index) =>
                new CreateRequirementFileInput(
                    NormalizeFileName(file.FileName),
                    file.ContentType,
                    file.Length,
                    streams[index])).ToArray();

            var result = await createRequirementService.ExecuteAsync(
                new CreateRequirementCommand(preQuoteId, commandFiles),
                cancellationToken);

            if (result.IsSuccess && result.Requirement is { } requirement)
            {
                return StatusCode(
                    StatusCodes.Status201Created,
                    new CreateRequirementResponse(
                        requirement.RequirementId,
                        requirement.PreQuoteId,
                        requirement.FileCount,
                        requirement.Status,
                        requirement.CreatedAtUtc));
            }

            return MapFailure(result.Failure);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private IActionResult MapFailure(CreateRequirementFailure failure)
    {
        return failure switch
        {
            CreateRequirementFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "Los datos enviados no son validos."),
            CreateRequirementFailure.InvalidFileName =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Nombre de archivo invalido",
                    "Todos los archivos deben tener un nombre valido de hasta 255 caracteres."),
            CreateRequirementFailure.UnsupportedFileType =>
                RequirementProblem(
                    StatusCodes.Status415UnsupportedMediaType,
                    RequirementErrorCodes.UnsupportedFileType,
                    "Tipo de archivo no soportado",
                    "Unicamente se permiten archivos PDF, XLSX, JPG o PNG."),
            CreateRequirementFailure.EmptyFile =>
                RequirementProblem(
                    StatusCodes.Status422UnprocessableEntity,
                    RequirementErrorCodes.EmptyFile,
                    "Archivo vacio",
                    "Todos los archivos deben contener informacion."),
            CreateRequirementFailure.FileTooLarge =>
                RequirementProblem(
                    StatusCodes.Status413PayloadTooLarge,
                    RequirementErrorCodes.FileTooLarge,
                    "Archivo demasiado grande",
                    "Cada archivo no puede superar 20 MiB y el total no puede superar 100 MiB."),
            CreateRequirementFailure.TooManyFiles =>
                RequirementProblem(
                    StatusCodes.Status413PayloadTooLarge,
                    RequirementErrorCodes.TooManyFiles,
                    "Demasiados archivos",
                    "Un requerimiento no puede contener mas de 10 archivos."),
            CreateRequirementFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            CreateRequirementFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            CreateRequirementFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion indicada."),
            CreateRequirementFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "El proyecto asociado a la precotizacion no existe."),
            CreateRequirementFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se pueden crear requerimientos en un proyecto inactivo."),
            CreateRequirementFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "El cliente asociado al proyecto no existe."),
            CreateRequirementFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se pueden crear requerimientos para un cliente inactivo."),
            CreateRequirementFailure.StorageError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.StorageError,
                    "Error de almacenamiento",
                    "No fue posible almacenar los archivos del requerimiento."),
            CreateRequirementFailure.PersistenceError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.PersistenceError,
                    "Error de persistencia",
                    "No fue posible registrar el requerimiento."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible registrar el requerimiento.")
        };
    }

    private IActionResult MapFailure(GetCurrentRequirementFailure failure)
    {
        return failure switch
        {
            GetCurrentRequirementFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "La precotizacion indicada no es valida."),
            GetCurrentRequirementFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            GetCurrentRequirementFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            GetCurrentRequirementFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion indicada."),
            GetCurrentRequirementFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "El proyecto asociado a la precotizacion no existe."),
            GetCurrentRequirementFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se pueden consultar requerimientos de un proyecto inactivo."),
            GetCurrentRequirementFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "El cliente asociado al proyecto no existe."),
            GetCurrentRequirementFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se pueden consultar requerimientos de un cliente inactivo."),
            GetCurrentRequirementFailure.CurrentRequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "Aun no se ha procesado un requerimiento para esta precotizacion."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de consulta",
                "No fue posible consultar el requerimiento vigente.")
        };
    }

    private ObjectResult InvalidMultipartRequest()
    {
        return RequirementProblem(
            StatusCodes.Status400BadRequest,
            RequirementErrorCodes.InvalidRequest,
            "Solicitud multipart invalida",
            "La solicitud debe contener uno o mas archivos en el campo 'files'.");
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
