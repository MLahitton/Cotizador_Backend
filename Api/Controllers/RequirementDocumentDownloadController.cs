using Api.ErrorHandling;
using Application.PreQuotes.GetRequirementDetails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Api.Controllers;

[ApiController]
[Authorize]
[DocumentDownloadEndpoint]
[Route("api/v2/requirements/{requirementId:guid}/documents/{fileId:guid}/download")]
public sealed class RequirementDocumentDownloadController(DownloadRequirementDocumentService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid requirementId, Guid fileId, CancellationToken ct)
    {
        var result = await service.ExecuteAsync(requirementId, fileId, ct);
        if (result.Content is null)
            return ApiProblemDetailsFactory.Create(HttpContext, result.Status,
                result.Status == 500 ? "REQUIREMENT_DOCUMENT_UNAVAILABLE" : result.Status switch
                { 401 => "UNAUTHORIZED", 403 => "INACTIVE_USER", _ => "REQUIREMENT_DOCUMENT_NOT_FOUND" },
                "Documento no disponible", "No fue posible descargar el documento solicitado.");
        Response.RegisterForDisposeAsync(result.Content);
        return new RequirementDocumentDownloadResponse(result.Content);
    }
}

public sealed class RequirementDocumentDownloadResponse(DocumentDownloadContent content) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        await using (content)
        {
            var http = context.HttpContext;
            http.RequestAborted.ThrowIfCancellationRequested();
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(content.FileName);
            http.Response.Headers.ContentDisposition = disposition.ToString();
            http.Response.StatusCode = 200;
            http.Response.ContentType = content.ContentType;
            http.Response.ContentLength = content.Length;
            await content.Reader.CopyToAsync(http.Response.Body, http.RequestAborted);
        }
    }
}
