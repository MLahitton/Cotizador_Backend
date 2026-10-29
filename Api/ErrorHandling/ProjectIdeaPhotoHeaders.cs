namespace Api.ErrorHandling;

[AttributeUsage(AttributeTargets.Class)]
public sealed class ProjectIdeaPhotoEndpointAttribute : Attribute { }

public static class ProjectIdeaPhotoHeaders
{
    public static IApplicationBuilder UseProjectIdeaPhotoHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<ProjectIdeaPhotoEndpointAttribute>() is not null)
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "private, no-store";
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    return Task.CompletedTask;
                });
            }
            await next(context);
        });
}
