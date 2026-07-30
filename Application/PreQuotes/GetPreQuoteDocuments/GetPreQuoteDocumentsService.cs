using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.GetPreQuoteDocuments;

public sealed class GetPreQuoteDocumentsService(
    IValidator<GetPreQuoteDocumentsQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteDocumentQueryRepository repository)
{
    public async Task<GetPreQuoteDocumentsResult> ExecuteAsync(
        GetPreQuoteDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.InactiveUser);
        }

        try
        {
            var documents = await repository.GetDocumentsAsync(
                query.PreQuoteId,
                query.Page,
                query.PageSize,
                cancellationToken);

            return documents is null
                ? GetPreQuoteDocumentsResult.Failed(
                    GetPreQuoteDocumentsFailure.NotFound)
                : GetPreQuoteDocumentsResult.Success(documents);
        }
        catch (PreQuoteDocumentQueryException)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.QueryError);
        }
    }
}
