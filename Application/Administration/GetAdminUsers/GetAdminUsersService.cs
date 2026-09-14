using Application.Common.Abstractions.Administration;
using Application.Common.Abstractions.Authentication;
using Domain.Identity;
using FluentValidation;

namespace Application.Administration.GetAdminUsers;

public sealed class GetAdminUsersService(
    IValidator<GetAdminUsersQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IAdministrationUserReader administrationUserReader)
{
    public async Task<GetAdminUsersResult> ExecuteAsync(
        GetAdminUsersQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetAdminUsersResult.Failed(
                GetAdminUsersFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated ||
            currentUser.UserId is not Guid userId)
        {
            return GetAdminUsersResult.Failed(
                GetAdminUsersFailure.Unauthorized);
        }

        var currentDatabaseUser =
            await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);

        if (currentDatabaseUser is null)
        {
            return GetAdminUsersResult.Failed(
                GetAdminUsersFailure.Unauthorized);
        }

        if (!currentDatabaseUser.IsActive)
        {
            return GetAdminUsersResult.Failed(
                GetAdminUsersFailure.InactiveUser);
        }

        if (currentDatabaseUser.Role != UserRole.Admin)
        {
            return GetAdminUsersResult.Failed(
                GetAdminUsersFailure.Forbidden);
        }

        var search = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : query.Search.Trim();

        var normalizedStatus = string.IsNullOrWhiteSpace(query.Status)
            ? "all"
            : query.Status.Trim().ToLowerInvariant();

        bool? isActive = normalizedStatus switch
        {
            "active" => true,
            "inactive" => false,
            "all" => null,
            _ => throw new InvalidOperationException(
                "El estado de usuario validado no es reconocido.")
        };

        UserRole? role = string.IsNullOrWhiteSpace(query.Role)
            ? null
            : Enum.Parse<UserRole>(
                query.Role.Trim(),
                ignoreCase: true);

        AdministrationUserSearchPage usersPage;

        try
        {
            usersPage = await administrationUserReader.SearchAsync(
                new AdministrationUserSearchCriteria(
                    search,
                    isActive,
                    role,
                    query.Page,
                    query.PageSize),
                cancellationToken);
        }
        catch (AdministrationUserQueryException)
        {
            return GetAdminUsersResult.Failed(
                GetAdminUsersFailure.QueryError);
        }

        var totalPages = usersPage.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                usersPage.TotalCount / (double)query.PageSize);

        var items = usersPage.Items
            .Select(user => new AdminUserListItemResult(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.ProfilePictureUrl,
                user.IsActive,
                user.Role,
                user.LastLoginAtUtc,
                user.CreatedAtUtc,
                user.UpdatedAtUtc,
                user.PreQuoteCount))
            .ToArray();

        return GetAdminUsersResult.Success(
            new AdminUsersPageResult(
                items,
                query.Page,
                query.PageSize,
                usersPage.TotalCount,
                totalPages));
    }
}