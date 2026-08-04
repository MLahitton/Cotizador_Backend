using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using FluentValidation;
namespace Application.PreQuotes.GetPreQuoteDraft;
public sealed class GetPreQuoteDraftService(
    IValidator<GetPreQuoteDraftQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identity,
    IPreQuoteDraftRepository repository)
{
    public async Task<GetPreQuoteDraftResult> ExecuteAsync(GetPreQuoteDraftQuery query, CancellationToken cancellationToken)
    {
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid) return GetPreQuoteDraftResult.Failed(PreQuoteDraftFailure.InvalidRequest);
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId) return GetPreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        var user = await identity.FindUserByIdAsync(userId, cancellationToken);
        if (user is null) return GetPreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        if (!user.IsActive) return GetPreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveUser);
        try
        {
            var draft = await repository.FindReadAsync(
                query.PreQuoteId,
                userId,
                cancellationToken);
            return draft is null ? GetPreQuoteDraftResult.Failed(PreQuoteDraftFailure.NotFound) : GetPreQuoteDraftResult.Success(draft);
        }
        catch (PreQuoteDraftQueryException) { return GetPreQuoteDraftResult.Failed(PreQuoteDraftFailure.QueryError); }
    }
}
