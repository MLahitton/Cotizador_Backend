using System.Net.Mail;
using Application.Common.Abstractions.Authentication;
using Google.Apis.Auth;

namespace Infrastructure.Authentication;

public sealed class GoogleTokenValidator(
    GoogleAuthenticationOptions options)
    : IGoogleTokenValidator
{
    private static readonly string[] ValidIssuers =
    [
        "accounts.google.com",
        "https://accounts.google.com"
    ];

    public async Task<GoogleIdentityData> ValidateAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var validationSettings =
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [options.ClientId]
                };

            var payload = await GoogleJsonWebSignature
                .ValidateAsync(idToken, validationSettings)
                .WaitAsync(cancellationToken);

            return MapValidatedPayload(payload);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidJwtException)
        {
            throw new GoogleTokenValidationException();
        }
        catch (ArgumentException)
        {
            throw new GoogleTokenValidationException();
        }
        catch (GoogleTokenValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new GoogleTokenValidationUnavailableException(exception);
        }
    }

    private static GoogleIdentityData MapValidatedPayload(
        GoogleJsonWebSignature.Payload payload)
    {
        var subject = NormalizeRequired(payload.Subject, 255);
        var email = NormalizeEmail(payload.Email);

        if (!payload.EmailVerified
            || !ValidIssuers.Contains(payload.Issuer, StringComparer.Ordinal))
        {
            throw new GoogleTokenValidationException();
        }

        var firstName = NormalizeOptional(payload.GivenName, 100)
            ?? NormalizeOptional(payload.Name, 100)
            ?? email[..email.IndexOf('@')];

        return new GoogleIdentityData(
            subject,
            email,
            payload.EmailVerified,
            firstName,
            NormalizeOptional(payload.FamilyName, 100),
            NormalizeOptional(payload.Picture, 2048));
    }

    private static string NormalizeEmail(string? value)
    {
        var email = NormalizeRequired(value, 320).ToLowerInvariant();

        if (!MailAddress.TryCreate(email, out var parsedEmail)
            || !string.Equals(
                parsedEmail.Address,
                email,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new GoogleTokenValidationException();
        }

        return email;
    }

    private static string NormalizeRequired(string? value, int maximumLength)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > maximumLength)
        {
            throw new GoogleTokenValidationException();
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new GoogleTokenValidationException();
        }

        return normalized;
    }
}
