using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;

namespace Application.Catalogs.GetCanonicalCatalog;

public sealed class GetCanonicalCatalogService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProductSystemCatalogRepository systems,
    IFrameTypeCatalogRepository frames,
    IFinishTypeCatalogRepository finishes,
    ICatalogAliasRepository aliases)
{
    public async Task<GetCanonicalCatalogResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetCanonicalCatalogResult.Failed(
                GetCanonicalCatalogFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);
        if (user is null)
        {
            return GetCanonicalCatalogResult.Failed(
                GetCanonicalCatalogFailure.Unauthorized);
        }
        if (!user.IsActive)
        {
            return GetCanonicalCatalogResult.Failed(
                GetCanonicalCatalogFailure.InactiveUser);
        }

        try
        {
            return GetCanonicalCatalogResult.Success(
                await systems.ListActiveAsync(cancellationToken),
                await frames.ListActiveAsync(cancellationToken),
                await finishes.ListActiveAsync(cancellationToken),
                await aliases.ListActiveAsync(cancellationToken));
        }
        catch (CanonicalCatalogQueryException)
        {
            return GetCanonicalCatalogResult.Failed(
                GetCanonicalCatalogFailure.QueryError);
        }
    }
}
