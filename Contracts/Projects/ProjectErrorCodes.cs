namespace Contracts.Projects;

public static class ProjectErrorCodes
{
    public const string InvalidRequest = "PROJECT_INVALID_REQUEST";
    public const string Unauthorized = "AUTH_UNAUTHORIZED";
    public const string InactiveUser = "AUTH_USER_INACTIVE";
    public const string ClientNotFound = "PROJECT_CLIENT_NOT_FOUND";
    public const string ClientInactive = "PROJECT_CLIENT_INACTIVE";
    public const string ProjectNotFound = "PROJECT_NOT_FOUND";
    public const string DuplicateCode = "PROJECT_CODE_DUPLICATE";
    public const string QueryError = "PROJECT_QUERY_ERROR";
    public const string PersistenceError = "PROJECT_PERSISTENCE_ERROR";
}
