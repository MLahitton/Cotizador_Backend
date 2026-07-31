using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;

namespace Application.Catalogs.GetGlassTypesCatalog;

public sealed class GetGlassTypesCatalogService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IGlassTypeCatalogRepository repository)
{
    public async Task<GetGlassTypesCatalogResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetGlassTypesCatalogResult.Failed(
                GetGlassTypesCatalogFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);
        if (user is null)
        {
            return GetGlassTypesCatalogResult.Failed(
                GetGlassTypesCatalogFailure.Unauthorized);
        }
        if (!user.IsActive)
        {
            return GetGlassTypesCatalogResult.Failed(
                GetGlassTypesCatalogFailure.InactiveUser);
        }

        try
        {
            var items = await repository
                .GetActiveWithCurrentPriceRangesAsync(cancellationToken);
            return GetGlassTypesCatalogResult.Success(
                items.Where(item =>
                        item.IsActive
                        && item.CurrentPriceRange is { ValidToUtc: null })
                    .OrderBy(item => item.Code, StringComparer.Ordinal)
                    .ToArray());
        }
        catch (GlassTypeCatalogQueryException)
        {
            return GetGlassTypesCatalogResult.Failed(
                GetGlassTypesCatalogFailure.QueryError);
        }
    }
}
