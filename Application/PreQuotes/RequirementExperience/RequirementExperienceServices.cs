using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Domain.Identity;
using Domain.PreQuotes;

namespace Application.PreQuotes.RequirementExperience;

public sealed class GetRequirementExperienceCatalogService(
    IRequirementExperienceCatalogProvider catalogProvider)
{
    public RequirementExperienceCatalogResponse Execute()
    {
        return MapCatalog(catalogProvider.Current);
    }

    internal static RequirementExperienceCatalogResponse MapCatalog(RequirementExperienceCatalog catalog)
    {
        return new RequirementExperienceCatalogResponse(
            catalog.Version,
            catalog.Questions
                .Where(question => !question.ReferenceOnly)
                .Select(question => new RequirementExperienceQuestionResponse(
                    question.BenefitCode,
                    question.Label,
                    question.Question,
                    question.Options
                        .Select(option => new RequirementExperienceOptionResponse(
                            option.OptionCode,
                            option.OptionLabel,
                            option.ShortLabel,
                            option.Conditions))
                        .ToArray()))
                .ToArray(),
            catalog.Spaces
                .Where(space => !space.ReferenceOnly)
                .Select(space => new RequirementExperienceSpaceResponse(space.Code, space.Label, space.Priorities))
                .ToArray());
    }
}

public sealed class GetRequirementExperienceDraftsService(
    IRequirementRepository requirementRepository,
    IIdentityRepository identityRepository,
    ICurrentUser currentUser)
{
    public async Task<RequirementExperienceResult<RequirementExperienceDraftsResponse>> ExecuteAsync(
        Guid technicalProposalId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return RequirementExperienceResult<RequirementExperienceDraftsResponse>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);
        if (user is null)
        {
            return RequirementExperienceResult<RequirementExperienceDraftsResponse>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return RequirementExperienceResult<RequirementExperienceDraftsResponse>.Failed(
                RequirementExperienceFailure.InactiveUser);
        }

        var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
            technicalProposalId,
            cancellationToken);
        if (proposal is null)
        {
            return RequirementExperienceResult<RequirementExperienceDraftsResponse>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        if (!CanAccess(proposal.Requirement.PreQuote.Project.CreatedByUserId, user))
        {
            return RequirementExperienceResult<RequirementExperienceDraftsResponse>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        var drafts = await requirementRepository.ListExperienceDraftsByTechnicalProposalIdAsync(
            technicalProposalId,
            cancellationToken);
        var draftsByItem = drafts.ToDictionary(value => value.TechnicalProposalItemId);

        var items = proposal.Items
            .OrderBy(value => value.Sequence)
            .Select(item => draftsByItem.TryGetValue(item.Id, out var draft)
                ? MapDraft(item.Id, draft)
                : EmptyDraft(item.Id))
            .ToArray();

        return RequirementExperienceResult<RequirementExperienceDraftsResponse>.Success(
            new RequirementExperienceDraftsResponse(technicalProposalId, items));
    }

    internal static RequirementExperienceItemDraftResponse EmptyDraft(Guid itemId)
    {
        return new RequirementExperienceItemDraftResponse(
            itemId,
            CatalogVersion: null,
            SpaceTypeCode: null,
            ResolutionState: RequirementItemExperienceResolutionState.Pending.ToString().ToUpperInvariant(),
            Revision: 0,
            UpdatedAtUtc: null,
            UpdatedByUserId: null,
            Answers: []);
    }

    internal static RequirementExperienceItemDraftResponse MapDraft(
        Guid itemId,
        RequirementItemExperienceDraft draft)
    {
        return new RequirementExperienceItemDraftResponse(
            itemId,
            draft.CatalogVersion,
            draft.SpaceTypeCode,
            draft.ResolutionState.ToString().ToUpperInvariant(),
            draft.Revision,
            draft.UpdatedAtUtc,
            draft.UpdatedByUserId,
            draft.Answers
                .OrderBy(answer => answer.BenefitCode, StringComparer.Ordinal)
                .Select(answer => new RequirementExperienceAnswerResponse(answer.BenefitCode, answer.OptionCode))
                .ToArray());
    }

    private static bool CanAccess(Guid projectOwnerUserId, User user)
    {
        return user.Role == UserRole.Admin || user.Id == projectOwnerUserId;
    }
}

public sealed class UpdateRequirementExperienceDraftService(
    IRequirementRepository requirementRepository,
    IRequirementExperienceCatalogProvider catalogProvider,
    IIdentityRepository identityRepository,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public async Task<RequirementExperienceResult<RequirementExperienceItemDraftResponse>> ExecuteAsync(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        UpdateRequirementExperienceDraftRequest request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);
        if (user is null)
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(
                RequirementExperienceFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(
                RequirementExperienceFailure.InactiveUser);
        }

        var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
            technicalProposalId,
            cancellationToken);
        if (proposal is null)
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        if (!CanAccess(proposal.Requirement.PreQuote.Project.CreatedByUserId, user))
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(
                RequirementExperienceFailure.NotFound);
        }

        var item = proposal.Items.FirstOrDefault(value => value.Id == technicalProposalItemId);
        if (item is null)
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(
                RequirementExperienceFailure.ItemNotFound);
        }

        var catalog = catalogProvider.FindByVersion(request.CatalogVersion);
        if (catalog is null)
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(
                RequirementExperienceFailure.UnknownCatalogVersion);
        }

        var validation = ValidateRequest(catalog, request);
        if (validation is not null)
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(validation.Value);
        }

        var draft = await requirementRepository.FindExperienceDraftForUpdateAsync(
            technicalProposalItemId,
            cancellationToken);
        var currentRevision = draft?.Revision ?? 0;
        if (currentRevision != request.ExpectedRevision)
        {
            return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Failed(
                RequirementExperienceFailure.Conflict);
        }

        var answerValues = request.Answers
            .Select(answer => new RequirementItemExperienceAnswerValue(answer.BenefitCode, answer.OptionCode))
            .ToArray();

        if (draft is null)
        {
            draft = RequirementItemExperienceDraft.Create(
                technicalProposalId,
                technicalProposalItemId,
                request.CatalogVersion,
                request.SpaceTypeCode,
                answerValues,
                user.Id,
                timeProvider.GetUtcNow());
            requirementRepository.AddExperienceDraft(draft);
        }
        else
        {
            draft.ReplaceDraft(
                request.CatalogVersion,
                request.SpaceTypeCode,
                answerValues,
                user.Id,
                timeProvider.GetUtcNow());
        }

        await requirementRepository.SaveChangesAsync(cancellationToken);

        return RequirementExperienceResult<RequirementExperienceItemDraftResponse>.Success(
            GetRequirementExperienceDraftsService.MapDraft(technicalProposalItemId, draft));
    }

    private static RequirementExperienceFailure? ValidateRequest(
        RequirementExperienceCatalog catalog,
        UpdateRequirementExperienceDraftRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SpaceTypeCode)
            && !catalog.Spaces.Any(space => string.Equals(space.Code, request.SpaceTypeCode, StringComparison.Ordinal)))
        {
            return RequirementExperienceFailure.UnknownSpaceType;
        }

        var duplicate = request.Answers
            .GroupBy(answer => answer.BenefitCode, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return RequirementExperienceFailure.DuplicateBenefit;
        }

        foreach (var answer in request.Answers)
        {
            var question = catalog.Questions.FirstOrDefault(value =>
                string.Equals(value.BenefitCode, answer.BenefitCode, StringComparison.Ordinal));
            if (question is null)
            {
                return RequirementExperienceFailure.UnknownBenefit;
            }

            if (!question.Options.Any(option => string.Equals(option.OptionCode, answer.OptionCode, StringComparison.Ordinal)))
            {
                return RequirementExperienceFailure.OptionDoesNotBelongToBenefit;
            }
        }

        return null;
    }

    private static bool CanAccess(Guid projectOwnerUserId, User user)
    {
        return user.Role == UserRole.Admin || user.Id == projectOwnerUserId;
    }
}

public enum RequirementExperienceFailure
{
    None,
    Unauthorized,
    InactiveUser,
    NotFound,
    ItemNotFound,
    UnknownCatalogVersion,
    UnknownSpaceType,
    DuplicateBenefit,
    UnknownBenefit,
    OptionDoesNotBelongToBenefit,
    Conflict
}
