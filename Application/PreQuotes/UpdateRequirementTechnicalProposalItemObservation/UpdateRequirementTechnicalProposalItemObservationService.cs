using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.UpdateRequirementTechnicalProposalItemObservation;

public sealed record UpdateRequirementTechnicalProposalItemObservationCommand(
    Guid TechnicalProposalId,
    Guid ItemId,
    string? Observation);

public sealed class UpdateRequirementTechnicalProposalItemObservationCommandValidator
    : AbstractValidator<UpdateRequirementTechnicalProposalItemObservationCommand>
{
    public UpdateRequirementTechnicalProposalItemObservationCommandValidator()
    {
        RuleFor(command => command.TechnicalProposalId).NotEmpty();
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Observation).MaximumLength(500);
    }
}

public enum UpdateRequirementTechnicalProposalItemObservationFailure
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

public sealed record RequirementTechnicalProposalItemObservationReadModel(
    Guid TechnicalProposalId,
    Guid ItemId,
    string? ManualObservation);

public sealed record UpdateRequirementTechnicalProposalItemObservationResult(
    bool IsSuccess,
    UpdateRequirementTechnicalProposalItemObservationFailure Failure,
    RequirementTechnicalProposalItemObservationReadModel? Observation)
{
    public static UpdateRequirementTechnicalProposalItemObservationResult Success(
        RequirementTechnicalProposalItemObservationReadModel observation) =>
        new(true, UpdateRequirementTechnicalProposalItemObservationFailure.None, observation);

    public static UpdateRequirementTechnicalProposalItemObservationResult Failed(
        UpdateRequirementTechnicalProposalItemObservationFailure failure) =>
        new(false, failure, null);
}

public sealed class UpdateRequirementTechnicalProposalItemObservationService(
    IValidator<UpdateRequirementTechnicalProposalItemObservationCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository)
{
    public async Task<UpdateRequirementTechnicalProposalItemObservationResult>
        ExecuteAsync(
            UpdateRequirementTechnicalProposalItemObservationCommand command,
            CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return UpdateRequirementTechnicalProposalItemObservationResult.Failed(
                UpdateRequirementTechnicalProposalItemObservationFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return UpdateRequirementTechnicalProposalItemObservationResult.Failed(
                UpdateRequirementTechnicalProposalItemObservationFailure.Unauthorized);
        }

        try
        {
            var user = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return UpdateRequirementTechnicalProposalItemObservationResult.Failed(
                    UpdateRequirementTechnicalProposalItemObservationFailure.Unauthorized);
            }

            if (!user.IsActive)
            {
                return UpdateRequirementTechnicalProposalItemObservationResult.Failed(
                    UpdateRequirementTechnicalProposalItemObservationFailure.InactiveUser);
            }

            var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
                command.TechnicalProposalId,
                cancellationToken);
            if (proposal is null)
            {
                return UpdateRequirementTechnicalProposalItemObservationResult.Failed(
                    UpdateRequirementTechnicalProposalItemObservationFailure.TechnicalProposalNotFound);
            }

            var access = await ValidateAccessAsync(proposal.Requirement, userId, cancellationToken);
            if (access != UpdateRequirementTechnicalProposalItemObservationFailure.None)
            {
                return UpdateRequirementTechnicalProposalItemObservationResult.Failed(access);
            }

            var item = proposal.Items.SingleOrDefault(value => value.Id == command.ItemId);
            if (item is null)
            {
                return UpdateRequirementTechnicalProposalItemObservationResult.Failed(
                    UpdateRequirementTechnicalProposalItemObservationFailure.TechnicalProposalItemNotFound);
            }

            item.UpdateManualObservation(command.Observation);

            await requirementRepository.SaveChangesAsync(cancellationToken);

            return UpdateRequirementTechnicalProposalItemObservationResult.Success(
                new RequirementTechnicalProposalItemObservationReadModel(
                    proposal.Id,
                    item.Id,
                    item.ManualObservation));
        }
        catch (RequirementPersistenceException)
        {
            return UpdateRequirementTechnicalProposalItemObservationResult.Failed(
                UpdateRequirementTechnicalProposalItemObservationFailure.PersistenceError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateRequirementTechnicalProposalItemObservationResult.Failed(
                UpdateRequirementTechnicalProposalItemObservationFailure.QueryError);
        }
    }

    private async Task<UpdateRequirementTechnicalProposalItemObservationFailure> ValidateAccessAsync(
        Requirement requirement,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!requirement.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemObservationFailure.RequirementNotFound;
        }

        var preQuote = await preQuoteRepository.FindByIdAsync(requirement.PreQuoteId, cancellationToken);
        if (preQuote is null)
        {
            return UpdateRequirementTechnicalProposalItemObservationFailure.PreQuoteNotFound;
        }

        var project = await projectRepository.FindByIdAsync(preQuote.ProjectId, cancellationToken);
        if (project is null)
        {
            return UpdateRequirementTechnicalProposalItemObservationFailure.ProjectNotFound;
        }

        if (project.CreatedByUserId != userId)
        {
            return UpdateRequirementTechnicalProposalItemObservationFailure.RequirementNotFound;
        }

        if (!project.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemObservationFailure.InactiveProject;
        }

        var client = await clientRepository.FindByIdAsync(project.ClientId, cancellationToken);
        if (client is null)
        {
            return UpdateRequirementTechnicalProposalItemObservationFailure.ClientNotFound;
        }

        return client.IsActive
            ? UpdateRequirementTechnicalProposalItemObservationFailure.None
            : UpdateRequirementTechnicalProposalItemObservationFailure.InactiveClient;
    }
}