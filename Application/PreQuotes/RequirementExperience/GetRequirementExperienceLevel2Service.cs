using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.Identity;

namespace Application.PreQuotes.RequirementExperience;

public sealed class GetRequirementExperienceLevel2Service(
    IRequirementRepository requirementRepository,
    IIdentityRepository identityRepository,
    ICurrentUser currentUser,
    IProductSystemCatalogRepository productSystemCatalogRepository,
    IGlassTypeCatalogRepository glassTypeCatalogRepository,
    RequirementExperienceLevel2Resolver resolver)
{
    public async Task<RequirementExperienceResult<RequirementExperienceLevel2Response>> ExecuteAsync(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);
        if (user is null)
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                RequirementExperienceFailure.InactiveUser);
        }

        var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
            technicalProposalId,
            cancellationToken);
        if (proposal is null)
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        if (!CanAccess(proposal.Requirement.PreQuote.Project.CreatedByUserId, user))
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        var item = proposal.Items.FirstOrDefault(value => value.Id == technicalProposalItemId);
        if (item is null)
        {
            return RequirementExperienceResult<RequirementExperienceLevel2Response>.Failed(
                RequirementExperienceFailure.ItemNotFound);
        }

        var drafts = await requirementRepository.ListExperienceDraftsByTechnicalProposalIdAsync(
            technicalProposalId,
            cancellationToken);
        var draft = drafts.FirstOrDefault(value => value.TechnicalProposalItemId == technicalProposalItemId);
        var level1 = draft is null
            ? null
            : GetRequirementExperienceDraftsService.MapDraft(technicalProposalItemId, draft).Level1Resolution;

        var systems = await productSystemCatalogRepository.ListActiveSelectableAsync(cancellationToken);
        var glasses = await glassTypeCatalogRepository.GetActiveWithCurrentPriceRangesAsync(cancellationToken);

        return RequirementExperienceResult<RequirementExperienceLevel2Response>.Success(
            await resolver.ResolveAsync(
                proposal,
                item,
                level1,
                systems,
                glasses,
                cancellationToken));
    }

    private static bool CanAccess(Guid projectOwnerUserId, User user)
    {
        return user.Role == UserRole.Admin || user.Id == projectOwnerUserId;
    }
}

