using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using FluentValidation;

namespace Application.PreQuotes.GetProjectPreQuotes;

public sealed class GetProjectPreQuotesService(
    IValidator<GetProjectPreQuotesQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IProjectRepository projectRepository,
    IPreQuoteRepository preQuoteRepository)
{
    public async Task<GetProjectPreQuotesResult> ExecuteAsync(
        GetProjectPreQuotesQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetProjectPreQuotesResult.Failed(
                GetProjectPreQuotesFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetProjectPreQuotesResult.Failed(
                GetProjectPreQuotesFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetProjectPreQuotesResult.Failed(
                GetProjectPreQuotesFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetProjectPreQuotesResult.Failed(
                GetProjectPreQuotesFailure.InactiveUser);
        }

        Domain.Projects.Project? project;

        try
        {
            project = await projectRepository.FindByIdAsync(
                query.ProjectId,
                cancellationToken);
        }
        catch (ProjectQueryException)
        {
            return GetProjectPreQuotesResult.Failed(
                GetProjectPreQuotesFailure.QueryError);
        }

        if (project is null)
        {
            return GetProjectPreQuotesResult.Failed(
                GetProjectPreQuotesFailure.ProjectNotFound);
        }

        PreQuoteSearchPage preQuotesPage;

        try
        {
            preQuotesPage =
                await preQuoteRepository.SearchByProjectAsync(
                    project.Id,
                    query.Page,
                    query.PageSize,
                    cancellationToken);
        }
        catch (PreQuoteQueryException)
        {
            return GetProjectPreQuotesResult.Failed(
                GetProjectPreQuotesFailure.QueryError);
        }

        var totalPages = preQuotesPage.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                preQuotesPage.TotalCount / (double)query.PageSize);

        var items = preQuotesPage.Items
            .Select(preQuote => new PreQuoteListItemResult(
                preQuote.Id,
                preQuote.ProjectId,
                preQuote.DocumentCount,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc))
            .ToArray();

        return GetProjectPreQuotesResult.Success(
            new ProjectPreQuotesPageResult(
                items,
                query.Page,
                query.PageSize,
                preQuotesPage.TotalCount,
                totalPages));
    }
}
