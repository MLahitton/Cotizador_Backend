using Domain.Identity;

namespace Application.Common.Abstractions.Authentication;

public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    UserRole? Role { get; }

    bool IsAdmin { get; }
}