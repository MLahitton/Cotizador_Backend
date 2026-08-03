using System.Text.Json;
using Contracts.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Api.ErrorHandling;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (context.Response.HasStarted)
        {
            logger.LogWarning(
                exception,
                "Unhandled exception received after response started. TraceId: {TraceId}",
                context.TraceIdentifier);
            return false;
        }

        logger.LogError(exception,
            "Unhandled exception in API request. TraceId: {TraceId}",
            context.TraceIdentifier);

        var (statusCode, errorCode, title, detail) = exception
            is BadHttpRequestException badRequestException
            && badRequestException.StatusCode == StatusCodes.Status413PayloadTooLarge
            ? (
                StatusCodes.Status413PayloadTooLarge,
                ApiErrorCodes.ApiPayloadTooLarge,
                "Solicitud demasiado grande.",
                "El cuerpo de la solicitud supera el tamaño permitido.")
            : (
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalServerError,
                "Error interno del servidor.",
                "Ocurrió un error inesperado al procesar la solicitud.");

        var result = ApiProblemDetailsFactory.Create(
            context,
            statusCode,
            errorCode,
            title,
            detail);

        context.Response.StatusCode = result.StatusCode!.Value;
        context.Response.ContentType = "application/problem+json";

        await ApiProblemDetailsFactory.WriteProblemDetailsAsync(
            context,
            result,
            cancellationToken);

        return true;
    }
}
