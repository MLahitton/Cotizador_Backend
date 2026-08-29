using Api.ErrorHandling;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.CreateRequirement;
using Application.PreQuotes.GetCurrentRequirement;
using Application.PreQuotes.GetRequirementDetails;
using Application.PreQuotes.ManageRequirementDocuments;
using Contracts.Common;
using Contracts.PreQuotes;
using Domain.PreQuotes;
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
                requirement.CommercialLine is null
                    ? null
                    : ToContract(requirement.CommercialLine.Value),
                requirement.CreatedAtUtc,
                requirement.HasTechnicalProposal,
                requirement.TechnicalProposalId,
                requirement.LatestAttemptId,
                requirement.LatestAttemptState?.ToString(),
                requirement.LatestAttemptOutcome?.ToString(),
                requirement.LatestAttemptErrorCode,
                requirement.CanEditDocuments,
                requirement.CanCancel,
                requirement.CanReplace,
                requirement.IsCurrent,
                requirement.SupersedesRequirementId,
                requirement.SupersededByRequirementId,
                ToDocumentResponses(requirement.Documents)));
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

        if (formCollection.Keys.Any(key =>
                !string.Equals(
                    key,
                    "commercialLine",
                    StringComparison.Ordinal))
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
                new CreateRequirementCommand(
                    preQuoteId,
                    form.CommercialLine,
                    commandFiles),
                cancellationToken);

            if (result.IsSuccess && result.Requirement is { } requirement)
            {
                return StatusCode(
                    StatusCodes.Status201Created,
                    new CreateRequirementResponse(
                        requirement.RequirementId,
                        requirement.PreQuoteId,
                        requirement.FileCount,
                        requirement.CommercialLine,
                        requirement.Status,
                        CanEditDocuments: true,
                        CanCancel: true,
                        CanReplace: false,
                        IsCurrent: true,
                        SupersedesRequirementId: null,
                        SupersededByRequirementId: null,
                        requirement.CreatedAtUtc,
                        requirement.Documents
                            .Select(document => new RequirementDocumentResponse(
                                document.RequirementFileId,
                                document.FileName,
                                document.ContentType,
                                document.SizeBytes,
                                document.CreatedAtUtc))
                            .ToArray()));
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

    [HttpGet("~/api/v2/requirements/{requirementId:guid}")]
    [ProducesResponseType(
        typeof(RequirementDetailsResponse),
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
    public async Task<IActionResult> GetRequirement(
        [FromRoute] Guid requirementId,
        [FromServices] GetRequirementDetailsService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new GetRequirementDetailsCommand(requirementId),
            cancellationToken);

        return result.IsSuccess && result.Requirement is { } requirement
            ? Ok(ToDetailsResponse(requirement))
            : MapFailure(result.Failure);
    }

    [HttpGet("~/api/v2/requirements/{requirementId:guid}/documents")]
    [ProducesResponseType(
        typeof(IReadOnlyList<RequirementDocumentResponse>),
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
    public async Task<IActionResult> GetRequirementDocuments(
        [FromRoute] Guid requirementId,
        [FromServices] GetRequirementDetailsService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(
            new GetRequirementDetailsCommand(requirementId),
            cancellationToken);

        return result.IsSuccess && result.Requirement is { } requirement
            ? Ok(ToDocumentResponses(requirement.Documents))
            : MapFailure(result.Failure);
    }

    [HttpPost("~/api/v2/requirements/{requirementId:guid}/documents")]
    [ProducesResponseType(
        typeof(RequirementLifecycleResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> AddDocument(
        [FromRoute] Guid requirementId,
        [FromForm] CreateRequirementForm form,
        [FromServices] ManageRequirementDocumentsService service,
        CancellationToken cancellationToken)
    {
        var commandFiles = await ReadFilesAsync(form, expectedCount: 1, cancellationToken);
        if (commandFiles is null)
        {
            return InvalidMultipartRequest();
        }

        RequirementLifecycleResult result;
        try
        {
            result = await service.AddDocumentAsync(
                new AddRequirementDocumentCommand(requirementId, commandFiles[0]),
                cancellationToken);
        }
        finally
        {
            await DisposeFilesAsync(commandFiles);
        }

        return result.IsSuccess && result.Requirement is { } requirement
            ? Ok(ToLifecycleResponse(requirement))
            : MapFailure(result.Failure);
    }

    [HttpDelete("~/api/v2/requirements/{requirementId:guid}/documents/{requirementFileId:guid}")]
    [ProducesResponseType(
        typeof(RequirementLifecycleResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveDocument(
        [FromRoute] Guid requirementId,
        [FromRoute] Guid requirementFileId,
        [FromServices] ManageRequirementDocumentsService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RemoveDocumentAsync(
            new RemoveRequirementDocumentCommand(requirementId, requirementFileId),
            cancellationToken);

        return result.IsSuccess && result.Requirement is { } requirement
            ? Ok(ToLifecycleResponse(requirement))
            : MapFailure(result.Failure);
    }

    [HttpPut("~/api/v2/requirements/{requirementId:guid}/documents/{requirementFileId:guid}")]
    [ProducesResponseType(
        typeof(RequirementLifecycleResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> ReplaceDocument(
        [FromRoute] Guid requirementId,
        [FromRoute] Guid requirementFileId,
        [FromForm] CreateRequirementForm form,
        [FromServices] ManageRequirementDocumentsService service,
        CancellationToken cancellationToken)
    {
        var commandFiles = await ReadFilesAsync(form, expectedCount: 1, cancellationToken);
        if (commandFiles is null)
        {
            return InvalidMultipartRequest();
        }

        RequirementLifecycleResult result;
        try
        {
            result = await service.ReplaceDocumentAsync(
                new ReplaceRequirementDocumentCommand(
                    requirementId,
                    requirementFileId,
                    commandFiles[0]),
                cancellationToken);
        }
        finally
        {
            await DisposeFilesAsync(commandFiles);
        }

        return result.IsSuccess && result.Requirement is { } requirement
            ? Ok(ToLifecycleResponse(requirement))
            : MapFailure(result.Failure);
    }

    [HttpPost("~/api/v2/requirements/{requirementId:guid}/replacement")]
    [ProducesResponseType(
        typeof(RequirementLifecycleResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status409Conflict)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> ReplaceRequirement(
        [FromRoute] Guid requirementId,
        [FromForm] CreateRequirementForm form,
        [FromServices] ManageRequirementDocumentsService service,
        CancellationToken cancellationToken)
    {
        var commandFiles = await ReadFilesAsync(form, expectedCount: null, cancellationToken);
        if (commandFiles is null)
        {
            return InvalidMultipartRequest();
        }

        RequirementLifecycleResult result;
        try
        {
            result = await service.ReplaceRequirementAsync(
                new ReplaceRequirementCommand(requirementId, commandFiles),
                cancellationToken);
        }
        finally
        {
            await DisposeFilesAsync(commandFiles);
        }

        return result.IsSuccess && result.Requirement is { } requirement
            ? StatusCode(StatusCodes.Status201Created, ToLifecycleResponse(requirement))
            : MapFailure(result.Failure);
    }

    [HttpPost("~/api/v2/requirements/{requirementId:guid}/cancel")]
    [ProducesResponseType(
        typeof(RequirementLifecycleResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ApiProblemDetailsResponse),
        StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid requirementId,
        [FromServices] ManageRequirementDocumentsService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CancelAsync(
            new CancelRequirementCommand(requirementId),
            cancellationToken);

        return result.IsSuccess && result.Requirement is { } requirement
            ? Ok(ToLifecycleResponse(requirement))
            : MapFailure(result.Failure);
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

    private IActionResult MapFailure(GetRequirementDetailsFailure failure)
    {
        return failure switch
        {
            GetRequirementDetailsFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "El requerimiento indicado no es valido."),
            GetRequirementDetailsFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            GetRequirementDetailsFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            GetRequirementDetailsFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            GetRequirementDetailsFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion asociada al requerimiento."),
            GetRequirementDetailsFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "El proyecto asociado a la precotizacion no existe."),
            GetRequirementDetailsFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se pueden consultar requerimientos de un proyecto inactivo."),
            GetRequirementDetailsFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "El cliente asociado al proyecto no existe."),
            GetRequirementDetailsFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se pueden consultar requerimientos de un cliente inactivo."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de consulta",
                "No fue posible consultar el requerimiento.")
        };
    }

    private IActionResult MapFailure(ManageRequirementDocumentsFailure failure)
    {
        return failure switch
        {
            ManageRequirementDocumentsFailure.InvalidRequest =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "Los datos enviados no son validos."),
            ManageRequirementDocumentsFailure.InvalidFileName =>
                RequirementProblem(
                    StatusCodes.Status400BadRequest,
                    RequirementErrorCodes.InvalidRequest,
                    "Nombre de archivo invalido",
                    "Todos los archivos deben tener un nombre valido de hasta 255 caracteres."),
            ManageRequirementDocumentsFailure.UnsupportedFileType =>
                RequirementProblem(
                    StatusCodes.Status415UnsupportedMediaType,
                    RequirementErrorCodes.UnsupportedFileType,
                    "Tipo de archivo no soportado",
                    "Unicamente se permiten archivos PDF, XLSX, JPG o PNG."),
            ManageRequirementDocumentsFailure.EmptyFile =>
                RequirementProblem(
                    StatusCodes.Status422UnprocessableEntity,
                    RequirementErrorCodes.EmptyFile,
                    "Archivo vacio",
                    "Todos los archivos deben contener informacion."),
            ManageRequirementDocumentsFailure.FileTooLarge =>
                RequirementProblem(
                    StatusCodes.Status413PayloadTooLarge,
                    RequirementErrorCodes.FileTooLarge,
                    "Archivo demasiado grande",
                    "Cada archivo no puede superar 20 MiB y el total no puede superar 100 MiB."),
            ManageRequirementDocumentsFailure.TooManyFiles =>
                RequirementProblem(
                    StatusCodes.Status413PayloadTooLarge,
                    RequirementErrorCodes.TooManyFiles,
                    "Demasiados archivos",
                    "Un requerimiento no puede contener mas de 10 archivos."),
            ManageRequirementDocumentsFailure.Unauthorized =>
                RequirementProblem(
                    StatusCodes.Status401Unauthorized,
                    PreQuoteErrorCodes.Unauthorized,
                    "No autorizado",
                    "No fue posible identificar al usuario autenticado."),
            ManageRequirementDocumentsFailure.InactiveUser =>
                RequirementProblem(
                    StatusCodes.Status403Forbidden,
                    PreQuoteErrorCodes.InactiveUser,
                    "Usuario inactivo",
                    "El usuario autenticado se encuentra inactivo."),
            ManageRequirementDocumentsFailure.RequirementNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Requerimiento no encontrado",
                    "No existe el requerimiento indicado."),
            ManageRequirementDocumentsFailure.DocumentNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.RequirementNotFound,
                    "Documento no encontrado",
                    "No existe el documento indicado para este requerimiento."),
            ManageRequirementDocumentsFailure.RequirementNotMutable =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.RequirementNotMutable,
                    "Requerimiento no editable",
                    "Los documentos no pueden modificarse despues de iniciar procesamiento."),
            ManageRequirementDocumentsFailure.RequirementNotReplaceable =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.RequirementNotReplaceable,
                    "Requerimiento no reemplazable",
                    "Solo un requerimiento vigente ya procesado o fallido puede reemplazarse."),
            ManageRequirementDocumentsFailure.PreQuoteNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Precotizacion no encontrada",
                    "No existe la precotizacion indicada."),
            ManageRequirementDocumentsFailure.ProjectNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Proyecto no encontrado",
                    "El proyecto asociado a la precotizacion no existe."),
            ManageRequirementDocumentsFailure.InactiveProject =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ProjectInactive,
                    "Proyecto inactivo",
                    "No se pueden modificar requerimientos de un proyecto inactivo."),
            ManageRequirementDocumentsFailure.ClientNotFound =>
                RequirementProblem(
                    StatusCodes.Status404NotFound,
                    RequirementErrorCodes.PreQuoteNotFound,
                    "Cliente no encontrado",
                    "El cliente asociado al proyecto no existe."),
            ManageRequirementDocumentsFailure.InactiveClient =>
                RequirementProblem(
                    StatusCodes.Status409Conflict,
                    RequirementErrorCodes.ClientInactive,
                    "Cliente inactivo",
                    "No se pueden modificar requerimientos de un cliente inactivo."),
            ManageRequirementDocumentsFailure.StorageError =>
                RequirementProblem(
                    StatusCodes.Status500InternalServerError,
                    RequirementErrorCodes.StorageError,
                    "Error de almacenamiento",
                    "No fue posible almacenar los archivos del requerimiento."),
            _ => RequirementProblem(
                StatusCodes.Status500InternalServerError,
                RequirementErrorCodes.PersistenceError,
                "Error de persistencia",
                "No fue posible modificar el requerimiento.")
        };
    }

    private async Task<IReadOnlyList<CreateRequirementFileInput>?> ReadFilesAsync(
        CreateRequirementForm form,
        int? expectedCount,
        CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
        {
            return null;
        }

        IFormCollection formCollection;
        try
        {
            formCollection = await Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        if (formCollection.Keys.Any()
            || formCollection.Files.Count == 0
            || form.Files.Count != formCollection.Files.Count
            || formCollection.Files.Any(file =>
                !string.Equals(file.Name, "files", StringComparison.Ordinal))
            || (expectedCount is { } count && form.Files.Count != count))
        {
            return null;
        }

        var files = new List<CreateRequirementFileInput>(form.Files.Count);
        foreach (var file in form.Files)
        {
            files.Add(new CreateRequirementFileInput(
                NormalizeFileName(file.FileName),
                file.ContentType,
                file.Length,
                file.OpenReadStream()));
        }

        return files;
    }

    private static RequirementLifecycleResponse ToLifecycleResponse(
        RequirementLifecycleReadModel requirement) =>
        new(
            requirement.RequirementId,
            requirement.PreQuoteId,
            requirement.FileCount,
            requirement.CommercialLine,
            requirement.Status,
            requirement.CanEditDocuments,
            requirement.CanCancel,
            requirement.CanReplace,
            requirement.IsCurrent,
            requirement.SupersedesRequirementId,
            requirement.SupersededByRequirementId,
            requirement.UpdatedAtUtc,
            ToDocumentResponses(requirement.Documents));

    private static RequirementDetailsResponse ToDetailsResponse(
        RequirementDetailsReadModel requirement) =>
        new(
            requirement.RequirementId,
            requirement.PreQuoteId,
            requirement.Status.ToString().ToUpperInvariant(),
            requirement.CommercialLine is null
                ? null
                : ToContract(requirement.CommercialLine.Value),
            requirement.CanEditDocuments,
            requirement.CanCancel,
            requirement.CanReplace,
            requirement.IsCurrent,
            requirement.SupersedesRequirementId,
            requirement.SupersededByRequirementId,
            requirement.CreatedAtUtc,
            requirement.UpdatedAtUtc,
            ToDocumentResponses(requirement.Documents));

    private static IReadOnlyList<RequirementDocumentResponse> ToDocumentResponses(
        IReadOnlyList<RequirementDocumentReadModel> documents) =>
        documents
            .Select(document => new RequirementDocumentResponse(
                document.RequirementFileId,
                document.FileName,
                document.ContentType,
                document.SizeBytes,
                document.CreatedAtUtc))
            .ToArray();

    private static async Task DisposeFilesAsync(
        IReadOnlyList<CreateRequirementFileInput> files)
    {
        foreach (var file in files)
        {
            await file.Content.DisposeAsync();
        }
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

    private static string ToContract(RequirementCommercialLine commercialLine) =>
        commercialLine switch
        {
            RequirementCommercialLine.Classic => "CLASSIC",
            RequirementCommercialLine.Essential => "ESSENTIAL",
            RequirementCommercialLine.Bioconfort => "BIOCONFORT",
            RequirementCommercialLine.Signature => "SIGNATURE",
            _ => throw new ArgumentOutOfRangeException(nameof(commercialLine))
        };
}
