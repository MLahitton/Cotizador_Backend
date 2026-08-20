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
                    x.IsIncluded,
                    x.TechnicalSelection?.Selected is null
                        ? null
                        : new PreQuoteDraftItemTechnicalSelectionEdit(
                            x.TechnicalSelection.Selected.SystemCode,
                            x.TechnicalSelection.Selected.GlassCode,
                            x.TechnicalSelection.Selected.FinishCode,
                            x.TechnicalSelection.Selected.HardwareCode,
                            x.TechnicalSelection.Selected.ConfirmSelection)))
                    .ToArray(),
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

        var itemResponses = items.Select(x => new PreQuoteDraftItemResponse(
            x.Id,
            x.Sequence,
            Origin(x.Origin),
            x.SourceStructuredItemId,
            x.SourceItemSequence,
            x.Reference,
            x.Description,
            Element(x.ElementType),
            x.RawMeasurements,
            x.WidthMillimeters,
            x.HeightMillimeters,
            x.Quantity,
            x.IsIncluded,
            MapGlassSnapshot(x.GlassSnapshot),
            MapValuationSnapshot(
                x.ValuationStatus,
                x.ValuationSnapshot),
            MapTechnicalSnapshot(x.TechnicalSnapshot)
            ,
            MapTechnicalSelection(x.TechnicalSelection)
        )).ToArray();

        var requirementResponses = requirements.Select(x => new PreQuoteDraftRequirementResponse(
            x.Id,
            x.Sequence,
            Origin(x.Origin),
            x.SourceRequirementSequence,
            Category(x.Category),
            x.Value,
            x.IsIncluded)).ToArray();

        var documentReferenceResponses = references.Select(x => new PreQuoteDraftDocumentReferenceResponse(
            x.Id,
            x.Sequence,
            Origin(x.Origin),
            x.SourceDocumentReferenceSequence,
            x.Reference,
            x.Description,
            x.Detail,
            x.Quantity,
            x.IsIncluded)).ToArray();

        var issueResponses = issues.Select(x => new PreQuoteDraftIssueResponse(
            x.Id,
            x.Sequence,
            x.SourceIssueSequence,
            Issue(x.Code),
            x.Message,
            x.ItemSequence,
            x.PageNumbers,
            Resolution(x.ResolutionStatus),
            x.ResolutionNote,
            x.ResolvedByUserId,
            x.ResolvedAtUtc)).ToArray();

        var conflictResponses = conflicts.Select(x => new PreQuoteDraftConflictResponse(
            x.Id,
            x.Sequence,
            x.SourceConflictSequence,
            Conflict(x.Code),
            x.Message,
            x.ItemSequences,
            x.PageNumbers,
            Resolution(x.ResolutionStatus),
            x.ResolutionNote,
            x.ResolvedByUserId,
            x.ResolvedAtUtc)).ToArray();

        var summary = new PreQuoteDraftSummaryResponse(
            items.Length,
            items.Count(x => x.IsIncluded),
            items.Count(x => !x.IsIncluded),
            items.Count(x => x.Origin == PreQuoteDraftOrigin.Manual),
            items.Count(x => x.IsIncluded && !x.IsCompleteForApproval),
            items.Where(x => x.IsIncluded).Sum(x => (long)(x.Quantity ?? 0)),
            requirements.Length,
            requirements.Count(x => x.IsIncluded),
            references.Length,
            references.Count(x => x.IsIncluded),
            issues.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Pending),
            issues.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Resolved),
            issues.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Dismissed),
            conflicts.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Pending),
            conflicts.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Resolved),
            conflicts.Count(x => x.ResolutionStatus == PreQuoteDraftResolutionStatus.Dismissed));

        var economicSummaryResponse = MapEconomicSummary(d);

        return new PreQuoteDraftDetailsResponse(
            d.Id,
            d.PreQuoteId,
            d.SourceDocumentId,
            d.SourceStructuredExtractionId,
            Status(d.Status),
            d.Version,
            d.ProjectName,
            d.ClientName,
            d.Location,
            d.CreatedAtUtc,
            d.UpdatedAtUtc,
            d.ApprovedAtUtc,
            itemResponses,
            requirementResponses,
            documentReferenceResponses,
            issueResponses,
            conflictResponses,
            economicSummaryResponse,
            summary,
            null);
    }

    private static PreQuoteDraftItemGlassResponse? MapGlassSnapshot(
        PreQuoteDraftItemGlassSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        return new(
            SourceStructuredItemGlassId: snapshot.SourceStructuredItemGlassId,
            GlassTypeId: snapshot.GlassTypeId,
            RawSpecification: snapshot.RawSpecification,
            NormalizedCodeSnapshot: snapshot.NormalizedCodeSnapshot,
            AssignmentScope: snapshot.AssignmentScope.ToString(),
            RequiresReview: snapshot.RequiresReview,
            ReviewReasons: snapshot.ReviewReasons
                .OrderBy(r => r.Sequence)
                .Select(r => r.Code.ToString())
                .ToArray(),
            SourcePages: snapshot.SourcePages
                .OrderBy(p => p.Sequence)
                .Select(p => p.PageNumber)
                .ToArray(),
            Evidence: snapshot.Evidence
                .OrderBy(e => e.Sequence)
                .Select(e => new PreQuoteDraftItemGlassEvidenceResponse(
                    e.Sequence, e.PageNumber, Map(e.SourceType),
                    e.Text,
                    e.SheetName,
                    e.CellRange))
                .ToArray());
    }

    private static PreQuoteDraftItemValuationResponse? MapValuationSnapshot(
        PreQuoteDraftValuationStatus status,
        PreQuoteDraftItemValuationSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        return new(
            snapshot.SourceStructuredItemValuationId,
            ValuationStatus(status),
            snapshot.Reason?.ToString(),
            snapshot.GlassTypeId,
            snapshot.GlassPriceRangeVersionId,
            snapshot.WidthMillimetersUsed,
            snapshot.HeightMillimetersUsed,
            snapshot.QuantityUsed,
            snapshot.UnitAreaSquareMeters,
            snapshot.TotalAreaSquareMeters,
            snapshot.UnitPricePerSquareMeter,
            snapshot.UnitAmount,
            snapshot.TotalAmount,
            snapshot.Currency,
            snapshot.ValuedAtUtc,
            snapshot.InvalidatedAtUtc,
            snapshot.InvalidationReason?.ToString(),
            snapshot.BillableAreaUnitSquareMeters,
            snapshot.GlassPriceRangeVersion,
            snapshot.GlassMinimumPricePerSquareMeter,
            snapshot.GlassExpectedPricePerSquareMeter,
            snapshot.GlassMaximumPricePerSquareMeter,
            snapshot.SystemCode,
            TechnicalSource(snapshot.SystemSource),
            snapshot.FrameCode,
            snapshot.FinishCode,
            snapshot.LaborProfileCode,
            snapshot.AssemblyProfileCode,
            snapshot.FinishFactorMinimum,
            snapshot.FinishFactorExpected,
            snapshot.FinishFactorMaximum,
            snapshot.AccessoryFactor,
            snapshot.GlassMinimumAmount,
            snapshot.GlassExpectedAmount,
            snapshot.GlassMaximumAmount,
            snapshot.LaborMinimumAmount,
            snapshot.LaborExpectedAmount,
            snapshot.LaborMaximumAmount,
            snapshot.AssemblyMinimumAmount,
            snapshot.AssemblyExpectedAmount,
            snapshot.AssemblyMaximumAmount,
            snapshot.AccessoriesMinimumAmount,
            snapshot.AccessoriesExpectedAmount,
            snapshot.AccessoriesMaximumAmount,
            snapshot.ItemMinimumAmount,
            snapshot.ItemExpectedAmount,
            snapshot.ItemMaximumAmount,
            snapshot.PricingProfileVersion,
            snapshot.ConfidenceScore,
            snapshot.ConfidenceLevel?.ToString().ToUpperInvariant(),
            snapshot.Assumptions,
            snapshot.MissingData,
            snapshot.RequiresReview,
            snapshot.CalculatedAtUtc);
    }

    private static PreQuoteDraftItemTechnicalSnapshotResponse? MapTechnicalSnapshot(
        PreQuoteDraftItemTechnicalSnapshot? snapshot)
    {
        if (snapshot is null) return null;
        return new(
            snapshot.SourceStructuredItemTechnicalClassificationId,
            snapshot.SystemCode,
            snapshot.SystemOriginalText,
            TechnicalSource(snapshot.SystemSource),
            snapshot.SystemConfidence,
            snapshot.FrameCode,
            snapshot.FrameOriginalText,
            TechnicalSource(snapshot.FrameSource),
            snapshot.FrameConfidence,
            snapshot.FinishCode,
            snapshot.FinishOriginalText,
            TechnicalSource(snapshot.FinishSource),
            snapshot.FinishConfidence,
            snapshot.RequiresReview,
            snapshot.ReviewReasons);
    }

    private static PreQuoteDraftItemTechnicalSelectionResponse? MapTechnicalSelection(
        PreQuoteDraftItemTechnicalSelection? selection)
    {
        if (selection is null) return null;
        return new(
            Part(selection.RequestedSystemCode, selection.RequestedSystemOriginalText,
                selection.SuggestedSystemCode, null, selection.SelectedSystemCode, null),
            Part(selection.RequestedGlassCode, selection.RequestedGlassOriginalText,
                selection.SuggestedGlassCode, null, selection.SelectedGlassCode, null),
            Part(selection.RequestedFinishCode, selection.RequestedFinishOriginalText,
                selection.SuggestedFinishCode, null, selection.SelectedFinishCode, null),
            Part(selection.RequestedHardwareCode, selection.RequestedHardwareOriginalText,
                selection.SuggestedHardwareCode, null, selection.SelectedHardwareCode, null),
            SelectionState(selection.SelectionState),
            selection.RequiresReview,
            selection.Confidence,
            selection.ReviewReasons,
            SelectionSource(selection.RequestedSource),
            SelectionSource(selection.SuggestedSource),
            SelectionSource(selection.SelectedSource));
    }

    private static PreQuoteDraftTechnicalSelectionPartResponse Part(
        string? requestedCode,
        string? requestedOriginalText,
        string? suggestedCode,
        string? suggestedOriginalText,
        string? selectedCode,
        string? selectedOriginalText) => new(
        new(requestedCode, requestedOriginalText),
        new(suggestedCode, suggestedOriginalText),
        new(selectedCode, selectedOriginalText));

    private static string SelectionState(
        PreQuoteDraftTechnicalSelectionState value) => value switch
    {
        PreQuoteDraftTechnicalSelectionState.Pending => "PENDING",
        PreQuoteDraftTechnicalSelectionState.Suggested => "SUGGESTED",
        PreQuoteDraftTechnicalSelectionState.Confirmed => "CONFIRMED",
        PreQuoteDraftTechnicalSelectionState.Modified => "MODIFIED",
        _ => throw new ArgumentException()
    };

    private static string? SelectionSource(
        PreQuoteDraftTechnicalSelectionSource? value) => value switch
    {
        PreQuoteDraftTechnicalSelectionSource.Extracted => "EXTRACTED",
        PreQuoteDraftTechnicalSelectionSource.Requested => "REQUESTED",
        PreQuoteDraftTechnicalSelectionSource.Rule => "RULE",
        PreQuoteDraftTechnicalSelectionSource.AiSuggestion => "AI_SUGGESTION",
        PreQuoteDraftTechnicalSelectionSource.Manual => "MANUAL",
        null => null,
        _ => throw new ArgumentException()
    };
    private static PreQuoteDraftEconomicSummaryResponse MapEconomicSummary(PreQuoteDraft draft) =>
        new(
            draft.EconomicSummary.IncludedItemCount,
            draft.EconomicSummary.IncludedKnownQuoteableUnitCount,
            draft.EconomicSummary.ValuedItemCount,
            draft.EconomicSummary.PendingValuationItemCount,
            draft.EconomicSummary.StaleValuationItemCount,
            draft.EconomicSummary.NotPriceableItemCount,
            draft.EconomicSummary.ItemsRequiringReviewCount,
            draft.EconomicSummary.TotalAreaSquareMeters,
            draft.EconomicSummary.GlassSubtotal,
            draft.EconomicSummary.Currency,
            draft.EconomicSummary.IsEconomicallyComplete,
            draft.EconomicSummary.MinimumTechnicalSubtotal,
            draft.EconomicSummary.ExpectedTechnicalSubtotal,
            draft.EconomicSummary.MaximumTechnicalSubtotal,
            draft.EconomicSummary.TransportMinimum,
            draft.EconomicSummary.TransportExpected,
            draft.EconomicSummary.TransportMaximum,
            draft.EconomicSummary.AdministrationMinimum,
            draft.EconomicSummary.AdministrationExpected,
            draft.EconomicSummary.AdministrationMaximum,
            draft.EconomicSummary.ContingencyMinimum,
            draft.EconomicSummary.ContingencyExpected,
            draft.EconomicSummary.ContingencyMaximum,
            draft.EconomicSummary.ProfitMinimum,
            draft.EconomicSummary.ProfitExpected,
            draft.EconomicSummary.ProfitMaximum,
            draft.EconomicSummary.VatMinimum,
            draft.EconomicSummary.VatExpected,
            draft.EconomicSummary.VatMaximum,
            draft.EconomicSummary.FinalMinimum,
            draft.EconomicSummary.FinalExpected,
            draft.EconomicSummary.FinalMaximum,
            draft.EconomicSummary.OverallConfidence,
            draft.EconomicSummary.ConfidenceLevel?.ToString().ToUpperInvariant(),
            draft.EconomicSummary.Assumptions ?? [],
            draft.EconomicSummary.MissingData ?? [],
            draft.EconomicSummary.HasLimitedPricingScope);

    private static PreQuoteDraftResolutionEdit IssueResolution(PreQuoteDraftIssueResolutionRequest x) => new(x.DraftIssueId, ResolutionStatus(x.ResolutionStatus), x.ResolutionNote);
    private static PreQuoteDraftResolutionEdit ConflictResolution(PreQuoteDraftConflictResolutionRequest x) => new(x.DraftConflictId, ResolutionStatus(x.ResolutionStatus), x.ResolutionNote);
    private static PreQuoteDraftResolutionStatus ResolutionStatus(string x) => x switch { "PENDING" => PreQuoteDraftResolutionStatus.Pending, "RESOLVED" => PreQuoteDraftResolutionStatus.Resolved, "DISMISSED" => PreQuoteDraftResolutionStatus.Dismissed, _ => throw new ArgumentException() };
    private static StructuredElementType Element(string x) => x switch { "WINDOW" => StructuredElementType.Window, "DOOR" => StructuredElementType.Door, "FACADE" => StructuredElementType.Facade, "PARTITION" => StructuredElementType.Partition, "RAILING" => StructuredElementType.Railing, "SKYLIGHT" => StructuredElementType.Skylight, "SHOWER_DIVISION" => StructuredElementType.ShowerDivision, "OTHER" => StructuredElementType.Other, _ => throw new ArgumentException() };
    private static string Element(StructuredElementType x) => x == StructuredElementType.ShowerDivision ? "SHOWER_DIVISION" : x.ToString().ToUpperInvariant();
    private static RequirementCategory Category(string x) => x switch { "GLASS_SPECIFICATION" => RequirementCategory.GlassSpecification, "PROFILE_SPECIFICATION" => RequirementCategory.ProfileSpecification, "FINISH" => RequirementCategory.Finish, "ACCESSORIES_AND_SEALANTS" => RequirementCategory.AccessoriesAndSealants, "GENERAL_NOTE" => RequirementCategory.GeneralNote, _ => throw new ArgumentException() };
    private static string Category(RequirementCategory x) => x switch { RequirementCategory.GlassSpecification => "GLASS_SPECIFICATION", RequirementCategory.ProfileSpecification => "PROFILE_SPECIFICATION", RequirementCategory.Finish => "FINISH", RequirementCategory.AccessoriesAndSealants => "ACCESSORIES_AND_SEALANTS", _ => "GENERAL_NOTE" };
    private static string Status(PreQuoteDraftStatus x) => x switch { PreQuoteDraftStatus.PendingReview => "PENDING_REVIEW", PreQuoteDraftStatus.InReview => "IN_REVIEW", _ => "APPROVED" };
    private static string Origin(PreQuoteDraftOrigin x) => x == PreQuoteDraftOrigin.Ai ? "AI" : "MANUAL";
    private static string Resolution(PreQuoteDraftResolutionStatus x) => x.ToString().ToUpperInvariant();
    private static string ValuationStatus(PreQuoteDraftValuationStatus x) => x switch
    {
        PreQuoteDraftValuationStatus.Pending => "PENDING",
        PreQuoteDraftValuationStatus.Valued => "VALUED",
        PreQuoteDraftValuationStatus.Stale => "STALE",
        PreQuoteDraftValuationStatus.RequiresReview => "REQUIRES_REVIEW",
        PreQuoteDraftValuationStatus.NotPriceable => "NOT_PRICEABLE",
        PreQuoteDraftValuationStatus.NotApplicable => "PENDING",
        _ => "PENDING"
    };
    private static string? TechnicalSource(TechnicalClassificationSource? x) => x switch
    {
        TechnicalClassificationSource.Explicit => "EXPLICIT",
        TechnicalClassificationSource.Alias => "ALIAS",
        TechnicalClassificationSource.Inferred => "INFERRED",
        TechnicalClassificationSource.Unresolved => "UNRESOLVED",
        null => null,
        _ => throw new ArgumentException()
    };
    private static string Map(EvidenceSourceType value) => value switch
    {
        EvidenceSourceType.Native => "NATIVE",
        EvidenceSourceType.Ocr => "OCR",
        EvidenceSourceType.Xlsx => "XLSX",
        _ => throw new InvalidOperationException()
    };
    private static string Issue(StructuredIssueCode x) =>
        PreQuoteDraftIssueCodeMap.MapContractCode(x);
    private static string Conflict(StructuredConflictCode x) => x switch { StructuredConflictCode.ConflictingProjectName => "CONFLICTING_PROJECT_NAME", StructuredConflictCode.ConflictingClientName => "CONFLICTING_CLIENT_NAME", StructuredConflictCode.ConflictingLocation => "CONFLICTING_LOCATION", _ => "DUPLICATE_ITEM_REFERENCE" };
}
