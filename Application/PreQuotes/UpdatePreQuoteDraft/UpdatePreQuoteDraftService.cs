using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using FluentValidation;
namespace Application.PreQuotes.UpdatePreQuoteDraft;
public sealed class UpdatePreQuoteDraftService(
    IValidator<UpdatePreQuoteDraftCommand> validator, ICurrentUser currentUser,
    IIdentityRepository identity, IPreQuoteDraftRepository repository,
    IProductSystemCatalogRepository productSystemCatalogRepository,
    IGlassTypeCatalogRepository glassTypeCatalogRepository,
    IFinishTypeCatalogRepository finishTypeCatalogRepository,
    TimeProvider timeProvider)
{
    public async Task<UpdatePreQuoteDraftResult> ExecuteAsync(UpdatePreQuoteDraftCommand command, CancellationToken cancellationToken)
    {
        if (!(await validator.ValidateAsync(command, cancellationToken)).IsValid) return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InvalidRequest);
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId) return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        var user = await identity.FindUserByIdAsync(userId, cancellationToken);
        if (user is null) return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        if (!user.IsActive) return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveUser);
        try
        {
            var draft = await repository.FindForUpdateAsync(
                command.PreQuoteId,
                userId,
                cancellationToken);
            if (draft is null) return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.NotFound);
            if (!await SelectedCodesAreValidAsync(command, cancellationToken))
                return UpdatePreQuoteDraftResult.Failed(
                    PreQuoteDraftFailure.InvalidDraftContent);
            try
            {
                draft.Update(command.ExpectedVersion, command.ProjectName,
                    command.ClientName, command.Location, command.Items,
                    command.Requirements, command.DocumentReferences,
                    command.Issues, command.Conflicts, userId,
                    timeProvider.GetUtcNow());
            }
            catch (InvalidOperationException e) when (e.Message == "VERSION_CONFLICT") { return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.VersionConflict); }
            catch (InvalidOperationException e) when (e.Message == "DRAFT_APPROVED") { return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.DraftAlreadyApproved); }
            catch (ArgumentException) { return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InvalidDraftContent); }
            await repository.SaveChangesAsync(cancellationToken);
            return UpdatePreQuoteDraftResult.Success(draft);
        }
        catch (PreQuoteDraftConcurrencyException) { return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.VersionConflict); }
        catch (PreQuoteDraftQueryException) { return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.QueryError); }
        catch (PreQuoteDraftPersistenceException) { return UpdatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.PersistenceError); }
    }

    private async Task<bool> SelectedCodesAreValidAsync(
        UpdatePreQuoteDraftCommand command,
        CancellationToken cancellationToken)
    {
        var selectedSystems = command.Items
            .Select(x => x.TechnicalSelection?.SelectedSystemCode)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var selectedGlass = command.Items
            .Select(x => x.TechnicalSelection?.SelectedGlassCode)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var selectedFinishes = command.Items
            .Select(x => x.TechnicalSelection?.SelectedFinishCode)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        try
        {
            foreach (var code in selectedSystems)
            {
                var system = await productSystemCatalogRepository
                    .FindActiveByCodeAsync(code, cancellationToken);
                if (system is null || !system.IsSelectable)
                {
                    return false;
                }
            }

            foreach (var code in selectedGlass)
            {
                if (await glassTypeCatalogRepository
                        .GetActiveByCodeWithCurrentPriceRangeAsync(
                            code,
                            cancellationToken) is null)
                {
                    return false;
                }
            }

            foreach (var code in selectedFinishes)
            {
                if (await finishTypeCatalogRepository
                        .FindActiveByCodeAsync(code, cancellationToken)
                    is null)
                {
                    return false;
                }
            }
        }
        catch (CanonicalCatalogQueryException)
        {
            return false;
        }
        catch (GlassTypeCatalogQueryException)
        {
            return false;
        }

        return true;
    }

    private static string Normalize(string value) =>
        value.Trim().ToUpperInvariant();
}
