using Application.PreQuotes;
using Application.PreQuotes.ApprovePreQuoteDraft;
using Application.PreQuotes.CreatePreQuoteDraft;
using Application.PreQuotes.GetPreQuoteDraft;
using Application.PreQuotes.UpdatePreQuoteDraft;
using Contracts.PreQuotes;
using Domain.PreQuotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/prequotes/{preQuoteId:guid}/draft")]
public sealed class PreQuoteDraftsController(
    CreatePreQuoteDraftService createService,
    GetPreQuoteDraftService getService,
    UpdatePreQuoteDraftService updateService,
    ApprovePreQuoteDraftService approveService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        Guid preQuoteId, CreatePreQuoteDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createService.ExecuteAsync(
            new(preQuoteId, request.SourceDocumentId,
                request.SourceStructuredExtractionId), cancellationToken);
        return result.IsSuccess
            ? StatusCode(201, Map(result.Draft!))
            : Failure(result.Failure);
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid preQuoteId, CancellationToken cancellationToken)
    {
        var result = await getService.ExecuteAsync(
            new(preQuoteId), cancellationToken);
        return result.IsSuccess ? Ok(Map(result.Draft!)) : Failure(result.Failure);
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        Guid preQuoteId, UpdatePreQuoteDraftRequest request,
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
    public async Task<IActionResult> Approve(
        Guid preQuoteId, ApprovePreQuoteDraftRequest request,
        CancellationToken cancellationToken)
    {
        var result = await approveService.ExecuteAsync(
            new(preQuoteId, request.ExpectedVersion), cancellationToken);
        return result.IsSuccess ? Ok(Map(result.Draft!)) : Failure(result.Failure);
    }

    private IActionResult Failure(PreQuoteDraftFailure failure) => failure switch
    {
        PreQuoteDraftFailure.InvalidRequest => Problem(statusCode: 400, title: "Solicitud inválida", detail: "Los datos del borrador no son válidos."),
        PreQuoteDraftFailure.Unauthorized => Problem(statusCode: 401, title: "No autorizado", detail: "No fue posible identificar al usuario autenticado."),
        PreQuoteDraftFailure.InactiveUser => Problem(statusCode: 403, title: "Usuario inactivo", detail: "El usuario no tiene acceso a esta operación."),
        PreQuoteDraftFailure.NotFound => Problem(statusCode: 404, title: "Borrador no encontrado", detail: "No fue posible encontrar la información solicitada."),
        PreQuoteDraftFailure.VersionConflict => Problem(statusCode: 409, title: "Conflicto de concurrencia", detail: "El borrador fue modificado por otro usuario. Consulte nuevamente la versión actual antes de guardar."),
        PreQuoteDraftFailure.InactiveProject => Problem(statusCode: 409, title: "Proyecto inactivo", detail: "El proyecto debe estar activo."),
        PreQuoteDraftFailure.InactiveClient => Problem(statusCode: 409, title: "Cliente inactivo", detail: "El cliente debe estar activo."),
        PreQuoteDraftFailure.DraftAlreadyExists => Problem(statusCode: 409, title: "Borrador existente", detail: "La precotización ya tiene un borrador."),
        PreQuoteDraftFailure.DraftAlreadyApproved => Problem(statusCode: 409, title: "Borrador aprobado", detail: "El borrador aprobado no puede modificarse."),
        PreQuoteDraftFailure.PendingIssues => Problem(statusCode: 409, title: "Issues pendientes", detail: "Todos los issues deben resolverse o descartarse."),
        PreQuoteDraftFailure.PendingConflicts => Problem(statusCode: 409, title: "Conflictos pendientes", detail: "Todos los conflictos deben resolverse o descartarse."),
        PreQuoteDraftFailure.InvalidDraftContent => Problem(statusCode: 409, title: "Borrador incompleto", detail: "El borrador no cumple las condiciones requeridas."),
        _ => Problem(statusCode: 500, title: "Error del borrador", detail: "No fue posible completar la operación.")
    };

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
