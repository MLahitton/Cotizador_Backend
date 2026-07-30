using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using FluentValidation;
namespace Application.PreQuotes.ApprovePreQuoteDraft;
public sealed class ApprovePreQuoteDraftService(
    IValidator<ApprovePreQuoteDraftCommand> validator, ICurrentUser currentUser,
    IIdentityRepository identity, IPreQuoteDraftRepository repository,
    TimeProvider timeProvider)
{
    public async Task<ApprovePreQuoteDraftResult> ExecuteAsync(ApprovePreQuoteDraftCommand command, CancellationToken cancellationToken)
    {
        if (!(await validator.ValidateAsync(command, cancellationToken)).IsValid) return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InvalidRequest);
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId) return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        var user = await identity.FindUserByIdAsync(userId, cancellationToken);
        if (user is null) return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        if (!user.IsActive) return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveUser);
        try
        {
            var activity = await repository.FindActivityAsync(command.PreQuoteId, cancellationToken);
            var draft = await repository.FindForUpdateAsync(command.PreQuoteId, cancellationToken);
            if (activity is null || draft is null) return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.NotFound);
            if (!activity.ProjectIsActive) return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveProject);
            if (!activity.ClientIsActive) return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveClient);
            try { draft.Approve(command.ExpectedVersion, userId, timeProvider.GetUtcNow()); }
            catch (InvalidOperationException e) when (e.Message == "DRAFT_APPROVED") { return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.DraftAlreadyApproved); }
            catch (InvalidOperationException e) when (e.Message == "VERSION_CONFLICT") { return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.VersionConflict); }
            catch (InvalidOperationException e) when (e.Message == "PENDING_ISSUES") { return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.PendingIssues); }
            catch (InvalidOperationException e) when (e.Message == "PENDING_CONFLICTS") { return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.PendingConflicts); }
            catch (InvalidOperationException) { return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InvalidDraftContent); }
            await repository.SaveChangesAsync(cancellationToken);
            return ApprovePreQuoteDraftResult.Success(draft);
        }
        catch (PreQuoteDraftConcurrencyException) { return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.VersionConflict); }
        catch (PreQuoteDraftQueryException) { return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.QueryError); }
        catch (PreQuoteDraftPersistenceException) { return ApprovePreQuoteDraftResult.Failed(PreQuoteDraftFailure.PersistenceError); }
    }
}
