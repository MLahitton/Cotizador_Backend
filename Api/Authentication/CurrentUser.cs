using System.Security.Claims;
using Application.Common.Abstractions.Authentication;

namespace Api.Authentication;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor)
    : ICurrentUser
{
    private ClaimsPrincipal? Principal =>
        httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var subject = Principal?.FindFirstValue("sub");

            return Guid.TryParse(subject, out var userId)
                ? userId
                : null;
        }
    }
}
