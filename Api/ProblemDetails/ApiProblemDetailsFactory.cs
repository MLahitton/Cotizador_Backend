using System.Diagnostics;
using System.Text.Json;
using Contracts.Common;
using Contracts.Projects;
using Contracts.PreQuotes;
using Microsoft.AspNetCore.Mvc;

namespace Api.ErrorHandling;

public static class ApiProblemDetailsFactory
{
    private static readonly JsonSerializerOptions ProblemDetailsJsonOptions = new(
        JsonSerializerDefaults.Web);

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
            Activity.Current?.Id ?? context?.TraceIdentifier ?? "00000000000000000000000000000000";
        return new ObjectResult(response)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

    public static bool IsContractualRequest(HttpContext context) =>
        context.TryGetContractualMetadata(out _)
        || IsProjectsRoute(context)
        || IsCreatePreQuoteRequest(context)
        || IsUploadDocumentRequest(context)
        || IsGetProjectPreQuotesRequest(context)
        || IsGetPreQuoteByIdRequest(context)
        || IsGetPreQuoteDocumentsRequest(context)
        || IsGetDocumentProcessingAttemptRequest(context)
        || IsGetStructuredExtractionRequest(context)
        || IsCreateDocumentProcessingAttemptRequest(context);

    public static bool IsCreatePreQuoteRequest(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && (GetSegments(context) is
            ["api", "v1", "projects", .., "prequotes"]);

    public static bool IsProjectsRoute(HttpContext context) =>
        GetSegments(context) is ["api", "v1", "projects", ..];

    public static bool IsUploadDocumentRequest(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && (GetSegments(context) is
            ["api", "v1", "prequotes", _, "documents"]);

    public static bool IsCreateDocumentProcessingAttemptRequest(
        HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && (GetSegments(context) is
            ["api", "v1", "prequote-documents", _, "processing-attempts"]);

    public static bool IsGetProjectPreQuotesRequest(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && (GetSegments(context) is ["api", "v1", "projects", _, "prequotes"]);

    public static bool IsGetPreQuoteByIdRequest(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && (GetSegments(context) is ["api", "v1", "prequotes", _]);

    public static bool IsGetPreQuoteDocumentsRequest(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && (GetSegments(context) is ["api", "v1", "prequotes", _, "documents"]);

    public static bool IsGetDocumentProcessingAttemptRequest(
        HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && (GetSegments(context) is
            ["api", "v1", "prequote-documents", _, "processing-attempts", _]);

    public static bool IsGetStructuredExtractionRequest(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && (GetSegments(context) is
            ["api", "v1", "prequote-documents", _, "structured-extraction"]);

    public static string ResolveInvalidRequestErrorCode(
        HttpContext context,
        bool fallback = true)
    {
        if (context.TryGetContractualMetadata(out var metadata))
        {
            return metadata.InvalidRequestErrorCode;
        }

        if (!fallback)
        {
            return string.Empty;
        }

        if (IsUploadDocumentRequest(context))
        {
            return DocumentErrorCodes.InvalidRequest;
        }

        if (IsGetProjectPreQuotesRequest(context))
        {
            return PreQuoteQueryErrorCodes.ListInvalidRequest;
        }

        if (IsGetPreQuoteByIdRequest(context))
        {
            return PreQuoteErrorCodes.InvalidRequest;
        }

        if (IsGetPreQuoteDocumentsRequest(context))
        {
            return PreQuoteQueryErrorCodes.DocumentsInvalidRequest;
        }

        if (IsGetDocumentProcessingAttemptRequest(context))
        {
            return DocumentProcessingAttemptErrorCodes.InvalidRequest;
        }

        if (IsGetStructuredExtractionRequest(context))
        {
            return StructuredExtractionErrorCodes.InvalidRequest;
        }

        return IsCreateDocumentProcessingAttemptRequest(context)
            ? DocumentProcessingErrorCodes.InvalidRequest
            : PreQuoteErrorCodes.InvalidRequest;
    }

    public static ContractualErrorsAttribute ResolveFallbackContractualMetadata(
        HttpContext context)
    {
        if (IsProjectsRoute(context))
        {
            return new ContractualErrorsAttribute
            {
                InvalidRequestErrorCode = ProjectErrorCodes.InvalidRequest
            };
        }

        if (IsUploadDocumentRequest(context))
        {
            return new ContractualErrorsAttribute
            {
                InvalidRequestErrorCode = DocumentErrorCodes.InvalidRequest,
                UnsupportedMediaTypeErrorCode = DocumentErrorCodes.InvalidRequest,
                PayloadTooLargeErrorCode = DocumentErrorCodes.FileTooLarge
            };
        }

        return new ContractualErrorsAttribute
        {
            InvalidRequestErrorCode = ApiErrorCodes.InternalServerError
        };
    }

    internal static async Task WriteProblemDetailsAsync(
        HttpContext context,
        ObjectResult result,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = result.StatusCode!.Value;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            result.Value,
            ProblemDetailsJsonOptions,
            "application/problem+json",
            cancellationToken);
    }

    public static async Task WriteUnauthorizedAsync(
        HttpContext context,
        string? errorCode = null)
    {
        var resolvedErrorCode = errorCode;
        if (string.IsNullOrWhiteSpace(resolvedErrorCode)
            && context.TryGetContractualMetadata(out var metadata))
        {
            resolvedErrorCode = metadata.UnauthorizedErrorCode;
        }

        resolvedErrorCode ??= PreQuoteErrorCodes.Unauthorized;

        var result = Create(
            context,
            StatusCodes.Status401Unauthorized,
            resolvedErrorCode,
            "No autorizado",
            "Se requiere autenticacion para acceder al recurso.");
        await WriteProblemDetailsAsync(context, result, context.RequestAborted);
    }

    public static async Task WriteForbiddenAsync(
        HttpContext context,
        string? errorCode = null)
    {
        var resolvedErrorCode = errorCode;
        if (string.IsNullOrWhiteSpace(resolvedErrorCode)
            && context.TryGetContractualMetadata(out var metadata))
        {
            resolvedErrorCode = metadata.ForbiddenErrorCode;
        }

        resolvedErrorCode ??= ApiErrorCodes.AuthForbidden;

        var result = Create(
            context,
            StatusCodes.Status403Forbidden,
            resolvedErrorCode,
            "No autorizado",
            "No tienes permisos para ejecutar esta accion.");
        await WriteProblemDetailsAsync(context, result, context.RequestAborted);
    }

    private static string[]? GetSegments(HttpContext context) =>
        context.Request.Path.Value?.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
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
                        ApiProblemDetailsFactory.ResolveInvalidRequestErrorCode(
                            context.HttpContext,
                            fallback: false),
                        "Solicitud invalida",
                        "La solicitud no tiene un formato valido.")
                    : fallback(context);
        });
        return services;
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

            var hasMetadata = context.TryGetContractualMetadata(out var metadata);
            var isContractual = hasMetadata
                || ApiProblemDetailsFactory.IsContractualRequest(context);
            var resolvedMetadata = hasMetadata
                ? metadata
                : ApiProblemDetailsFactory.ResolveFallbackContractualMetadata(context);

            if (!isContractual || context.Response.HasStarted)
            {
                return;
            }

            var allowHeader = context.Response.Headers.TryGetValue(
                "Allow",
                out var allowValues)
                ? allowValues.ToString()
                : null;

            async Task WriteAsync(ObjectResult result)
            {
                if (!string.IsNullOrWhiteSpace(allowHeader))
                {
                    context.Response.Headers["Allow"] = allowHeader;
                }

                await ApiProblemDetailsFactory.WriteProblemDetailsAsync(
                    context,
                    result,
                    context.RequestAborted);
            }

            if (context.Response.StatusCode
                == StatusCodes.Status415UnsupportedMediaType)
            {
                var resolvedUnsupportedErrorCode =
                    string.IsNullOrWhiteSpace(
                        resolvedMetadata.UnsupportedMediaTypeErrorCode)
                        || resolvedMetadata.UnsupportedMediaTypeErrorCode
                            == DocumentErrorCodes.InvalidRequest
                        ? ApiErrorCodes.ApiUnsupportedMediaType
                        : resolvedMetadata.UnsupportedMediaTypeErrorCode;

                var result = ApiProblemDetailsFactory.Create(
                    context,
                    StatusCodes.Status415UnsupportedMediaType,
                    string.IsNullOrWhiteSpace(
                        resolvedUnsupportedErrorCode)
                        ? ApiErrorCodes.ApiUnsupportedMediaType
                        : resolvedUnsupportedErrorCode,
                    "Solicitud invalida",
                    "La solicitud no usa un tipo de contenido valido.");
                await WriteAsync(result);
            }
            else if (context.Response.StatusCode
                == StatusCodes.Status405MethodNotAllowed)
            {
                var result = ApiProblemDetailsFactory.Create(
                    context,
                    StatusCodes.Status405MethodNotAllowed,
                    string.IsNullOrWhiteSpace(
                        resolvedMetadata.MethodNotAllowedErrorCode)
                        ? ApiErrorCodes.ApiMethodNotAllowed
                        : resolvedMetadata.MethodNotAllowedErrorCode,
                    "Metodo no permitido",
                    "El metodo no es valido para este recurso.");
                await WriteAsync(result);
            }
            else if (context.Response.StatusCode
                == StatusCodes.Status413PayloadTooLarge)
            {
                var result = ApiProblemDetailsFactory.Create(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    string.IsNullOrWhiteSpace(
                        resolvedMetadata.PayloadTooLargeErrorCode)
                        ? ApiErrorCodes.ApiPayloadTooLarge
                        : resolvedMetadata.PayloadTooLargeErrorCode,
                    "Solicitud demasiado grande",
                    "El cuerpo de la solicitud supera el tamaño permitido.");
                await WriteAsync(result);
            }
        });
    }
}
