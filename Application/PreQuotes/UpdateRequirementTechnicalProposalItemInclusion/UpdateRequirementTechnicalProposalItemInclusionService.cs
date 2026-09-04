using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.UpdateRequirementTechnicalProposalItemInclusion;

public sealed record UpdateRequirementTechnicalProposalItemInclusionCommand(
    Guid RequirementId,
    Guid ItemId,
    bool IsIncluded,
    string? Reason = null);

public sealed class UpdateRequirementTechnicalProposalItemInclusionCommandValidator
    : AbstractValidator<UpdateRequirementTechnicalProposalItemInclusionCommand>
{
    public UpdateRequirementTechnicalProposalItemInclusionCommandValidator()
    {
        RuleFor(command => command.RequirementId).NotEmpty();
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Reason).MaximumLength(500);
    }
}

public enum UpdateRequirementTechnicalProposalItemInclusionFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    RequirementNotFound,
    PreQuoteNotFound,
    ProjectNotFound,
    InactiveProject,
    ClientNotFound,
    InactiveClient,
    TechnicalProposalNotFound,
    TechnicalProposalItemNotFound,
    QueryError,
    PersistenceError
}

public sealed record UpdateRequirementTechnicalProposalItemInclusionResult(
    bool IsSuccess,
    UpdateRequirementTechnicalProposalItemInclusionFailure Failure,
    RequirementTechnicalProposalItemInclusionReadModel? Inclusion)
{
    public static UpdateRequirementTechnicalProposalItemInclusionResult Success(
        RequirementTechnicalProposalItemInclusionReadModel inclusion) =>
        new(true, UpdateRequirementTechnicalProposalItemInclusionFailure.None,
            inclusion);

    public static UpdateRequirementTechnicalProposalItemInclusionResult Failed(
        UpdateRequirementTechnicalProposalItemInclusionFailure failure) =>
        new(false, failure, null);
}

public sealed class UpdateRequirementTechnicalProposalItemInclusionService(
    IValidator<UpdateRequirementTechnicalProposalItemInclusionCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    TimeProvider timeProvider)
{
    public async Task<UpdateRequirementTechnicalProposalItemInclusionResult>
        ExecuteAsync(
            UpdateRequirementTechnicalProposalItemInclusionCommand command,
            CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                UpdateRequirementTechnicalProposalItemInclusionFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                UpdateRequirementTechnicalProposalItemInclusionFailure.Unauthorized);
        }

        try
        {
            var user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
            if (user is null)
            {
                return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                    UpdateRequirementTechnicalProposalItemInclusionFailure.Unauthorized);
            }

            if (!user.IsActive)
            {
                return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                    UpdateRequirementTechnicalProposalItemInclusionFailure.InactiveUser);
            }

            await using var transaction = await requirementRepository
                .BeginPricingUpdateTransactionAsync(cancellationToken);

            var proposal = await requirementRepository
                .FindCurrentTechnicalProposalForUpdateAsync(
                    command.RequirementId,
                    cancellationToken);
            if (proposal is null)
            {
                return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                    UpdateRequirementTechnicalProposalItemInclusionFailure
                        .TechnicalProposalNotFound);
            }

            var access = await ValidateAccessAsync(
                proposal.Requirement,
                userId,
                cancellationToken);
            if (access != UpdateRequirementTechnicalProposalItemInclusionFailure.None)
            {
                return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                    access);
            }

            var item = proposal.Items.SingleOrDefault(value =>
                value.Id == command.ItemId);
            if (item is null)
            {
                return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                    UpdateRequirementTechnicalProposalItemInclusionFailure
                        .TechnicalProposalItemNotFound);
            }

            var changed = command.IsIncluded
                ? item.Reactivate()
                : item.Exclude(userId, timeProvider.GetUtcNow(), command.Reason);
            if (changed)
            {
                proposal.MarkCommerciallyChanged();
                proposal.InvalidateCommercialConfirmation();
            }

            await requirementRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return UpdateRequirementTechnicalProposalItemInclusionResult.Success(
                Map(proposal, item));
        }
        catch (RequirementPersistenceException)
        {
            return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                UpdateRequirementTechnicalProposalItemInclusionFailure
                    .PersistenceError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateRequirementTechnicalProposalItemInclusionResult.Failed(
                UpdateRequirementTechnicalProposalItemInclusionFailure.QueryError);
        }
    }

    private async Task<UpdateRequirementTechnicalProposalItemInclusionFailure>
        ValidateAccessAsync(
            Requirement requirement,
            Guid userId,
            CancellationToken cancellationToken)
    {
        if (!requirement.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemInclusionFailure
                .RequirementNotFound;
        }

        var preQuote = await preQuoteRepository.FindByIdAsync(
            requirement.PreQuoteId,
            cancellationToken);
        if (preQuote is null)
        {
            return UpdateRequirementTechnicalProposalItemInclusionFailure
                .PreQuoteNotFound;
        }

        var project = await projectRepository.FindByIdAsync(
            preQuote.ProjectId,
            cancellationToken);
        if (project is null)
        {
            return UpdateRequirementTechnicalProposalItemInclusionFailure
                .ProjectNotFound;
        }

        if (project.CreatedByUserId != userId)
        {
            return UpdateRequirementTechnicalProposalItemInclusionFailure
                .RequirementNotFound;
        }

        if (!project.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemInclusionFailure
                .InactiveProject;
        }

        var client = await clientRepository.FindByIdAsync(
            project.ClientId,
            cancellationToken);
        if (client is null)
        {
            return UpdateRequirementTechnicalProposalItemInclusionFailure
                .ClientNotFound;
        }

        return client.IsActive
            ? UpdateRequirementTechnicalProposalItemInclusionFailure.None
            : UpdateRequirementTechnicalProposalItemInclusionFailure.InactiveClient;
    }

    private static RequirementTechnicalProposalItemInclusionReadModel Map(
        RequirementTechnicalProposal proposal,
        RequirementTechnicalProposalItem item) =>
        new(
            proposal.Id,
            item.Id,
            item.IsIncluded,
            item.ExcludedAtUtc,
            item.ExcludedByUserId,
            item.ExclusionReason,
            proposal.CommercialRevision);
}

public sealed record RequirementTechnicalProposalItemInclusionReadModel(
    Guid TechnicalProposalId,
    Guid ItemId,
    bool IsIncluded,
    DateTimeOffset? ExcludedAtUtc,
    Guid? ExcludedByUserId,
    string? ExclusionReason,
    long CommercialRevision);
