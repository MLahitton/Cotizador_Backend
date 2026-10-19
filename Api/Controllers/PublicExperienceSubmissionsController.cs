using Api.ErrorHandling;
using Application.PublicExperience;
using Contracts.PublicExperience;
using Domain.PublicExperience;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/public/experience-submissions")]
public sealed class PublicExperienceSubmissionsController(
    PublicExperienceIntakeService service,
    ProcessPublicExperienceSubmissionService processor,
    PreparePublicExperienceRequirementService preparation,
    ILogger<PublicExperienceSubmissionsController> logger) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PublicExperiencePayloadValidation.MaximumTotalBytes + 1024 * 1024)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = PublicExperiencePayloadValidation.MaximumTotalBytes + 1024 * 1024,
        ValueLengthLimit = PublicExperiencePayloadValidation.MaximumPayloadBytes,
        ValueCountLimit = PublicExperiencePayloadValidation.MaximumFiles + 1)]
    [ProducesResponseType<PublicExperienceSubmissionResponse>(201)]
    [ProducesResponseType<PublicExperienceSubmissionResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(422)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Invalid("Multipart invalido o supera los limites permitidos.");
        }
        if (form.Count != 1 || !form.TryGetValue("payload", out var payload) || payload.Count != 1
            || form.Files.Any(file => file.Name != "files"))
            return Invalid("Se requiere un campo payload JSON y archivos con nombre files.");

        var files = form.Files.Select(file => new PublicExperienceUpload(
            file.FileName, file.ContentType, file.Length, file.OpenReadStream)).ToArray();
        var result = await service.ExecuteAsync(payload[0] ?? "", files, cancellationToken);
        if (result.StatusCode == 400) return Invalid(result.Error!);
        if (result.StatusCode == 409)
            return ApiProblemDetailsFactory.Create(HttpContext, 409, "PUBLIC_EXPERIENCE_CONFLICT",
                "Submission en conflicto", result.Error!);
        PublicExperienceProcessingResult? processed;
        try
        {
            processed = await processor.ExecuteAsync(result.SubmissionId!, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Public experience HTTP processing failed. SubmissionId={SubmissionId}",
                result.SubmissionId);
            return ProcessingFailure(result.SubmissionId!, "PERSISTENCE_ERROR", 500);
        }
        if (processed is null)
            return ProcessingFailure(result.SubmissionId!, "PERSISTENCE_ERROR", 500);
        if (processed.Status == PublicExperienceSubmissionStatuses.Processing)
            return ProcessingFailure(result.SubmissionId!, "PUBLIC_EXPERIENCE_PROCESSING_IN_PROGRESS", 409);
        if (processed.Status != PublicExperienceSubmissionStatuses.Processed
            || processed.ClientId is null || processed.ProjectId is null)
        {
            var code = processed.ErrorCode ?? "PERSISTENCE_ERROR";
            var statusCode = code switch
            {
                PublicExperienceProcessingErrors.OwnerNotAvailable => 503,
                PublicExperienceRequirementPreparationErrors.DocumentsRequired => 422,
                PublicExperienceProcessingErrors.LocationInvalid => 422,
                PublicExperienceProcessingErrors.ProjectCodeConflict => 409,
                _ => 500
            };
            return ProcessingFailure(result.SubmissionId!, code, statusCode);
        }
        PublicExperienceRequirementPreparationResult? prepared;
        try
        {
            prepared = await preparation.ExecuteAsync(result.SubmissionId!, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Public experience HTTP preparation failed. SubmissionId={SubmissionId}",
                result.SubmissionId);
            return ProcessingFailure(result.SubmissionId!, "PERSISTENCE_ERROR", 500);
        }
        if (prepared is null)
            return ProcessingFailure(result.SubmissionId!, "PERSISTENCE_ERROR", 500);
        if (!prepared.IsSuccess)
        {
            var code = prepared.ErrorCode ?? "PERSISTENCE_ERROR";
            var statusCode = code switch
            {
                PublicExperienceRequirementPreparationErrors.DocumentsRequired => 422,
                PublicExperienceRequirementPreparationErrors.FileIntegrity => 500,
                _ => 500
            };
            return ProcessingFailure(result.SubmissionId!, code, statusCode);
        }
        // Public receipt ends at persisted documents. Analysis is an authenticated action.
        return StatusCode(result.StatusCode, new PublicExperienceSubmissionResponse(
            result.SubmissionId!, PublicExperienceSubmissionStatuses.Received, result.IsReplay,
            processed.ClientId, processed.ProjectId, prepared.PreQuoteId, prepared.RequirementId));
    }

    private ObjectResult ProcessingFailure(string submissionId, string code, int statusCode)
    {
        var response = ApiProblemDetailsFactory.Create(HttpContext, statusCode, code,
            "No fue posible completar el procesamiento",
            "La submission se conserva. No se pudo completar el procesamiento en esta solicitud.");
        var problem = (ProblemDetails)response.Value!;
        problem.Extensions["submissionId"] = submissionId;
        problem.Extensions["processingErrorCode"] = code;
        return response;
    }

    private ObjectResult Invalid(string detail) =>
        ApiProblemDetailsFactory.Create(HttpContext, 400, "PUBLIC_EXPERIENCE_INVALID_REQUEST",
            "Solicitud invalida", detail);
}
