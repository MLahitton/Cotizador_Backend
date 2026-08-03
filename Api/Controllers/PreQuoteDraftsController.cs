using Api.ErrorHandling;
using Application.PreQuotes;
using Application.PreQuotes.ApprovePreQuoteDraft;
using Application.PreQuotes.CreatePreQuoteDraft;
using Application.PreQuotes.GetPreQuoteDraft;
using Application.PreQuotes.UpdatePreQuoteDraft;
using Contracts.Common;
using Contracts.PreQuotes;
using Domain.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[ContractualErrors(InvalidRequestErrorCode = PreQuoteDraftErrorCodes.InvalidRequest)]
[Route("api/v1/prequotes/{preQuoteId}/draft")]
public sealed class PreQuoteDraftsController(
    CreatePreQuoteDraftService createService,
    GetPreQuoteDraftService getService,
    UpdatePreQuoteDraftService updateService,
    ApprovePreQuoteDraftService approveService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PreQuoteDraftDetailsResponse>(
        StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Create(
        [FromRoute] Guid preQuoteId, CreatePreQuoteDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createService.ExecuteAsync(
            new(preQuoteId, request.SourceDocumentId,
                request.SourceStructuredExtractionId), cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, Map(result.Draft!))
            : Failure(result.Failure);
    }

    [HttpGet]
    [ProducesResponseType<PreQuoteDraftDetailsResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Get(
        [FromRoute] Guid preQuoteId,
        CancellationToken cancellationToken)
    {
        var result = await getService.ExecuteAsync(
            new(preQuoteId), cancellationToken);
        return result.IsSuccess ? Ok(Map(result.Draft!)) : Failure(result.Failure);
    }

    [HttpPut]
    [ProducesResponseType<PreQuoteDraftDetailsResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Update(
        [FromRoute] Guid preQuoteId, UpdatePreQuoteDraftRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new UpdatePreQuoteDraftCommand(
                preQuoteId, request.ExpectedVersion, request.Project.Name,
                request.Project.ClientName, request.Project.Location,
                request.Items.Select(x => new PreQuoteDraftItemEdit(
                    x.DraftItemId, x.Sequence, x.Reference, x.Description,
                    Element(x.ElementType), x.RawMeasurements,
                    x.WidthMillimeters, x.HeightMillimeters, x.Quantity,
                    x.IsIncluded)).ToArray(),
                request.Requirements.Select(x => new PreQuoteDraftRequirementEdit(
                    x.DraftRequirementId, x.Sequence, Category(x.Category),
                    x.Value, x.IsIncluded)).ToArray(),
                request.DocumentReferences.Select(x => new PreQuoteDraftReferenceEdit(
                    x.DraftDocumentReferenceId, x.Sequence, x.Reference,
                    x.Description, x.Detail, x.Quantity, x.IsIncluded)).ToArray(),
                request.Issues.Select(IssueResolution).ToArray(),
                request.Conflicts.Select(ConflictResolution).ToArray());
            var result = await updateService.ExecuteAsync(command, cancellationToken);
            return result.IsSuccess ? Ok(Map(result.Draft!)) : Failure(result.Failure);
        }
        catch (ArgumentException) { return Failure(PreQuoteDraftFailure.InvalidRequest); }
    }

    [HttpPost("approve")]
    [ProducesResponseType<PreQuoteDraftDetailsResponse>(
        StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiProblemDetailsResponse>(
        StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Approve(
        [FromRoute] Guid preQuoteId, ApprovePreQuoteDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await approveService.ExecuteAsync(
            new(preQuoteId, request.ExpectedVersion), cancellationToken);
        return result.IsSuccess ? Ok(Map(result.Draft!)) : Failure(result.Failure);
    }

    private IActionResult Failure(PreQuoteDraftFailure failure) => failure switch
    {
        PreQuoteDraftFailure.InvalidRequest => DraftProblem(
            StatusCodes.Status400BadRequest,
            PreQuoteDraftErrorCodes.InvalidRequest,
            "Solicitud invalida",
            "Los datos del borrador no son validos."),
        PreQuoteDraftFailure.Unauthorized => DraftProblem(
            StatusCodes.Status401Unauthorized,
            PreQuoteErrorCodes.Unauthorized,
            "No autorizado",
            "No fue posible identificar al usuario autenticado."),
        PreQuoteDraftFailure.InactiveUser => DraftProblem(
            StatusCodes.Status403Forbidden,
            PreQuoteErrorCodes.InactiveUser,
            "Usuario inactivo",
            "El usuario no tiene acceso a esta operacion."),
        PreQuoteDraftFailure.NotFound => DraftProblem(
            StatusCodes.Status404NotFound,
            PreQuoteDraftErrorCodes.NotFound,
            "Borrador no encontrado",
            "No fue posible encontrar la informacion solicitada."),
        PreQuoteDraftFailure.VersionConflict => DraftProblem(
            StatusCodes.Status409Conflict,
            PreQuoteDraftErrorCodes.VersionConflict,
            "Conflicto de concurrencia",
            "El borrador fue modificado por otro usuario. Consulte nuevamente la version actual antes de guardar."),
        PreQuoteDraftFailure.InactiveProject => DraftProblem(
            StatusCodes.Status409Conflict,
            PreQuoteDraftErrorCodes.ProjectInactive,
            "Proyecto inactivo",
            "El proyecto debe estar activo."),
        PreQuoteDraftFailure.InactiveClient => DraftProblem(
            StatusCodes.Status409Conflict,
            PreQuoteDraftErrorCodes.ClientInactive,
            "Cliente inactivo",
            "El cliente debe estar activo."),
        PreQuoteDraftFailure.DraftAlreadyExists => DraftProblem(
            StatusCodes.Status409Conflict,
            PreQuoteDraftErrorCodes.AlreadyExists,
            "Borrador existente",
            "La precotizacion ya tiene un borrador."),
        PreQuoteDraftFailure.DraftAlreadyApproved => DraftProblem(
            StatusCodes.Status409Conflict,
            PreQuoteDraftErrorCodes.AlreadyApproved,
            "Borrador aprobado",
            "El borrador aprobado no puede modificarse."),
        PreQuoteDraftFailure.PendingIssues => DraftProblem(
            StatusCodes.Status409Conflict,
            PreQuoteDraftErrorCodes.PendingIssues,
            "Issues pendientes",
            "Todos los issues deben resolverse o descartarse."),
        PreQuoteDraftFailure.PendingConflicts => DraftProblem(
            StatusCodes.Status409Conflict,
            PreQuoteDraftErrorCodes.PendingConflicts,
            "Conflictos pendientes",
            "Todos los conflictos deben resolverse o descartarse."),
        PreQuoteDraftFailure.InvalidDraftContent => DraftProblem(
            StatusCodes.Status409Conflict,
            PreQuoteDraftErrorCodes.InvalidContent,
            "Borrador incompleto",
            "El borrador no cumple las condiciones requeridas."),
        PreQuoteDraftFailure.QueryError => DraftProblem(
            StatusCodes.Status500InternalServerError,
            PreQuoteDraftErrorCodes.QueryError,
            "Error al consultar borrador",
            "No fue posible consultar el borrador."),
        PreQuoteDraftFailure.PersistenceError => DraftProblem(
            StatusCodes.Status500InternalServerError,
            PreQuoteDraftErrorCodes.PersistenceError,
            "Error al guardar borrador",
            "No fue posible guardar el borrador."),
        _ => DraftProblem(
            StatusCodes.Status500InternalServerError,
            ApiErrorCodes.InternalServerError,
            "Error del borrador",
            "No fue posible completar la operacion.")
    };

    private ObjectResult DraftProblem(
        int statusCode,
        string errorCode,
        string title,
        string detail) => ApiProblemDetailsFactory.Create(
            HttpContext,
            statusCode,
            errorCode,
            title,
            detail);

    private static PreQuoteDraftDetailsResponse Map(PreQuoteDraft d)
    {
        var items = d.Items.OrderBy(x => x.Sequence).ToArray();
        var requirements = d.Requirements.OrderBy(x => x.Sequence).ToArray();
        var references = d.DocumentReferences.OrderBy(x => x.Sequence).ToArray();
        var issues = d.Issues.OrderBy(x => x.Sequence).ToArray();
        var conflicts = d.Conflicts.OrderBy(x => x.Sequence).ToArray();
        return new(
            d.Id, d.PreQuoteId, d.SourceDocumentId,
            d.SourceStructuredExtractionId, Status(d.Status), d.Version,
            new(d.ProjectName, d.ClientName, d.Location),
            items.Select(x => new PreQuoteDraftItemResponse(
                x.Id, x.Sequence, Origin(x.Origin), x.SourceItemSequence,
                x.Reference, x.Description, Element(x.ElementType),
                x.RawMeasurements, x.WidthMillimeters, x.HeightMillimeters,
                x.Quantity, x.IsIncluded)).ToArray(),
            requirements.Select(x => new PreQuoteDraftRequirementResponse(
                x.Id, x.Sequence, Origin(x.Origin),
                x.SourceRequirementSequence, Category(x.Category),
                x.Value, x.IsIncluded)).ToArray(),
            references.Select(x => new PreQuoteDraftDocumentReferenceResponse(
                x.Id, x.Sequence, Origin(x.Origin),
                x.SourceDocumentReferenceSequence, x.Reference,
                x.Description, x.Detail, x.Quantity, x.IsIncluded)).ToArray(),
            issues.Select(x => new PreQuoteDraftIssueResponse(
                x.Id, x.Sequence, x.SourceIssueSequence, Issue(x.Code),
                x.Message, x.ItemSequence, x.PageNumbers,
                Resolution(x.ResolutionStatus), x.ResolutionNote,
                x.ResolvedByUserId, x.ResolvedAtUtc)).ToArray(),
            conflicts.Select(x => new PreQuoteDraftConflictResponse(
                x.Id, x.Sequence, x.SourceConflictSequence, Conflict(x.Code),
                x.Message, x.ItemSequences, x.PageNumbers,
                Resolution(x.ResolutionStatus), x.ResolutionNote,
                x.ResolvedByUserId, x.ResolvedAtUtc)).ToArray(),
            new(items.Length, items.Count(x => x.IsIncluded),
                items.Count(x => !x.IsIncluded),
                items.Count(x => x.Origin == PreQuoteDraftOrigin.Manual),
                items.Count(x => x.IsIncluded && !x.IsCompleteForApproval),
                items.Where(x => x.IsIncluded).Sum(x => (long)(x.Quantity ?? 0)),
                requirements.Length, requirements.Count(x => x.IsIncluded),
                references.Length, references.Count(x => x.IsIncluded),
                issues.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Pending),
                issues.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Resolved),
                issues.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Dismissed),
                conflicts.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Pending),
                conflicts.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Resolved),
                conflicts.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Dismissed)),
            new(d.CreatedByUserId, d.UpdatedByUserId, d.ApprovedByUserId,
                d.CreatedAtUtc, d.UpdatedAtUtc, d.ApprovedAtUtc));
    }

    private static PreQuoteDraftResolutionEdit IssueResolution(PreQuoteDraftIssueResolutionRequest x) => new(x.DraftIssueId, ResolutionStatus(x.ResolutionStatus), x.ResolutionNote);
    private static PreQuoteDraftResolutionEdit ConflictResolution(PreQuoteDraftConflictResolutionRequest x) => new(x.DraftConflictId, ResolutionStatus(x.ResolutionStatus), x.ResolutionNote);
    private static PreQuoteDraftResolutionStatus ResolutionStatus(string x) => x switch { "PENDING" => PreQuoteDraftResolutionStatus.Pending, "RESOLVED" => PreQuoteDraftResolutionStatus.Resolved, "DISMISSED" => PreQuoteDraftResolutionStatus.Dismissed, _ => throw new ArgumentException() };
    private static StructuredElementType Element(string x) => x switch { "WINDOW" => StructuredElementType.Window, "DOOR" => StructuredElementType.Door, "FACADE" => StructuredElementType.Facade, "PARTITION" => StructuredElementType.Partition, "RAILING" => StructuredElementType.Railing, "SKYLIGHT" => StructuredElementType.Skylight, "OTHER" => StructuredElementType.Other, _ => throw new ArgumentException() };
    private static string Element(StructuredElementType x) => x.ToString().ToUpperInvariant();
    private static RequirementCategory Category(string x) => x switch { "GLASS_SPECIFICATION" => RequirementCategory.GlassSpecification, "PROFILE_SPECIFICATION" => RequirementCategory.ProfileSpecification, "FINISH" => RequirementCategory.Finish, "ACCESSORIES_AND_SEALANTS" => RequirementCategory.AccessoriesAndSealants, "GENERAL_NOTE" => RequirementCategory.GeneralNote, _ => throw new ArgumentException() };
    private static string Category(RequirementCategory x) => x switch { RequirementCategory.GlassSpecification => "GLASS_SPECIFICATION", RequirementCategory.ProfileSpecification => "PROFILE_SPECIFICATION", RequirementCategory.Finish => "FINISH", RequirementCategory.AccessoriesAndSealants => "ACCESSORIES_AND_SEALANTS", _ => "GENERAL_NOTE" };
    private static string Status(PreQuoteDraftStatus x) => x switch { PreQuoteDraftStatus.PendingReview => "PENDING_REVIEW", PreQuoteDraftStatus.InReview => "IN_REVIEW", _ => "APPROVED" };
    private static string Origin(PreQuoteDraftOrigin x) => x == PreQuoteDraftOrigin.Ai ? "AI" : "MANUAL";
    private static string Resolution(PreQuoteDraftResolutionStatus x) => x.ToString().ToUpperInvariant();
    private static string Issue(StructuredIssueCode x) => x switch { StructuredIssueCode.ProjectNameNotFound => "PROJECT_NAME_NOT_FOUND", StructuredIssueCode.NoQuoteableItemsFound => "NO_QUOTEABLE_ITEMS_FOUND", StructuredIssueCode.IncompleteTableRow => "INCOMPLETE_TABLE_ROW", StructuredIssueCode.MissingItemReference => "MISSING_ITEM_REFERENCE", StructuredIssueCode.MissingOrInvalidMeasurements => "MISSING_OR_INVALID_MEASUREMENTS", StructuredIssueCode.MissingOrInvalidQuantity => "MISSING_OR_INVALID_QUANTITY", StructuredIssueCode.UnknownElementType => "UNKNOWN_ELEMENT_TYPE", _ => "OCR_REVIEW_REQUIRED" };
    private static string Conflict(StructuredConflictCode x) => x switch { StructuredConflictCode.ConflictingProjectName => "CONFLICTING_PROJECT_NAME", StructuredConflictCode.ConflictingClientName => "CONFLICTING_CLIENT_NAME", StructuredConflictCode.ConflictingLocation => "CONFLICTING_LOCATION", _ => "DUPLICATE_ITEM_REFERENCE" };
}
