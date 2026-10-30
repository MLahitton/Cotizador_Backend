using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.PreQuotes;
using Domain.Identity;
using Application.Projects.GetProjectDiagnosis;

namespace Application.PreQuotes.RequirementExperience;

public sealed class GetRequirementExperienceLevel2Service(
    IRequirementRepository requirementRepository,
    IIdentityRepository identityRepository,
    ICurrentUser currentUser,
    IProductSystemCatalogRepository productSystemCatalogRepository,
    IGlassTypeCatalogRepository glassTypeCatalogRepository,
    RequirementExperienceLevel2Resolver resolver,
    IProjectDiagnosisSourceRepository? sources = null)
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
        var initial = draft is null
            ? await RequirementExperiencePreferences.ReadInitialAsync(sources, proposal.Requirement.PreQuote.ProjectId, cancellationToken)
            : [];
        var level1 = RequirementExperiencePreferences.Resolve(technicalProposalItemId, draft, initial).Level1Resolution;
        var technicalSelection =
            await requirementRepository.FindExperienceTechnicalSelectionForUpdateAsync(
                technicalProposalItemId,
                cancellationToken);

        var systems = await productSystemCatalogRepository.ListActiveSelectableAsync(cancellationToken);
        var glasses = await glassTypeCatalogRepository.GetActiveWithCurrentPriceRangesAsync(cancellationToken);

        return RequirementExperienceResult<RequirementExperienceLevel2Response>.Success(
            await resolver.ResolveAsync(
                proposal,
                item,
                level1,
                technicalSelection,
                systems,
                glasses,
                cancellationToken));
    }

    private static bool CanAccess(Guid projectOwnerUserId, User user)
    {
        return user.Role == UserRole.Admin || user.Id == projectOwnerUserId;
    }
}

