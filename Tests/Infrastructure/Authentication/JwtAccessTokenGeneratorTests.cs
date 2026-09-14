using Domain.Identity;
using Infrastructure.Authentication;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Authentication;

public sealed class JwtAccessTokenGeneratorTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Generate_UsesConfiguredEightHourAccessTokenLifetime()
    {
        var options = new JwtAuthenticationOptions(
            "steel-and-glass-tests",
            "steel-and-glass-frontend-tests",
            "abcdefghijklmnopqrstuvwxyz1234567890-test-signing-key",
            480);
        var generator = new JwtAccessTokenGenerator(options);
        var user = User.CreateFromGoogle(
            "admin@example.com",
            "Admin",
            "User",
            null,
            CreatedAt);

        var before = DateTimeOffset.UtcNow;
        var result = generator.Generate(user);
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(
            result.ExpiresAtUtc,
            before.AddHours(8).AddSeconds(-1),
            after.AddHours(8).AddSeconds(1));

        var token = new JsonWebTokenHandler().ReadJsonWebToken(
            result.Token);
        var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(
            token.GetPayloadValue<long>(JwtRegisteredClaimNames.Exp));

        Assert.InRange(
            expiresAtUtc,
            before.AddHours(8).AddSeconds(-1),
            after.AddHours(8).AddSeconds(1));
    }
}