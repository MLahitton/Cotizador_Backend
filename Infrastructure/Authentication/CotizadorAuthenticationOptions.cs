using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Authentication;

public sealed record GoogleAuthenticationOptions(string ClientId);

public sealed record JwtAuthenticationOptions(
    string Issuer,
    string Audience,
    string SigningKey,
    int AccessTokenMinutes);

public sealed record CotizadorAuthenticationOptions(
    GoogleAuthenticationOptions Google,
    JwtAuthenticationOptions Jwt)
{
    private const int MinimumSigningKeyBytes = 32;

    public static CotizadorAuthenticationOptions FromConfiguration(
        IConfiguration configuration)
    {
        var googleClientId = GetRequiredValue(
            configuration,
            "Authentication:Google:ClientId");
        var issuer = GetRequiredValue(
            configuration,
            "Authentication:Jwt:Issuer");
        var audience = GetRequiredValue(
            configuration,
            "Authentication:Jwt:Audience");
        var signingKey = GetRequiredValue(
            configuration,
            "Authentication:Jwt:SigningKey");
        var accessTokenMinutesValue = GetRequiredValue(
            configuration,
            "Authentication:Jwt:AccessTokenMinutes");

        if (Encoding.UTF8.GetByteCount(signingKey) < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                "La configuracion 'Authentication:Jwt:SigningKey' debe contener al menos 32 bytes UTF-8.");
        }

        if (!int.TryParse(
                accessTokenMinutesValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var accessTokenMinutes)
            || accessTokenMinutes <= 0)
        {
            throw new InvalidOperationException(
                "La configuracion 'Authentication:Jwt:AccessTokenMinutes' debe ser un entero mayor que cero.");
        }

        return new CotizadorAuthenticationOptions(
            new GoogleAuthenticationOptions(googleClientId),
            new JwtAuthenticationOptions(
                issuer,
                audience,
                signingKey,
                accessTokenMinutes));
    }

    private static string GetRequiredValue(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"La configuracion '{key}' es obligatoria.");
        }

        return value.Trim();
    }
}
