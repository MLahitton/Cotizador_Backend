using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Application.PreQuotes.GetStructuredDocumentExtraction;

public sealed class GetStructuredDocumentExtractionService(
    IValidator<GetStructuredDocumentExtractionQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteDocumentQueryRepository repository,
    ILogger<GetStructuredDocumentExtractionService>? logger = null)
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
                userId,
                cancellationToken);

            return details is null
                ? GetStructuredDocumentExtractionResult.Failed(
                    GetStructuredDocumentExtractionFailure.NotFound)
                : GetStructuredDocumentExtractionResult.Success(details);
        }
        catch (PreQuoteDocumentQueryException exception)
        {
            logger?.LogError(
                exception,
                "Structured extraction query failed for document {DocumentId}.",
                query.DocumentId);
            return GetStructuredDocumentExtractionResult.Failed(
                GetStructuredDocumentExtractionFailure.QueryError);
        }
    }
}
