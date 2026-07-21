using Domain.Identity;

namespace Application.Common.Abstractions.Authentication;

public interface IAccessTokenGenerator
{
    AccessTokenResult Generate(User user);
}

public sealed record AccessTokenResult(
    string Token,
    DateTimeOffset ExpiresAtUtc);
