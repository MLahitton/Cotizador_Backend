using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.GetPreQuoteById;

public sealed class GetPreQuoteByIdService(
    IValidator<GetPreQuoteByIdQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IPreQuoteRepository preQuoteRepository)
{
    public async Task<GetPreQuoteByIdResult> ExecuteAsync(
        GetPreQuoteByIdQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetPreQuoteByIdResult.Failed(
                GetPreQuoteByIdFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetPreQuoteByIdResult.Failed(
                GetPreQuoteByIdFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetPreQuoteByIdResult.Failed(
                GetPreQuoteByIdFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetPreQuoteByIdResult.Failed(
                GetPreQuoteByIdFailure.InactiveUser);
        }

        PreQuoteDetails? preQuote;

        try
        {
            preQuote = await preQuoteRepository.FindByIdAsync(
                query.PreQuoteId,
                cancellationToken);
        }
        catch (PreQuoteQueryException)
        {
            return GetPreQuoteByIdResult.Failed(
                GetPreQuoteByIdFailure.QueryError);
        }

        if (preQuote is null)
        {
            return GetPreQuoteByIdResult.Failed(
                GetPreQuoteByIdFailure.NotFound);
        }

        return GetPreQuoteByIdResult.Success(
            new PreQuoteDetailsResult(
                preQuote.Id,
                preQuote.ProjectId,
                preQuote.DocumentCount,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc));
    }
}
