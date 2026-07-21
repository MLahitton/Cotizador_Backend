using System.Text;
using Application.Common.Abstractions.Authentication;
using Domain.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Infrastructure.Authentication;

public sealed class JwtAccessTokenGenerator(
    JwtAuthenticationOptions options)
    : IAccessTokenGenerator
{
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private readonly SigningCredentials _signingCredentials = new(
        new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(options.SigningKey)),
        SecurityAlgorithms.HmacSha256);

    public AccessTokenResult Generate(User user)
    {
        var issuedAtUtc = DateTimeOffset.UtcNow;
        var expiresAtUtc = issuedAtUtc.AddMinutes(
            options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Email] = user.Email,
            [JwtRegisteredClaimNames.GivenName] = user.FirstName,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
        };

        if (!string.IsNullOrWhiteSpace(user.LastName))
        {
            claims[JwtRegisteredClaimNames.FamilyName] = user.LastName;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Claims = claims,
            IssuedAt = issuedAtUtc.UtcDateTime,
            NotBefore = issuedAtUtc.UtcDateTime,
            Expires = expiresAtUtc.UtcDateTime,
            SigningCredentials = _signingCredentials
        };

        return new AccessTokenResult(
            _tokenHandler.CreateToken(descriptor),
            expiresAtUtc);
    }
}
