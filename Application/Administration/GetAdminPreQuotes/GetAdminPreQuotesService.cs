using Application.Common.Abstractions.Administration;
using Application.Common.Abstractions.Authentication;
using Domain.Identity;
using FluentValidation;

namespace Application.Administration.GetAdminPreQuotes;

public sealed class GetAdminPreQuotesService(
    IValidator<GetAdminPreQuotesQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IAdministrationPreQuoteReader administrationPreQuoteReader)
{
    public async Task<GetAdminPreQuotesResult> ExecuteAsync(
        GetAdminPreQuotesQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetAdminPreQuotesResult.Failed(
                GetAdminPreQuotesFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated ||
            currentUser.UserId is not Guid userId)
        {
            return GetAdminPreQuotesResult.Failed(
                GetAdminPreQuotesFailure.Unauthorized);
        }

        var currentDatabaseUser =
            await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);

        if (currentDatabaseUser is null)
        {
            return GetAdminPreQuotesResult.Failed(
                GetAdminPreQuotesFailure.Unauthorized);
        }

        if (!currentDatabaseUser.IsActive)
        {
            return GetAdminPreQuotesResult.Failed(
                GetAdminPreQuotesFailure.InactiveUser);
        }

        if (currentDatabaseUser.Role != UserRole.Admin)
        {
            return GetAdminPreQuotesResult.Failed(
                GetAdminPreQuotesFailure.Forbidden);
        }

        var search = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : query.Search.Trim();

        AdministrationPreQuoteSearchPage preQuotesPage;

        try
        {
            preQuotesPage =
                await administrationPreQuoteReader.SearchAsync(
                    new AdministrationPreQuoteSearchCriteria(
                        search,
                        query.UserId,
                        query.FromUtc,
                        query.ToUtc,
                        query.Page,
                        query.PageSize),
                    cancellationToken);
        }
        catch (AdministrationPreQuoteQueryException)
        {
            return GetAdminPreQuotesResult.Failed(
                GetAdminPreQuotesFailure.QueryError);
        }

        var totalPages = preQuotesPage.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                preQuotesPage.TotalCount / (double)query.PageSize);

        var items = preQuotesPage.Items
            .Select(preQuote => new AdminPreQuoteListItemResult(
                preQuote.Id,
                preQuote.ProjectId,
                preQuote.Serial,
                preQuote.Name,
                preQuote.DocumentCount,
                preQuote.CreatedAtUtc,
                preQuote.UpdatedAtUtc,
                new AdminPreQuoteUserResult(
                    preQuote.CreatedBy.Id,
                    preQuote.CreatedBy.Email,
                    preQuote.CreatedBy.FirstName,
                    preQuote.CreatedBy.LastName),
                new AdminPreQuoteProjectResult(
                    preQuote.Project.Id,
                    preQuote.Project.Code,
                    preQuote.Project.Name),
                preQuote.HasRequirement,
                preQuote.LatestRequirementId,
                preQuote.LatestRequirementStatus,
                preQuote.HasTechnicalProposal,
                preQuote.TechnicalProposalId,
                preQuote.TechnicalProposalItemCount,
                preQuote.LatestAttemptState,
                preQuote.LatestAttemptOutcome,
                preQuote.LatestAttemptErrorCode))
            .ToArray();

        return GetAdminPreQuotesResult.Success(
            new AdminPreQuotesPageResult(
                items,
                query.Page,
                query.PageSize,
                preQuotesPage.TotalCount,
                totalPages));
    }
}