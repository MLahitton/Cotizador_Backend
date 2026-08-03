namespace Contracts.Common;

public static class ApiErrorCodes
{
    public const string AuthUnauthorized = "AUTH_UNAUTHORIZED";
    public const string AuthForbidden = "AUTH_FORBIDDEN";
    public const string InternalServerError = "INTERNAL_SERVER_ERROR";
    public const string ApiUnsupportedMediaType = "API_UNSUPPORTED_MEDIA_TYPE";
    public const string ApiMethodNotAllowed = "API_METHOD_NOT_ALLOWED";
    public const string ApiPayloadTooLarge = "API_PAYLOAD_TOO_LARGE";
    public const string ApiRouteNotFound = "API_ROUTE_NOT_FOUND";
}
