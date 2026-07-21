using Application.Common.Abstractions.Authentication;

namespace Application.Authentication.GetCurrentUser;

public sealed class GetCurrentUserService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository)
{
    public async Task<GetCurrentUserResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return GetCurrentUserResult.Failed(
                GetCurrentUserFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetCurrentUserResult.Failed(
                GetCurrentUserFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetCurrentUserResult.Failed(
                GetCurrentUserFailure.InactiveUser);
        }

        return GetCurrentUserResult.Success(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.ProfilePictureUrl,
            user.IsActive);
    }
}
