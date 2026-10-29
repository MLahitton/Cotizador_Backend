using Api.ErrorHandling;
using Application.PublicExperience;
using Contracts.PublicExperience;
using Domain.PublicExperience;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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
    [ManualMultipart]
    [RequestSizeLimit(IdeaPhotoLimits.MaximumRequestBytes)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = IdeaPhotoLimits.MaximumRequestBytes,
        ValueLengthLimit = PublicExperiencePayloadValidation.MaximumPayloadBytes,
        ValueCountLimit = PublicExperiencePayloadValidation.MaximumFiles + 1)]
    [ProducesResponseType<PublicExperienceSubmissionResponse>(201)]
    [ProducesResponseType<PublicExperienceSubmissionResponse>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(413)]
    [ProducesResponseType<ProblemDetails>(415)]
    [ProducesResponseType<ProblemDetails>(422)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> Post(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > IdeaPhotoLimits.MaximumRequestBytes)
            return ApiProblemDetailsFactory.Create(HttpContext, 413, "PUBLIC_EXPERIENCE_REQUEST_TOO_LARGE",
                "Solicitud demasiado grande", "El multipart supera 121 MiB.");
        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return ApiProblemDetailsFactory.Create(HttpContext, 413, "PUBLIC_EXPERIENCE_REQUEST_TOO_LARGE",
                "Solicitud demasiado grande", "El multipart supera 121 MiB.");
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status400BadRequest)
        {
            return Invalid("Multipart invalido o incompleto.");
        }
        catch (InvalidDataException)
        {
            return Invalid("Multipart invalido o supera los limites permitidos.");
        }
        if (form.Count != 1 || !form.TryGetValue("payload", out var payload) || payload.Count != 1
            || form.Files.Any(file => file.Name is not ("files" or "ideaPhoto"))
            || form.Files.Count(file => file.Name == "ideaPhoto") > 1)
            return Invalid("Se requiere payload JSON, archivos files y como maximo una ideaPhoto.");

        var files = form.Files.Where(file => file.Name == "files").Select(file => new PublicExperienceUpload(
            file.FileName, file.ContentType, file.Length, file.OpenReadStream)).ToArray();
        var photo = form.Files.SingleOrDefault(file => file.Name == "ideaPhoto");
        var upload = photo is null ? null : new PublicExperienceUpload(photo.FileName,
            photo.ContentType, photo.Length, photo.OpenReadStream);
        var result = await service.ExecuteAsync(payload[0] ?? "", files, upload, cancellationToken);
        if (result.ErrorCode is not null)
            return ApiProblemDetailsFactory.Create(HttpContext, result.StatusCode, result.ErrorCode,
                "Fotografia invalida", result.Error!);
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

    // This action owns form parsing so size failures keep their explicit 413 mapping.
    private sealed class ManualMultipartAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
            context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
            context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
        }

        public void OnResourceExecuted(ResourceExecutedContext context) { }
    }
}
