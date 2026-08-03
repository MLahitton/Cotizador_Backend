using System.Diagnostics.CodeAnalysis;
using Contracts.Common;
using Microsoft.AspNetCore.Http;

namespace Api.ErrorHandling;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class ContractualErrorsAttribute : Attribute
{
    public required string InvalidRequestErrorCode { get; init; }

    [StringSyntax(StringSyntaxAttribute.Regex)]
    public string UnauthorizedErrorCode { get; init; } =
        ApiErrorCodes.AuthUnauthorized;

    [StringSyntax(StringSyntaxAttribute.Regex)]
    public string? ForbiddenErrorCode { get; init; } =
        ApiErrorCodes.AuthForbidden;

    public string UnsupportedMediaTypeErrorCode { get; init; } =
        ApiErrorCodes.ApiUnsupportedMediaType;

    public string MethodNotAllowedErrorCode { get; init; } =
        ApiErrorCodes.ApiMethodNotAllowed;

    public string PayloadTooLargeErrorCode { get; init; } =
        ApiErrorCodes.ApiPayloadTooLarge;

    public string InternalServerErrorErrorCode { get; init; } =
        ApiErrorCodes.InternalServerError;

    public bool IsContractual { get; init; } = true;

    public string? RouteNotFoundErrorCode { get; init; } =
        ApiErrorCodes.ApiRouteNotFound;
}

public static class ContractualErrorsHttpContextExtensions
{
    public static bool TryGetContractualMetadata(
        this HttpContext context,
        out ContractualErrorsAttribute metadata)
    {
        var candidate = context.GetEndpoint()?.Metadata
            .GetMetadata<ContractualErrorsAttribute>();

        if (candidate is { IsContractual: true })
        {
            metadata = candidate;
            return true;
        }

        metadata = new ContractualErrorsAttribute
        {
            InvalidRequestErrorCode = ApiErrorCodes.InternalServerError
        };
        return false;
    }
}
