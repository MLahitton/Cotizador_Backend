using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Catalogs;
using Application.Common.Abstractions.Clients;
using Application.Common.Abstractions.PreQuotes;
using Application.Common.Abstractions.Projects;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.UpdateRequirementTechnicalProposalItemSelection;

public sealed record UpdateRequirementTechnicalProposalItemSelectionCommand(
    Guid TechnicalProposalId,
    Guid ItemId,
    bool? ConfirmSuggested,
    Guid? SystemId,
    Guid? GlassId,
    Guid? FinishId,
    int? Quantity = null,
    int? WidthMillimeters = null,
    int? HeightMillimeters = null);

public sealed class UpdateRequirementTechnicalProposalItemSelectionCommandValidator
    : AbstractValidator<UpdateRequirementTechnicalProposalItemSelectionCommand>
{
    public UpdateRequirementTechnicalProposalItemSelectionCommandValidator()
    {
        RuleFor(command => command.TechnicalProposalId).NotEmpty();
        RuleFor(command => command.ItemId).NotEmpty();
        RuleFor(command => command.SystemId)
            .Must(value => value is null || value != Guid.Empty);
        RuleFor(command => command.GlassId)
            .Must(value => value is null || value != Guid.Empty);
        RuleFor(command => command.FinishId)
            .Must(value => value is null || value != Guid.Empty);
        RuleFor(command => command.Quantity)
            .Must(value => value is null || value > 0);
        RuleFor(command => command.WidthMillimeters)
            .Must(value => value is null || value > 0);
        RuleFor(command => command.HeightMillimeters)
            .Must(value => value is null || value > 0);
        RuleFor(command => command)
            .Must(command => command.ConfirmSuggested != true
                || (command.SystemId is null
                    && command.GlassId is null
                    && command.FinishId is null));
    }
}

public enum UpdateRequirementTechnicalProposalItemSelectionFailure
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
    InvalidSystemSelection,
    InvalidGlassSelection,
    InvalidFinishSelection,
    QueryError,
    PersistenceError
}

public sealed record UpdateRequirementTechnicalProposalItemSelectionResult(
    bool IsSuccess,
    UpdateRequirementTechnicalProposalItemSelectionFailure Failure,
    RequirementTechnicalProposalItemSelectionReadModel? Selection)
{
    public static UpdateRequirementTechnicalProposalItemSelectionResult Success(
        RequirementTechnicalProposalItemSelectionReadModel selection) =>
        new(true, UpdateRequirementTechnicalProposalItemSelectionFailure.None,
            selection);

    public static UpdateRequirementTechnicalProposalItemSelectionResult Failed(
        UpdateRequirementTechnicalProposalItemSelectionFailure failure) =>
        new(false, failure, null);
}

public sealed class UpdateRequirementTechnicalProposalItemSelectionService(
    IValidator<UpdateRequirementTechnicalProposalItemSelectionCommand> validator,
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
    public async Task<UpdateRequirementTechnicalProposalItemSelectionResult>
        ExecuteAsync(
            UpdateRequirementTechnicalProposalItemSelectionCommand command,
            CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                UpdateRequirementTechnicalProposalItemSelectionFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                UpdateRequirementTechnicalProposalItemSelectionFailure.Unauthorized);
        }

        try
        {
            var user = await identityRepository.FindUserByIdAsync(
                userId,
                cancellationToken);
            if (user is null)
            {
                return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                    UpdateRequirementTechnicalProposalItemSelectionFailure.Unauthorized);
            }

            if (!user.IsActive)
            {
                return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                    UpdateRequirementTechnicalProposalItemSelectionFailure.InactiveUser);
            }

            var proposal =
                await requirementRepository.FindTechnicalProposalForUpdateAsync(
                    command.TechnicalProposalId,
                    cancellationToken);
            if (proposal is null)
            {
                return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                    UpdateRequirementTechnicalProposalItemSelectionFailure
                        .TechnicalProposalNotFound);
            }

            var access = await ValidateAccessAsync(
                proposal.Requirement,
                userId,
                cancellationToken);
            if (access != UpdateRequirementTechnicalProposalItemSelectionFailure.None)
            {
                return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                    access);
            }

            var item = proposal.Items.SingleOrDefault(value =>
                value.Id == command.ItemId);
            if (item is null)
            {
                return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                    UpdateRequirementTechnicalProposalItemSelectionFailure
                        .TechnicalProposalItemNotFound);
            }

            var systems = await productSystemCatalog.ListActiveSelectableAsync(
                cancellationToken);
            var glasses = await glassCatalog.GetActiveWithCurrentPriceRangesAsync(
                cancellationToken);
            var finishes = await finishCatalog.ListActiveAsync(cancellationToken);

            if (command.SystemId is { } systemId
                && !systems.Any(system => system.Id == systemId
                    && system.IsActive
                    && system.IsSelectable
                    && IsAllowedForCommercialLine(
                        system,
                        proposal.Requirement.CommercialLine)))
            {
                return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                    UpdateRequirementTechnicalProposalItemSelectionFailure
                        .InvalidSystemSelection);
            }

            if (command.GlassId is { } glassId
                && !glasses.Any(glass => glass.GlassTypeId == glassId
                    && glass.IsActive
                    && glass.IsSelectable))
            {
                return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                    UpdateRequirementTechnicalProposalItemSelectionFailure
                        .InvalidGlassSelection);
            }

            if (command.FinishId is { } finishId
                && !finishes.Any(finish => finish.Id == finishId
                    && finish.IsActive
                    && finish.IsSelectable))
            {
                return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                    UpdateRequirementTechnicalProposalItemSelectionFailure
                        .InvalidFinishSelection);
            }

            var baseSystemId = command.ConfirmSuggested == true
                || item.SelectedAtUtc is null
                    ? item.SuggestedSystemId
                    : item.SelectedSystemId;
            var baseGlassId = command.ConfirmSuggested == true
                || item.SelectedAtUtc is null
                    ? item.SuggestedGlassTypeId
                    : item.SelectedGlassTypeId;
            var baseFinishId = command.ConfirmSuggested == true
                || item.SelectedAtUtc is null
                    ? item.SuggestedFinishTypeId
                    : item.SelectedFinishTypeId;

            var selectedSystemId = command.SystemId ?? baseSystemId;
            var selectedGlassId = command.GlassId ?? baseGlassId;
            var selectedFinishId = command.FinishId ?? baseFinishId;

            item.ApplyManualDataOverride(
                command.Quantity,
                command.WidthMillimeters,
                command.HeightMillimeters);

            item.Select(
                selectedSystemId,
                selectedGlassId,
                selectedFinishId,
                userId,
                timeProvider.GetUtcNow());
            proposal.InvalidateCommercialConfirmation();

            await requirementRepository.SaveChangesAsync(cancellationToken);

            var selected = MapSelection(
                proposal.Id,
                item,
                systems,
                glasses,
                finishes);
            return UpdateRequirementTechnicalProposalItemSelectionResult.Success(
                selected);
        }
        catch (RequirementPersistenceException)
        {
            return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                UpdateRequirementTechnicalProposalItemSelectionFailure
                    .PersistenceError);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return UpdateRequirementTechnicalProposalItemSelectionResult.Failed(
                UpdateRequirementTechnicalProposalItemSelectionFailure.QueryError);
        }
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

    private async Task<UpdateRequirementTechnicalProposalItemSelectionFailure>
        ValidateAccessAsync(
            Requirement requirement,
            Guid userId,
            CancellationToken cancellationToken)
    {
        if (!requirement.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemSelectionFailure
                .RequirementNotFound;
        }

        var preQuote = await preQuoteRepository.FindByIdAsync(
            requirement.PreQuoteId,
            cancellationToken);
        if (preQuote is null)
        {
            return UpdateRequirementTechnicalProposalItemSelectionFailure
                .PreQuoteNotFound;
        }

        var project = await projectRepository.FindByIdAsync(
            preQuote.ProjectId,
            cancellationToken);
        if (project is null)
        {
            return UpdateRequirementTechnicalProposalItemSelectionFailure
                .ProjectNotFound;
        }

        if (project.CreatedByUserId != userId)
        {
            return UpdateRequirementTechnicalProposalItemSelectionFailure
                .RequirementNotFound;
        }

        if (!project.IsActive)
        {
            return UpdateRequirementTechnicalProposalItemSelectionFailure
                .InactiveProject;
        }

        var client = await clientRepository.FindByIdAsync(
            project.ClientId,
            cancellationToken);
        if (client is null)
        {
            return UpdateRequirementTechnicalProposalItemSelectionFailure
                .ClientNotFound;
        }

        return client.IsActive
            ? UpdateRequirementTechnicalProposalItemSelectionFailure.None
            : UpdateRequirementTechnicalProposalItemSelectionFailure.InactiveClient;
    }

    private static RequirementTechnicalProposalItemSelectionReadModel MapSelection(
        Guid technicalProposalId,
        RequirementTechnicalProposalItem item,
        IReadOnlyList<ProductSystemCatalogReadModel> systems,
        IReadOnlyList<GlassTypeCatalogReadModel> glasses,
        IReadOnlyList<FinishTypeCatalogReadModel> finishes)
    {
        var systemById = systems.ToDictionary(system => system.Id);
        var glassById = glasses.ToDictionary(glass => glass.GlassTypeId);
        var finishById = finishes.ToDictionary(finish => finish.Id);

        return new RequirementTechnicalProposalItemSelectionReadModel(
            technicalProposalId,
            item.Id,
            item.SelectedAtUtc is null || item.SelectedByUserId is null
                ? "UNCONFIRMED"
                : item.SelectedSystemId == item.SuggestedSystemId
                    && item.SelectedGlassTypeId == item.SuggestedGlassTypeId
                    && item.SelectedFinishTypeId == item.SuggestedFinishTypeId
                        ? "CONFIRMED_AS_SUGGESTED"
                        : "MODIFIED",
            item.SelectedAtUtc,
            item.SelectedByUserId,
            MapSystem(item.SelectedSystemId, systemById),
            MapGlass(item.SelectedGlassTypeId, glassById),
            MapFinish(item.SelectedFinishTypeId, finishById));
    }

    private static RequirementTechnicalProposalSystemOptionReadModel? MapSystem(
        Guid? id,
        IReadOnlyDictionary<Guid, ProductSystemCatalogReadModel> systems) =>
        id is { } value && systems.TryGetValue(value, out var system)
            ? new(
                system.Id,
                system.Code,
                system.Name,
                system.TechnicalName,
                system.CommercialName,
                system.FunctionalType,
                system.Family,
                system.Series,
                system.CommercialLine,
                system.Variant)
            : null;

    private static RequirementTechnicalProposalGlassOptionReadModel? MapGlass(
        Guid? id,
        IReadOnlyDictionary<Guid, GlassTypeCatalogReadModel> glasses) =>
        id is { } value && glasses.TryGetValue(value, out var glass)
            ? new(
                glass.GlassTypeId,
                glass.Code,
                glass.Name,
                glass.Family,
                glass.Composition,
                glass.Treatment,
                glass.OuterThicknessMm,
                glass.InnerThicknessMm,
                glass.PvbThicknessMm,
                glass.PvbType,
                glass.PvbColor,
                glass.ChamberThicknessMm,
                glass.ProductLine,
                glass.ProductToken,
                glass.Pattern,
                glass.Color)
            : null;

    private static RequirementTechnicalProposalFinishOptionReadModel? MapFinish(
        Guid? id,
        IReadOnlyDictionary<Guid, FinishTypeCatalogReadModel> finishes) =>
        id is { } value && finishes.TryGetValue(value, out var finish)
            ? new(
                finish.Id,
                finish.Code,
                finish.Name,
                finish.NormalizedType,
                finish.Color,
                finish.Texture,
                finish.Process,
                finish.CommercialCode,
                finish.Material)
            : null;
}

public sealed record RequirementTechnicalProposalItemSelectionReadModel(
    Guid TechnicalProposalId,
    Guid ItemId,
    string SelectionState,
    DateTimeOffset? SelectedAtUtc,
    Guid? SelectedByUserId,
    RequirementTechnicalProposalSystemOptionReadModel? System,
    RequirementTechnicalProposalGlassOptionReadModel? Glass,
    RequirementTechnicalProposalFinishOptionReadModel? Finish);
