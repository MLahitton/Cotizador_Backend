using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Projects;
using Application.Common.Abstractions.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.GetPreQuoteDocuments;

public sealed class GetPreQuoteDocumentsService(
    IValidator<GetPreQuoteDocumentsQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository,
    IPreQuoteRepository preQuoteRepository,
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
            var preQuote = await preQuoteRepository.FindByIdAsync(
                query.PreQuoteId,
                cancellationToken);

            if (preQuote is null)
            {
                return GetPreQuoteDocumentsResult.Failed(
                    GetPreQuoteDocumentsFailure.NotFound);
            }

            var project = await projectRepository.FindByIdAsync(
                preQuote.ProjectId,
                cancellationToken);

            if (project is null
                || project.CreatedByUserId != userId)
            {
                return GetPreQuoteDocumentsResult.Failed(
                    GetPreQuoteDocumentsFailure.NotFound);
            }

            var documents = await repository.GetDocumentsAsync(
                query.PreQuoteId,
                query.Page,
                query.PageSize,
                cancellationToken);

            return documents is null
                ? GetPreQuoteDocumentsResult.Failed(
                    GetPreQuoteDocumentsFailure.QueryError)
                : GetPreQuoteDocumentsResult.Success(documents);
        }
        catch (PreQuoteDocumentQueryException)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.QueryError);
        }
        catch (ProjectQueryException)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.QueryError);
        }
        catch (PreQuoteQueryException)
        {
            return GetPreQuoteDocumentsResult.Failed(
                GetPreQuoteDocumentsFailure.QueryError);
        }
    }
}
