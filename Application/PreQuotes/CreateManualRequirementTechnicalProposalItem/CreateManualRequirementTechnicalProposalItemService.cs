using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.CreateManualRequirementTechnicalProposalItem;

public sealed record CreateManualRequirementTechnicalProposalItemCommand(
    Guid RequirementId,
    string? Reference,
    string? Description,
    string ElementType,
    int Quantity,
    int WidthMillimeters,
    int HeightMillimeters,
    Guid SystemId,
    Guid GlassTypeId,
    Guid FinishTypeId,
    string? Note = null);

public sealed class CreateManualRequirementTechnicalProposalItemCommandValidator
    : AbstractValidator<CreateManualRequirementTechnicalProposalItemCommand>
{
    public CreateManualRequirementTechnicalProposalItemCommandValidator()
    {
        RuleFor(command => command.RequirementId).NotEmpty();
        RuleFor(command => command.Reference).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Description).MaximumLength(1000);
        RuleFor(command => command.ElementType).NotEmpty().MaximumLength(50);
        RuleFor(command => command.Quantity).GreaterThan(0);
        RuleFor(command => command.WidthMillimeters).GreaterThan(0);
        RuleFor(command => command.HeightMillimeters).GreaterThan(0);
        RuleFor(command => command.SystemId).NotEmpty();
        RuleFor(command => command.GlassTypeId).NotEmpty();
        RuleFor(command => command.FinishTypeId).NotEmpty();
        RuleFor(command => command.Note).MaximumLength(1000);
    }
}

public enum CreateManualRequirementTechnicalProposalItemFailure
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
    InvalidSystemSelection,
    InvalidGlassSelection,
    InvalidFinishSelection,
    QueryError,
    PersistenceError
}

public sealed record CreateManualRequirementTechnicalProposalItemResult(
    bool IsSuccess,
    CreateManualRequirementTechnicalProposalItemFailure Failure,
    ManualRequirementTechnicalProposalItemReadModel? Item)
{
    public static CreateManualRequirementTechnicalProposalItemResult Success(
        ManualRequirementTechnicalProposalItemReadModel item) =>
        new(true, CreateManualRequirementTechnicalProposalItemFailure.None, item);

    public static CreateManualRequirementTechnicalProposalItemResult Failed(
        CreateManualRequirementTechnicalProposalItemFailure failure) =>
        new(false, failure, null);
}

public sealed class CreateManualRequirementTechnicalProposalItemService(
    IValidator<CreateManualRequirementTechnicalProposalItemCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IRequirementRepository requirementRepository,
    IPreQuoteRepository preQuoteRepository,
    IProjectRepository projectRepository,
    IClientRepository clientRepository,
    IProductSystemCatalogRepository productSystemCatalog,
    IGlassTypeCatalogRepository glassCatalog,
    IFinishTypeCatalogRepository finishCatalog,
    TimeProvider timeProvider)
{
    public async Task<CreateManualRequirementTechnicalProposalItemResult>
        ExecuteAsync(
            CreateManualRequirementTechnicalProposalItemCommand command,
            CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid || !TryParseElementType(command.ElementType, out var elementType))
        {
            return CreateManualRequirementTechnicalProposalItemResult.Failed(
                CreateManualRequirementTechnicalProposalItemFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
        {
            return CreateManualRequirementTechnicalProposalItemResult.Failed(
                CreateManualRequirementTechnicalProposalItemFailure.Unauthorized);
        }

        try
        {
            var user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
            if (user is null)
            {
                return CreateManualRequirementTechnicalProposalItemResult.Failed(
                    CreateManualRequirementTechnicalProposalItemFailure.Unauthorized);
            }

            if (!user.IsActive)
            {
                return CreateManualRequirementTechnicalProposalItemResult.Failed(
                    CreateManualRequirementTechnicalProposalItemFailure.InactiveUser);
            }

            await using var transaction = await requirementRepository
                .BeginPricingUpdateTransactionAsync(cancellationToken);

            var proposal = await requirementRepository
                .FindCurrentTechnicalProposalForUpdateAsync(
                    command.RequirementId,
                    cancellationToken);
            if (proposal is null)
            {
                return CreateManualRequirementTechnicalProposalItemResult.Failed(
                    CreateManualRequirementTechnicalProposalItemFailure.TechnicalProposalNotFound);
            }

            var access = await ValidateAccessAsync(
                proposal.Requirement,
                userId,
                cancellationToken);
            if (access != CreateManualRequirementTechnicalProposalItemFailure.None)
            {
                return CreateManualRequirementTechnicalProposalItemResult.Failed(access);
            }

            var systems = await productSystemCatalog.ListActiveSelectableAsync(
                cancellationToken);
            var glasses = await glassCatalog.GetActiveWithCurrentPriceRangesAsync(
                cancellationToken);
            var finishes = await finishCatalog.ListActiveAsync(cancellationToken);

            if (!systems.Any(system => system.Id == command.SystemId
                && system.IsActive
                && system.IsSelectable
                && IsAllowedForCommercialLine(
                    system,
                    proposal.Requirement.CommercialLine)))
            {
                return CreateManualRequirementTechnicalProposalItemResult.Failed(
                    CreateManualRequirementTechnicalProposalItemFailure.InvalidSystemSelection);
            }

            if (!glasses.Any(glass => glass.GlassTypeId == command.GlassTypeId
                && glass.IsActive
                && glass.IsSelectable))
            {
                return CreateManualRequirementTechnicalProposalItemResult.Failed(
                    CreateManualRequirementTechnicalProposalItemFailure.InvalidGlassSelection);
            }

            if (!finishes.Any(finish => finish.Id == command.FinishTypeId
                && finish.IsActive
                && finish.IsSelectable))
            {
                return CreateManualRequirementTechnicalProposalItemResult.Failed(
                    CreateManualRequirementTechnicalProposalItemFailure.InvalidFinishSelection);
            }

            var nextSequence = proposal.Items.Count == 0
                ? 1
                : proposal.Items.Max(item => item.Sequence) + 1;
            var now = timeProvider.GetUtcNow();
            var item = RequirementTechnicalProposalItem.CreateManual(
                proposal.Id,
                nextSequence,
                command.Reference,
                command.Description,
                elementType,
                command.Quantity,
                command.WidthMillimeters,
                command.HeightMillimeters,
                command.SystemId,
                command.GlassTypeId,
                command.FinishTypeId,
                userId,
                now,
                command.Note);

            proposal.AddItem(item);
            proposal.MarkCommerciallyChanged();
            proposal.InvalidateCommercialConfirmation();

            await requirementRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreateManualRequirementTechnicalProposalItemResult.Success(
                new ManualRequirementTechnicalProposalItemReadModel(
                    proposal.Id,
                    item.Id,
                    item.Source.ToString(),
                    item.Sequence,
                    proposal.CommercialRevision));
        }
        catch (RequirementPersistenceException)
        {
            return CreateManualRequirementTechnicalProposalItemResult.Failed(
                CreateManualRequirementTechnicalProposalItemFailure.PersistenceError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateManualRequirementTechnicalProposalItemResult.Failed(
                CreateManualRequirementTechnicalProposalItemFailure.QueryError);
        }
    }

    private async Task<CreateManualRequirementTechnicalProposalItemFailure>
        ValidateAccessAsync(
            Requirement requirement,
            Guid userId,
            CancellationToken cancellationToken)
    {
        if (!requirement.IsActive)
        {
            return CreateManualRequirementTechnicalProposalItemFailure.RequirementNotFound;
        }

        var preQuote = await preQuoteRepository.FindByIdAsync(
            requirement.PreQuoteId,
            cancellationToken);
        if (preQuote is null)
        {
            return CreateManualRequirementTechnicalProposalItemFailure.PreQuoteNotFound;
        }

        var project = await projectRepository.FindByIdAsync(
            preQuote.ProjectId,
            cancellationToken);
        if (project is null)
        {
            return CreateManualRequirementTechnicalProposalItemFailure.ProjectNotFound;
        }

        if (project.CreatedByUserId != userId)
        {
            return CreateManualRequirementTechnicalProposalItemFailure.RequirementNotFound;
        }

        if (!project.IsActive)
        {
            return CreateManualRequirementTechnicalProposalItemFailure.InactiveProject;
        }

        var client = await clientRepository.FindByIdAsync(
            project.ClientId,
            cancellationToken);
        if (client is null)
        {
            return CreateManualRequirementTechnicalProposalItemFailure.ClientNotFound;
        }

        return client.IsActive
            ? CreateManualRequirementTechnicalProposalItemFailure.None
            : CreateManualRequirementTechnicalProposalItemFailure.InactiveClient;
    }

    private static bool TryParseElementType(
        string value,
        out StructuredElementType elementType)
    {
        elementType = value.Trim().ToUpperInvariant() switch
        {
            "WINDOW" => StructuredElementType.Window,
            "DOOR" => StructuredElementType.Door,
            "FACADE" => StructuredElementType.Facade,
            "PARTITION" => StructuredElementType.Partition,
            "RAILING" => StructuredElementType.Railing,
            "SKYLIGHT" => StructuredElementType.Skylight,
            "SHOWER_DIVISION" => StructuredElementType.ShowerDivision,
            "OTHER" => StructuredElementType.Other,
            _ => default
        };

        return Enum.IsDefined(elementType)
            && value.Trim().Length > 0
            && elementType != default;
    }

    private static bool IsAllowedForCommercialLine(
        ProductSystemCatalogReadModel system,
        RequirementCommercialLine? commercialLine) =>
        commercialLine switch
        {
            RequirementCommercialLine.Classic => MatchesLine(system, "CLASSIC"),
            RequirementCommercialLine.Signature => MatchesLine(system, "SIGNATURE"),
            RequirementCommercialLine.Essential => true,
            RequirementCommercialLine.Bioconfort => true,
            _ => false
        };

    private static bool MatchesLine(
        ProductSystemCatalogReadModel system,
        string expected) =>
        string.Equals(
            system.CommercialLine?.Trim(),
            expected,
            StringComparison.OrdinalIgnoreCase);
}

public sealed record ManualRequirementTechnicalProposalItemReadModel(
    Guid TechnicalProposalId,
    Guid ItemId,
    string Source,
    int Sequence,
    long CommercialRevision);
