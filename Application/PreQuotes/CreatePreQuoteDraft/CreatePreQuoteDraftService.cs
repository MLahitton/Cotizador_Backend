using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Domain.PreQuotes;
using FluentValidation;
namespace Application.PreQuotes.CreatePreQuoteDraft;
public sealed class CreatePreQuoteDraftService(
    IValidator<CreatePreQuoteDraftCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identity,
    IPreQuoteDraftRepository repository,
    ISgTechnicalSelector technicalSelector,
    TimeProvider timeProvider)
{
    public CreatePreQuoteDraftService(
        IValidator<CreatePreQuoteDraftCommand> validator,
        ICurrentUser currentUser,
        IIdentityRepository identity,
        IPreQuoteDraftRepository repository,
        TimeProvider timeProvider)
        : this(
            validator,
            currentUser,
            identity,
            repository,
            new NoopSgTechnicalSelector(),
            timeProvider)
    {
    }

    public async Task<CreatePreQuoteDraftResult> ExecuteAsync(
        CreatePreQuoteDraftCommand command, CancellationToken cancellationToken)
    {
        if (!(await validator.ValidateAsync(command, cancellationToken)).IsValid)
            return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InvalidRequest);
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        var user = await identity.FindUserByIdAsync(userId, cancellationToken);
        if (user is null) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.Unauthorized);
        if (!user.IsActive) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveUser);
        try
        {
            var source = await repository.FindSourceAsync(
                command.PreQuoteId, command.SourceDocumentId,
                command.SourceStructuredExtractionId, user.Id,
                cancellationToken);
            if (source is null) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.NotFound);
            if (await repository.ExistsAsync(
                    command.PreQuoteId, user.Id, cancellationToken))
                return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.DraftAlreadyExists);
            if (!source.ProjectIsActive) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveProject);
            if (!source.ClientIsActive) return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.InactiveClient);
            var items = await BuildTechnicalSelectionsAsync(
                source.Items,
                cancellationToken);
            var draft = PreQuoteDraft.Create(
                source.PreQuoteId, source.DocumentId, source.StructuredExtractionId,
                source.ProjectName, source.ClientName, source.Location, userId,
                timeProvider.GetUtcNow(), items, source.Requirements,
                source.DocumentReferences, source.Issues, source.Conflicts);
            repository.Add(draft);
            await repository.SaveChangesAsync(cancellationToken);
            return CreatePreQuoteDraftResult.Success(draft);
        }
        catch (PreQuoteDraftConflictException) { return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.DraftAlreadyExists); }
        catch (PreQuoteDraftQueryException) { return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.QueryError); }
        catch (PreQuoteDraftPersistenceException) { return CreatePreQuoteDraftResult.Failed(PreQuoteDraftFailure.PersistenceError); }
    }

    private async Task<IReadOnlyList<PreQuoteDraftItemSource>>
        BuildTechnicalSelectionsAsync(
            IReadOnlyList<PreQuoteDraftItemSource> items,
            CancellationToken cancellationToken)
    {
        var result = new List<PreQuoteDraftItemSource>(items.Count);
        foreach (var item in items)
        {
            var selection = await technicalSelector.SelectAsync(
                new SgTechnicalSelectionInput(
                    item.FunctionalType,
                    item.Operation,
                    item.WidthMillimeters,
                    item.HeightMillimeters,
                    item.AreaSquareMeters,
                    item.PanelCount,
                    item.MovablePanelCount,
                    item.FixedPanelCount,
                    item.Modulation,
                    item.OpeningDirection,
                    item.SpecialFeatures ?? [],
                    item.GeometryType,
                    null,
                    item.TechnicalSnapshot?.SystemOriginalText
                        ?? item.TechnicalSnapshot?.SystemCode,
                    item.Configuration),
                cancellationToken);
            result.Add(item with
            {
                TechnicalSelection = BuildSelection(item, selection)
            });
        }
        return result;
    }

    private static PreQuoteDraftItemTechnicalSelectionSource? BuildSelection(
        PreQuoteDraftItemSource item,
        SgTechnicalSelectionResult selection)
    {
        if (HasSelectedValue(item.TechnicalSelection))
        {
            return item.TechnicalSelection;
        }

        if (selection.SuggestedSystemCode is null
            && !selection.RequiresReview
            && selection.ReviewReasons.Count == 0)
        {
            return item.TechnicalSelection;
        }

        return new(
            RequestedSystemCode: item.TechnicalSnapshot?.SystemCode,
            RequestedSystemOriginalText: item.TechnicalSnapshot?.SystemOriginalText,
            SuggestedSystemCode: selection.SuggestedSystemCode,
            RequestedGlassCode: item.Glass?.NormalizedCodeSnapshot,
            RequestedGlassOriginalText: item.Glass?.RawSpecification,
            RequestedFinishCode: item.TechnicalSnapshot?.FinishCode,
            RequestedFinishOriginalText: item.TechnicalSnapshot?.FinishOriginalText,
            AppliedSystemRuleCode: selection.AppliedRuleCode,
            SelectionState: selection.SuggestedSystemCode is null
                ? PreQuoteDraftTechnicalSelectionState.Pending
                : PreQuoteDraftTechnicalSelectionState.Suggested,
            RequiresReview: selection.RequiresReview,
            Confidence: selection.Confidence == 0m ? null : selection.Confidence,
            ReviewReasons: selection.ReviewReasons,
            SuggestedSource: selection.SuggestedSystemCode is null
                ? null
                : PreQuoteDraftTechnicalSelectionSource.Rule);
    }

    private static bool HasSelectedValue(
        PreQuoteDraftItemTechnicalSelectionSource? selection) =>
        selection?.SelectedSystemCode is not null
        || selection?.SelectedGlassCode is not null
        || selection?.SelectedFinishCode is not null
        || selection?.SelectedHardwareCode is not null;

    private sealed class NoopSgTechnicalSelector : ISgTechnicalSelector
    {
        public Task<SgTechnicalSelectionResult> SelectAsync(
            SgTechnicalSelectionInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SgTechnicalSelectionResult(
                null,
                SgTechnicalSelectionRuleCodes.SystemNoMatchRequiresReview,
                0m,
                false,
                [],
                []));
    }
}
