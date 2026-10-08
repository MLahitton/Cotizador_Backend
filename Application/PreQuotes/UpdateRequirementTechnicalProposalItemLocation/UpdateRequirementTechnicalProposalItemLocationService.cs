using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.UpdateRequirementTechnicalProposalItemLocation;

public sealed record UpdateRequirementTechnicalProposalItemLocationCommand(
    Guid TechnicalProposalId,
    Guid ItemId,
    string? Location);

public sealed class UpdateRequirementTechnicalProposalItemLocationCommandValidator
    : AbstractValidator<UpdateRequirementTechnicalProposalItemLocationCommand>
{
    public UpdateRequirementTechnicalProposalItemLocationCommandValidator()
    {
        RuleFor(command => command.TechnicalProposalId).NotEmpty();
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Location).MaximumLength(500);
    }
}

public enum UpdateRequirementTechnicalProposalItemLocationFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    TechnicalProposalNotFound,
    RequirementNotFound,
    TechnicalProposalItemNotFound,
    PreQuoteNotFound,
    ProjectNotFound,
    InactiveProject,
    ClientNotFound,
    InactiveClient,
    QueryError,
    PersistenceError
}

public sealed record RequirementTechnicalProposalItemLocationReadModel(
    Guid TechnicalProposalId,
    Guid ItemId,
    string? ExtractedLocation,
    string? ManualLocationOverride,
    string? EffectiveLocation);

public sealed record UpdateRequirementTechnicalProposalItemLocationResult(
    bool IsSuccess,
    UpdateRequirementTechnicalProposalItemLocationFailure Failure,
    RequirementTechnicalProposalItemLocationReadModel? Location)
{
    public static UpdateRequirementTechnicalProposalItemLocationResult Success(
        RequirementTechnicalProposalItemLocationReadModel location) =>
        new(true, UpdateRequirementTechnicalProposalItemLocationFailure.None, location);

    public static UpdateRequirementTechnicalProposalItemLocationResult Failed(
        UpdateRequirementTechnicalProposalItemLocationFailure failure) =>
        new(false, failure, null);
}

public sealed class UpdateRequirementTechnicalProposalItemLocationService(
    IValidator<UpdateRequirementTechnicalProposalItemLocationCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository)
{
    public async Task<UpdateRequirementTechnicalProposalItemLocationResult>
        ExecuteAsync(
            UpdateRequirementTechnicalProposalItemLocationCommand command,
            CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return UpdateRequirementTechnicalProposalItemLocationResult.Failed(
                UpdateRequirementTechnicalProposalItemLocationFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return UpdateRequirementTechnicalProposalItemLocationResult.Failed(
                UpdateRequirementTechnicalProposalItemLocationFailure.Unauthorized);
        }

        try
        {
            var user = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return UpdateRequirementTechnicalProposalItemLocationResult.Failed(
                    UpdateRequirementTechnicalProposalItemLocationFailure.Unauthorized);
            }

            if (!user.IsActive)
            {
                return UpdateRequirementTechnicalProposalItemLocationResult.Failed(
                    UpdateRequirementTechnicalProposalItemLocationFailure.InactiveUser);
            }

            var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
                command.TechnicalProposalId,
                cancellationToken);
            if (proposal is null)
            {
                return UpdateRequirementTechnicalProposalItemLocationResult.Failed(
                    UpdateRequirementTechnicalProposalItemLocationFailure.TechnicalProposalNotFound);
            }

            var access = await ValidateAccessAsync(proposal.Requirement, userId, cancellationToken);
            if (access != UpdateRequirementTechnicalProposalItemLocationFailure.None)
            {
                return UpdateRequirementTechnicalProposalItemLocationResult.Failed(access);
            }

            var item = proposal.Items.SingleOrDefault(value => value.Id == command.ItemId);
            if (item is null)
            {
                return UpdateRequirementTechnicalProposalItemLocationResult.Failed(
                    UpdateRequirementTechnicalProposalItemLocationFailure.TechnicalProposalItemNotFound);
            }

            item.UpdateManualLocation(command.Location);

            await requirementRepository.SaveChangesAsync(cancellationToken);

            return UpdateRequirementTechnicalProposalItemLocationResult.Success(
                new RequirementTechnicalProposalItemLocationReadModel(
                    proposal.Id,
                    item.Id,
                    item.ExtractedItem?.OccurrenceContext,
                    item.ManualLocationOverride,
                    item.EffectiveLocation));
        }
        catch (RequirementPersistenceException)
        {
            return UpdateRequirementTechnicalProposalItemLocationResult.Failed(
                UpdateRequirementTechnicalProposalItemLocationFailure.PersistenceError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateRequirementTechnicalProposalItemLocationResult.Failed(
                UpdateRequirementTechnicalProposalItemLocationFailure.QueryError);
        }
    }

    private async Task<UpdateRequirementTechnicalProposalItemLocationFailure> ValidateAccessAsync(
        Requirement requirement,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!requirement.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemLocationFailure.RequirementNotFound;
        }

        var preQuote = await preQuoteRepository.FindByIdAsync(requirement.PreQuoteId, cancellationToken);
        if (preQuote is null)
        {
            return UpdateRequirementTechnicalProposalItemLocationFailure.PreQuoteNotFound;
        }

        var project = await projectRepository.FindByIdAsync(preQuote.ProjectId, cancellationToken);
        if (project is null)
        {
            return UpdateRequirementTechnicalProposalItemLocationFailure.ProjectNotFound;
        }

        if (project.CreatedByUserId != userId)
        {
            return UpdateRequirementTechnicalProposalItemLocationFailure.RequirementNotFound;
        }

        if (!project.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemLocationFailure.InactiveProject;
        }

        var client = await clientRepository.FindByIdAsync(project.ClientId, cancellationToken);
        if (client is null)
        {
            return UpdateRequirementTechnicalProposalItemLocationFailure.ClientNotFound;
        }

        return client.IsActive
            ? UpdateRequirementTechnicalProposalItemLocationFailure.None
            : UpdateRequirementTechnicalProposalItemLocationFailure.InactiveClient;
    }
}