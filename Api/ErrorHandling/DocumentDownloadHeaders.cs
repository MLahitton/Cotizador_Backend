namespace Api.ErrorHandling;

[AttributeUsage(AttributeTargets.Class)]
public sealed class DocumentDownloadEndpointAttribute : Attribute { }

public static class DocumentDownloadHeaders
{
    public static IApplicationBuilder UseDocumentDownloadHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<DocumentDownloadEndpointAttribute>() is not null)
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.CacheControl = "private, no-store";
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    return Task.CompletedTask;
                });
            await next(context);
        });
}
