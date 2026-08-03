using System.Diagnostics;
using System.Text.Json;
using Contracts.Common;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Api.ErrorHandling;

public static class ApiProblemDetailsFactory
{
    public static ObjectResult Create(
        HttpContext context,
        int status,
        string errorCode,
        string title,
        string detail)
    {
        var response = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Type = $"urn:cotizador:error:{errorCode.ToLowerInvariant()}",
            Title = title,
            Status = status,
            Detail = detail
        };
        response.Extensions["errorCode"] = errorCode;
        response.Extensions["traceId"] =
            Activity.Current?.Id ?? context.TraceIdentifier;
        return new ObjectResult(response)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

    public static bool IsCreatePreQuoteRequest(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return false;
        }

        var segments = context.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments is ["api", "v1", "projects", _, "prequotes"];
    }

    public static bool IsUploadDocumentRequest(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return false;
        }

        var segments = context.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments is ["api", "v1", "prequotes", _, "documents"];
    }

    public static bool IsCreateDocumentProcessingAttemptRequest(
        HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return false;
        }

        var segments = context.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments is
        [
            "api",
            "v1",
            "prequote-documents",
            _,
            "processing-attempts"
        ];
    }

    public static bool IsContractualRequest(HttpContext context) =>
        IsCreatePreQuoteRequest(context)
        || IsUploadDocumentRequest(context)
        || IsCreateDocumentProcessingAttemptRequest(context);

    public static async Task WriteUnauthorizedAsync(HttpContext context)
    {
        var result = Create(
            context,
            StatusCodes.Status401Unauthorized,
            PreQuoteErrorCodes.Unauthorized,
            "No autorizado",
            "Se requiere autenticacion para acceder al recurso.");
        context.Response.StatusCode = result.StatusCode!.Value;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            result.Value,
            result.Value!.GetType(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            context.RequestAborted);
    }
}

public static class ApiProblemDetailsServiceCollectionExtensions
{
    public static IServiceCollection AddPreQuoteProblemDetailsContract(
        this IServiceCollection services)
    {
        services.PostConfigure<ApiBehaviorOptions>(options =>
        {
            var fallback = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context =>
                ApiProblemDetailsFactory.IsContractualRequest(
                    context.HttpContext)
                    ? ApiProblemDetailsFactory.Create(
                        context.HttpContext,
                        StatusCodes.Status400BadRequest,
                        ResolveInvalidRequestErrorCode(context.HttpContext),
                        "Solicitud invalida",
                        "La solicitud no tiene un formato valido.")
                    : fallback(context);
        });
        return services;
    }

    private static string ResolveInvalidRequestErrorCode(
        HttpContext context)
    {
        if (ApiProblemDetailsFactory.IsUploadDocumentRequest(context))
        {
            return DocumentErrorCodes.InvalidRequest;
        }

        return ApiProblemDetailsFactory
            .IsCreateDocumentProcessingAttemptRequest(context)
            ? DocumentProcessingErrorCodes.InvalidRequest
            : PreQuoteErrorCodes.InvalidRequest;
    }
}

public static class ApiProblemDetailsApplicationBuilderExtensions
{
    public static IApplicationBuilder UseContractualProblemDetails(
        this IApplicationBuilder application)
    {
        return application.Use(async (context, next) =>
        {
            await next(context);
            if (ApiProblemDetailsFactory.IsUploadDocumentRequest(context)
                && context.Response.StatusCode
                    == StatusCodes.Status415UnsupportedMediaType
                && !context.Response.HasStarted)
            {
                context.Response.Clear();
                var result = ApiProblemDetailsFactory.Create(
                    context,
                    StatusCodes.Status400BadRequest,
                    DocumentErrorCodes.InvalidRequest,
                    "Solicitud invalida",
                    "La solicitud debe usar multipart/form-data.");
                context.Response.StatusCode = result.StatusCode!.Value;
                context.Response.ContentType = "application/problem+json";
                await JsonSerializer.SerializeAsync(
                    context.Response.Body,
                    result.Value,
                    result.Value!.GetType(),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    context.RequestAborted);
            }
        });
    }
}
