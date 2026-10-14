using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;
using Application.PreQuotes.TechnicalProposalDataCompleteness;
using FluentValidation;

namespace Application.PreQuotes.UpdateRequirementTechnicalProposalItemPhysicalData;

public sealed record UpdateRequirementTechnicalProposalItemPhysicalDataCommand(
    Guid TechnicalProposalId,
    Guid ItemId,
    int? Quantity,
    int? WidthMm,
    int? HeightMm);

public sealed class UpdateRequirementTechnicalProposalItemPhysicalDataCommandValidator
    : AbstractValidator<UpdateRequirementTechnicalProposalItemPhysicalDataCommand>
{
    public UpdateRequirementTechnicalProposalItemPhysicalDataCommandValidator()
    {
        RuleFor(command => command.TechnicalProposalId).NotEmpty();
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.Quantity).Must(value => value is null or > 0);
        RuleFor(command => command.WidthMm).Must(value => value is null or > 0);
        RuleFor(command => command.HeightMm).Must(value => value is null or > 0);
    }
}

public enum UpdateRequirementTechnicalProposalItemPhysicalDataFailure
{
    None = 0,
    InvalidRequest,
    Unauthorized,
    InactiveUser,
    Forbidden,
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

public sealed record RequirementTechnicalProposalItemPhysicalDataReadModel(
    Guid TechnicalProposalId,
    Guid ItemId,
    int? Quantity,
    int? WidthMm,
    int? HeightMm,
    long CommercialRevision,
    RequirementTechnicalProposalItemDataCompletenessReadModel DataCompleteness);

public sealed record UpdateRequirementTechnicalProposalItemPhysicalDataResult(
    bool IsSuccess,
    UpdateRequirementTechnicalProposalItemPhysicalDataFailure Failure,
    RequirementTechnicalProposalItemPhysicalDataReadModel? PhysicalData)
{
    public static UpdateRequirementTechnicalProposalItemPhysicalDataResult Success(
        RequirementTechnicalProposalItemPhysicalDataReadModel location) =>
        new(true, UpdateRequirementTechnicalProposalItemPhysicalDataFailure.None, location);

    public static UpdateRequirementTechnicalProposalItemPhysicalDataResult Failed(
        UpdateRequirementTechnicalProposalItemPhysicalDataFailure failure) =>
        new(false, failure, null);
}

public sealed class UpdateRequirementTechnicalProposalItemPhysicalDataService(
    IValidator<UpdateRequirementTechnicalProposalItemPhysicalDataCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository)
{
    public async Task<UpdateRequirementTechnicalProposalItemPhysicalDataResult>
        ExecuteAsync(
            UpdateRequirementTechnicalProposalItemPhysicalDataCommand command,
            CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(
                UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(
                UpdateRequirementTechnicalProposalItemPhysicalDataFailure.Unauthorized);
        }

        try
        {
            var user = await identityRepository.FindUserByIdAsync(userId, cancellationToken);
            if (user is null)
            {
                return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(
                    UpdateRequirementTechnicalProposalItemPhysicalDataFailure.Unauthorized);
            }

            if (!user.IsActive)
            {
                return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(
                    UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InactiveUser);
            }

            var proposal = await requirementRepository.FindTechnicalProposalForUpdateAsync(
                command.TechnicalProposalId,
                cancellationToken);
            if (proposal is null)
            {
                return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(
                    UpdateRequirementTechnicalProposalItemPhysicalDataFailure.TechnicalProposalNotFound);
            }

            var access = await ValidateAccessAsync(proposal.Requirement, userId, cancellationToken);
            if (access != UpdateRequirementTechnicalProposalItemPhysicalDataFailure.None)
            {
                return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(access);
            }

            var item = proposal.Items.SingleOrDefault(value => value.Id == command.ItemId);
            if (item is null)
            {
                return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(
                    UpdateRequirementTechnicalProposalItemPhysicalDataFailure.TechnicalProposalItemNotFound);
            }

            var before = (item.EffectiveQuantity, item.EffectiveWidthMillimeters, item.EffectiveHeightMillimeters);
            var after = (command.Quantity ?? before.EffectiveQuantity,
                command.WidthMm ?? before.EffectiveWidthMillimeters,
                command.HeightMm ?? before.EffectiveHeightMillimeters);
            if (before != after)
            {
                item.ApplyManualDataOverride(command.Quantity, command.WidthMm, command.HeightMm);
                if (item.IsIncluded)
                {
                    proposal.MarkCommerciallyChanged();
                    proposal.InvalidateCommercialConfirmation();
                }

                await requirementRepository.SaveChangesAsync(cancellationToken);
            }

            return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Success(
                new RequirementTechnicalProposalItemPhysicalDataReadModel(
                    proposal.Id,
                    item.Id,
                    item.EffectiveQuantity,
                    item.EffectiveWidthMillimeters,
                    item.EffectiveHeightMillimeters,
                    proposal.CommercialRevision,
                    TechnicalProposalItemDataCompletenessEvaluator.Evaluate(item)));
        }
        catch (RequirementPersistenceException)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(
                UpdateRequirementTechnicalProposalItemPhysicalDataFailure.PersistenceError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataResult.Failed(
                UpdateRequirementTechnicalProposalItemPhysicalDataFailure.QueryError);
        }
    }

    private async Task<UpdateRequirementTechnicalProposalItemPhysicalDataFailure> ValidateAccessAsync(
        Requirement requirement,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!requirement.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataFailure.RequirementNotFound;
        }

        var preQuote = await preQuoteRepository.FindByIdAsync(requirement.PreQuoteId, cancellationToken);
        if (preQuote is null)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataFailure.PreQuoteNotFound;
        }

        var project = await projectRepository.FindByIdAsync(preQuote.ProjectId, cancellationToken);
        if (project is null)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataFailure.ProjectNotFound;
        }

        if (project.CreatedByUserId != userId)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataFailure.Forbidden;
        }

        if (!project.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InactiveProject;
        }

        var client = await clientRepository.FindByIdAsync(project.ClientId, cancellationToken);
        if (client is null)
        {
            return UpdateRequirementTechnicalProposalItemPhysicalDataFailure.ClientNotFound;
        }

        return client.IsActive
            ? UpdateRequirementTechnicalProposalItemPhysicalDataFailure.None
            : UpdateRequirementTechnicalProposalItemPhysicalDataFailure.InactiveClient;
    }
}
