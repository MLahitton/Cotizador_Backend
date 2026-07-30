using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.GetStructuredDocumentExtraction;

public sealed class GetStructuredDocumentExtractionService(
    IValidator<GetStructuredDocumentExtractionQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteDocumentQueryRepository repository)
{
    public async Task<GetStructuredDocumentExtractionResult> ExecuteAsync(
        GetStructuredDocumentExtractionQuery query,
        CancellationToken cancellationToken)
    {
        if (!(await validator.ValidateAsync(query, cancellationToken)).IsValid)
        {
            return GetStructuredDocumentExtractionResult.Failed(
                GetStructuredDocumentExtractionFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetStructuredDocumentExtractionResult.Failed(
                GetStructuredDocumentExtractionFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetStructuredDocumentExtractionResult.Failed(
                GetStructuredDocumentExtractionFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetStructuredDocumentExtractionResult.Failed(
                GetStructuredDocumentExtractionFailure.InactiveUser);
        }

        try
        {
            var details = await repository.GetStructuredExtractionAsync(
                query.DocumentId,
                cancellationToken);

            return details is null
                ? GetStructuredDocumentExtractionResult.Failed(
                    GetStructuredDocumentExtractionFailure.NotFound)
                : GetStructuredDocumentExtractionResult.Success(details);
        }
        catch (PreQuoteDocumentQueryException)
        {
            return GetStructuredDocumentExtractionResult.Failed(
                GetStructuredDocumentExtractionFailure.QueryError);
        }
    }
}
