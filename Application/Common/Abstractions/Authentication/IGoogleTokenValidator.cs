namespace Application.Common.Abstractions.Authentication;

public interface IGoogleTokenValidator
{
    Task<GoogleIdentityData> ValidateAsync(
        string idToken,
        CancellationToken cancellationToken);
}

public sealed record GoogleIdentityData(
    string Subject,
    string Email,
    bool EmailVerified,
    string FirstName,
    string? LastName,
    string? ProfilePictureUrl);

public sealed class GoogleTokenValidationException : Exception
{
    public GoogleTokenValidationException()
        : base("El token de identidad de Google no es valido.")
    {
    }
}

public sealed class GoogleTokenValidationUnavailableException : Exception
{
    public GoogleTokenValidationUnavailableException(Exception innerException)
        : base(
            "No fue posible validar el token con el proveedor externo.",
            innerException)
    {
    }
}
