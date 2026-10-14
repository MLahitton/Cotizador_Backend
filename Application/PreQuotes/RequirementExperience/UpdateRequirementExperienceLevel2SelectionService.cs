using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.Identity;
using Domain.PreQuotes;

namespace Application.PreQuotes.RequirementExperience;

public sealed class UpdateRequirementExperienceLevel2SelectionService(
    IRequirementRepository requirementRepository,
    IIdentityRepository identityRepository,
    ICurrentUser currentUser,
    IProductSystemCatalogRepository productSystemCatalogRepository,
    IGlassTypeCatalogRepository glassTypeCatalogRepository,
    RequirementExperienceLevel2Resolver resolver,
    TimeProvider timeProvider)
{
    public Task<RequirementExperienceResult<RequirementExperienceLevel2Response>> UpdateSystemAsync(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        UpdateRequirementExperienceLevel2SystemRequest request,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            technicalProposalId,
            technicalProposalItemId,
            request.SystemId,
            request.ExpectedRevision,
            SelectionKind.System,
            cancellationToken);

    public Task<RequirementExperienceResult<RequirementExperienceLevel2Response>> UpdateGlassAsync(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        UpdateRequirementExperienceLevel2GlassRequest request,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            technicalProposalId,
            technicalProposalItemId,
            request.GlassId,
            request.ExpectedRevision,
            SelectionKind.Glass,
            cancellationToken);

    private async Task<RequirementExperienceResult<RequirementExperienceLevel2Response>> UpdateAsync(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        Guid? selectedId,
        long? expectedRevision,
        SelectionKind kind,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(
            technicalProposalId,
            technicalProposalItemId,
            cancellationToken);
        if (!context.IsSuccess)
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(context.Error);
        }

        var value = context.Value!;
        var currentReadModel = await ResolveAsync(value, cancellationToken);
        if (selectedId is not null
            && !IsAllowed(currentReadModel, selectedId.Value, kind))
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                RequirementExperienceFailure.CandidateNotAllowed);
        }

        var currentRevision = value.TechnicalSelection?.Revision ?? 0;
        if ((value.TechnicalSelection is not null && expectedRevision is null)
            || (expectedRevision is not null && expectedRevision.Value != currentRevision))
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                RequirementExperienceFailure.Conflict);
        }

        var changed = ApplySelection(value, selectedId, kind);
        if (changed)
        {
            try
            {
                await requirementRepository.SaveChangesAsync(cancellationToken);
            }
            catch (RequirementExperienceConflictException)
            {
                return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                    RequirementExperienceFailure.Conflict);
            }
            catch (RequirementPersistenceException)
            {
                return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                    RequirementExperienceFailure.PersistenceError);
            }
        }

        return RequirementExperienceResult<RequirementExperienceLevel2Response>.Success(
            await ResolveAsync(value, cancellationToken));
    }

    private async Task<RequirementExperienceResult<SelectionContext>> LoadContextAsync(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return RequirementExperienceResult<SelectionContext>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);
        if (user is null)
        {
            return RequirementExperienceResult<SelectionContext>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return RequirementExperienceResult<SelectionContext>.Failed(
                RequirementExperienceFailure.InactiveUser);
        }

        var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
            technicalProposalId,
            cancellationToken);
        if (proposal is null)
        {
            return RequirementExperienceResult<SelectionContext>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        if (!CanAccess(proposal.Requirement.PreQuote.Project.CreatedByUserId, user))
        {
            return RequirementExperienceResult<SelectionContext>.Failed(
                RequirementExperienceFailure.Forbidden);
        }

        var item = proposal.Items.FirstOrDefault(value => value.Id == technicalProposalItemId);
        if (item is null)
        {
            return RequirementExperienceResult<SelectionContext>.Failed(
                RequirementExperienceFailure.ItemNotFound);
        }

        var drafts = await requirementRepository.ListExperienceDraftsByTechnicalProposalIdAsync(
            technicalProposalId,
            cancellationToken);
        var draft = drafts.FirstOrDefault(value => value.TechnicalProposalItemId == technicalProposalItemId);
        var level1 = draft is null
            ? null
            : GetRequirementExperienceDraftsService.MapDraft(technicalProposalItemId, draft).Level1Resolution;
        var technicalSelection =
            await requirementRepository.FindExperienceTechnicalSelectionForUpdateAsync(
                technicalProposalItemId,
                cancellationToken);
        var systems = await productSystemCatalogRepository.ListActiveSelectableAsync(cancellationToken);
        var glasses = await glassTypeCatalogRepository.GetActiveWithCurrentPriceRangesAsync(cancellationToken);

        return RequirementExperienceResult<SelectionContext>.Success(
            new SelectionContext(
                proposal,
                item,
                level1,
                technicalSelection,
                systems,
                glasses,
                user));
    }

    private bool ApplySelection(
        SelectionContext context,
        Guid? selectedId,
        SelectionKind kind)
    {
        if (context.TechnicalSelection is null)
        {
            if (selectedId is null)
            {
                return false;
            }

            var selection = kind == SelectionKind.System
                ? RequirementItemExperienceTechnicalSelection.Create(
                    context.Proposal.Id,
                    context.Item.Id,
                    selectedId,
                    glassTypeId: null,
                    context.User.Id,
                    timeProvider.GetUtcNow())
                : RequirementItemExperienceTechnicalSelection.Create(
                    context.Proposal.Id,
                    context.Item.Id,
                    systemId: null,
                    glassTypeId: selectedId,
                    context.User.Id,
                    timeProvider.GetUtcNow());

            requirementRepository.AddExperienceTechnicalSelection(selection);
            context.TechnicalSelection = selection;
            return true;
        }

        return kind == SelectionKind.System
            ? context.TechnicalSelection.ReplaceSystem(
                selectedId,
                context.User.Id,
                timeProvider.GetUtcNow())
            : context.TechnicalSelection.ReplaceGlass(
                selectedId,
                context.User.Id,
                timeProvider.GetUtcNow());
    }

    private Task<RequirementExperienceLevel2Response> ResolveAsync(
        SelectionContext context,
        CancellationToken cancellationToken) =>
        resolver.ResolveAsync(
            context.Proposal,
            context.Item,
            context.Level1,
            context.TechnicalSelection,
            context.Systems,
            context.Glasses,
            cancellationToken);

    private static bool IsAllowed(
        RequirementExperienceLevel2Response readModel,
        Guid selectedId,
        SelectionKind kind)
    {
        return kind == SelectionKind.System
            ? IsAllowed(readModel.System.Candidates, selectedId)
            : IsAllowed(readModel.Glass.Candidates, selectedId);
    }

    private static bool IsAllowed<TCandidate>(
        IReadOnlyList<TCandidate> candidates,
        Guid selectedId)
        where TCandidate : IRequirementExperienceLevel2Candidate
    {
        var candidate = candidates.FirstOrDefault(value => value.Id == selectedId);
        return candidate is not null
            && candidate.CompatibilityState != RequirementExperienceLevel2CandidateStates.Incompatible;
    }

    private static bool CanAccess(Guid projectOwnerUserId, User user)
    {
        return user.Id == projectOwnerUserId;
    }

    private enum SelectionKind
    {
        System,
        Glass
    }

    private sealed record SelectionContext(
        RequirementTechnicalProposal Proposal,
        RequirementTechnicalProposalItem Item,
        RequirementExperienceLevel1ResolutionResponse? Level1,
        RequirementItemExperienceTechnicalSelection? TechnicalSelection,
        IReadOnlyList<ProductSystemCatalogReadModel> Systems,
        IReadOnlyList<GlassTypeCatalogReadModel> Glasses,
        User User)
    {
        public RequirementItemExperienceTechnicalSelection? TechnicalSelection { get; set; } =
            TechnicalSelection;
    }
}
