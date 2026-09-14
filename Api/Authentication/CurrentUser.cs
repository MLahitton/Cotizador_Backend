using System.Security.Claims;
using Application.Common.Abstractions.Authentication;
using Domain.Identity;

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

    public UserRole? Role
    {
        get
        {
            var role = Principal?.FindFirstValue(ClaimTypes.Role);

            return Enum.TryParse<UserRole>(
                role,
                ignoreCase: true,
                out var parsedRole)
                ? parsedRole
                : null;
        }
    }

    public bool IsAdmin =>
        Role == UserRole.Admin;
}