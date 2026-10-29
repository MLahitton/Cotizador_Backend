using Api.ErrorHandling;
using Application.Projects.GetProjectIdeaPhoto;
using Contracts.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Authorize]
[ProjectIdeaPhotoEndpoint]
[ContractualErrors(InvalidRequestErrorCode = ProjectErrorCodes.InvalidRequest)]
[Route("api/v2/projects/{projectId:guid}/idea-photo")]
public sealed class ProjectIdeaPhotoController(GetProjectIdeaPhotoService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await service.ExecuteAsync(projectId, cancellationToken);
        if (result.Failure != ProjectIdeaPhotoFailure.None)
        {
            var (status, code) = result.Failure switch
            {
                ProjectIdeaPhotoFailure.Unauthorized => (401, ProjectErrorCodes.Unauthorized),
                ProjectIdeaPhotoFailure.InactiveUser => (403, ProjectErrorCodes.InactiveUser),
                ProjectIdeaPhotoFailure.NotFound => (404, ProjectErrorCodes.ProjectNotFound),
                _ => (500, "PROJECT_IDEA_PHOTO_UNAVAILABLE")
            };
            return ApiProblemDetailsFactory.Create(HttpContext, status, code,
                "No fue posible consultar la fotografia", "La fotografia no esta disponible para esta solicitud.");
        }
        if (result.Content is null) return NoContent();
        Response.RegisterForDisposeAsync(result.Content);
        return new ProjectIdeaPhotoResponse(result.Content);
    }
}

public sealed class ProjectIdeaPhotoResponse(ProjectIdeaPhotoContent content) : IActionResult
{
    public async Task ExecuteResultAsync(ActionContext context)
    {
        await using (content)
        {
            var http = context.HttpContext;
            http.RequestAborted.ThrowIfCancellationRequested();
            http.Response.StatusCode = StatusCodes.Status200OK;
            http.Response.ContentType = content.ContentType;
            http.Response.ContentLength = content.Length;
            http.Response.Headers.ContentDisposition = "inline";
            // No ranges, ETag, redirects or reopen: drain this verified reader before disposal.
            await content.Reader.CopyToAsync(http.Response.Body, http.RequestAborted);
        }
    }
}
