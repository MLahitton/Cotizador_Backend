namespace Application.PreQuotes.GetRequirementTechnicalProposal;

public sealed record RequirementTechnicalProposalVisualModelReadModel(
    string Version,
    string Source,
    RequirementTechnicalProposalVisualSystemReadModel? System,
    string? FunctionalType,
    string? Operation,
    string? GeometryType,
    int? WidthMm,
    int? HeightMm,
    int? Quantity,
    IReadOnlyList<RequirementTechnicalProposalVisualPanelReadModel> Panels,
    IReadOnlyList<RequirementTechnicalProposalVisualDivisionReadModel> Divisions,
    IReadOnlyList<string> SpecialFeatures,
    bool RequiresReview,
    IReadOnlyList<string> ReviewReasons);

public sealed record RequirementTechnicalProposalVisualSystemReadModel(
    Guid Id,
    string Code,
    string DisplayName);

public sealed record RequirementTechnicalProposalVisualPanelReadModel(
    int Index,
    string Kind,
    string Role,
    string? Operation,
    int? WidthMm,
    int? HeightMm,
    decimal? WidthRatio,
    decimal? HeightRatio,
    bool? IsMovable,
    string? OpeningDirection,
    decimal? Confidence,
    IReadOnlyList<RequirementTechnicalProposalVisualPanelReadModel>
        SubPanels);

public sealed record RequirementTechnicalProposalVisualDivisionReadModel(
    string Orientation,
    decimal? PositionRatio,
    int? PositionMm,
    string? Source);
