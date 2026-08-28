using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.TechnicalProposalReadiness;
using Domain.PreQuotes;

namespace Application.PreQuotes.ConfirmRequirementTechnicalProposalSelection;

public sealed record ConfirmRequirementTechnicalProposalSelectionCommand(
    Guid TechnicalProposalId);

public enum ConfirmRequirementTechnicalProposalSelectionFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    TechnicalProposalNotFound,
    RequirementNotFound,
    PreQuoteNotFound,
    ProjectNotFound,
    InactiveProject,
    ClientNotFound,
    InactiveClient,
    IncompleteTechnicalProposal,
    QueryError,
    PersistenceError
}

public sealed record ConfirmRequirementTechnicalProposalSelectionResult(
    bool IsSuccess,
    ConfirmRequirementTechnicalProposalSelectionFailure Failure,
    ConfirmRequirementTechnicalProposalSelectionReadModel? Confirmation)
{
    public static ConfirmRequirementTechnicalProposalSelectionResult Success(
        ConfirmRequirementTechnicalProposalSelectionReadModel confirmation) =>
        new(true, ConfirmRequirementTechnicalProposalSelectionFailure.None,
            confirmation);

    public static ConfirmRequirementTechnicalProposalSelectionResult Failed(
        ConfirmRequirementTechnicalProposalSelectionFailure failure) =>
        new(false, failure, null);
}

public sealed class ConfirmRequirementTechnicalProposalSelectionService(
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    TimeProvider timeProvider)
{
    public async Task<ConfirmRequirementTechnicalProposalSelectionResult>
        ExecuteAsync(
            ConfirmRequirementTechnicalProposalSelectionCommand command,
            CancellationToken cancellationToken)
    {
        if (command.TechnicalProposalId == Guid.Empty)
        {
            return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                ConfirmRequirementTechnicalProposalSelectionFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                ConfirmRequirementTechnicalProposalSelectionFailure.Unauthorized);
        }

        try
        {
            var user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
            if (user is null)
            {
                return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                    ConfirmRequirementTechnicalProposalSelectionFailure.Unauthorized);
            }

            if (!user.IsActive)
            {
                return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                    ConfirmRequirementTechnicalProposalSelectionFailure.InactiveUser);
            }

            var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
                command.TechnicalProposalId,
                cancellationToken);
            if (proposal is null)
            {
                return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                    ConfirmRequirementTechnicalProposalSelectionFailure.TechnicalProposalNotFound);
            }

            var access = await ValidateAccessAsync(
                proposal.Requirement,
                userId,
                cancellationToken);
            if (access != ConfirmRequirementTechnicalProposalSelectionFailure.None)
            {
                return ConfirmRequirementTechnicalProposalSelectionResult.Failed(access);
            }

            if (TechnicalProposalReadinessEvaluator.BlocksConfirmation(proposal))
            {
                return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                    ConfirmRequirementTechnicalProposalSelectionFailure.IncompleteTechnicalProposal);
            }

            try
            {
                proposal.ConfirmCommercialSelection(
                    userId,
                    timeProvider.GetUtcNow());
            }
            catch (InvalidOperationException)
            {
                return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                    ConfirmRequirementTechnicalProposalSelectionFailure.IncompleteTechnicalProposal);
            }

            await requirementRepository.SaveChangesAsync(cancellationToken);

            return ConfirmRequirementTechnicalProposalSelectionResult.Success(
                Map(proposal));
        }
        catch (RequirementPersistenceException)
        {
            return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                ConfirmRequirementTechnicalProposalSelectionFailure.PersistenceError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ConfirmRequirementTechnicalProposalSelectionResult.Failed(
                ConfirmRequirementTechnicalProposalSelectionFailure.QueryError);
        }
    }

    private async Task<ConfirmRequirementTechnicalProposalSelectionFailure>
        ValidateAccessAsync(
            Requirement requirement,
            Guid userId,
            CancellationToken cancellationToken)
    {
        if (!requirement.IsActive)
        {
            return ConfirmRequirementTechnicalProposalSelectionFailure.RequirementNotFound;
        }

        var preQuote = await preQuoteRepository.FindByIdAsync(
            requirement.PreQuoteId,
            cancellationToken);
        if (preQuote is null)
        {
            return ConfirmRequirementTechnicalProposalSelectionFailure.PreQuoteNotFound;
        }

        var project = await projectRepository.FindByIdAsync(
            preQuote.ProjectId,
            cancellationToken);
        if (project is null)
        {
            return ConfirmRequirementTechnicalProposalSelectionFailure.ProjectNotFound;
        }

        if (project.CreatedByUserId != userId)
        {
            return ConfirmRequirementTechnicalProposalSelectionFailure.RequirementNotFound;
        }

        if (!project.IsActive)
        {
            return ConfirmRequirementTechnicalProposalSelectionFailure.InactiveProject;
        }

        var client = await clientRepository.FindByIdAsync(
            project.ClientId,
            cancellationToken);
        if (client is null)
        {
            return ConfirmRequirementTechnicalProposalSelectionFailure.ClientNotFound;
        }

        return client.IsActive
            ? ConfirmRequirementTechnicalProposalSelectionFailure.None
            : ConfirmRequirementTechnicalProposalSelectionFailure.InactiveClient;
    }

    private static ConfirmRequirementTechnicalProposalSelectionReadModel Map(
        RequirementTechnicalProposal proposal) =>
        new(
            proposal.Id,
            ToContract(proposal.CommercialConfirmationState),
            proposal.CommercialConfirmedAtUtc,
            proposal.CommercialConfirmedByUserId);

    private static string ToContract(
        RequirementTechnicalProposalCommercialConfirmationState state) =>
        state switch
        {
            RequirementTechnicalProposalCommercialConfirmationState
                .PendingConfirmation => "PENDING_CONFIRMATION",
            RequirementTechnicalProposalCommercialConfirmationState.Confirmed =>
                "CONFIRMED",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
}

public sealed record ConfirmRequirementTechnicalProposalSelectionReadModel(
    Guid TechnicalProposalId,
    string State,
    DateTimeOffset? ConfirmedAtUtc,
    Guid? ConfirmedByUserId);
