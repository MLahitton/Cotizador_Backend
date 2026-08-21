using Domain.Catalogs;

namespace Application.Common.Abstractions.Catalogs;

public sealed record ProductSystemCatalogReadModel(
    Guid Id,
    string Code,
    string Name,
    string? TechnicalName,
    string? CommercialName,
    string? FunctionalType,
    string? Family,
    string? Series,
    string? CommercialLine,
    string? Variant,
    bool IsSelectable,
    bool ActiveForRecognition,
    bool Priceable,
    bool FuturePriceable,
    bool RequiresReview,
    bool IsActive,
    IReadOnlyList<ProductSystemConstraintCatalogReadModel> Constraints)
{
    public ProductSystemCatalogReadModel(
        Guid id,
        string code,
        string name,
        string? technicalName,
        string? commercialName,
        string? functionalType,
        string? family,
        string? series,
        string? commercialLine,
        string? variant,
        bool isSelectable,
        bool activeForRecognition,
        bool priceable,
        bool futurePriceable,
        bool requiresReview,
        bool isActive)
        : this(id, code, name, technicalName, commercialName, functionalType,
            family, series, commercialLine, variant, isSelectable,
            activeForRecognition, priceable, futurePriceable, requiresReview,
            isActive, [])
    {
    }
}

public sealed record ProductSystemConstraintCatalogReadModel(
    Guid Id,
    Guid ProductSystemId,
    string Code,
    ProductSystemConstraintType ConstraintType,
    ProductSystemConstraintScope Scope,
    ConstraintEvaluationStage EvaluationStage,
    ProductSystemConstraintSeverity Severity,
    ProductSystemConstraintKnowledgeClass KnowledgeClass,
    decimal? MinValue,
    decimal? MaxValue,
    string? TextValue,
    IReadOnlyList<string> AllowedValues,
    string? Unit,
    bool RequiresReviewWhenUnknown,
    bool IsActive,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    ProductSystemConstraintSourceType SourceType,
    string? SourceReference,
    string? Notes);

public sealed record FrameTypeCatalogReadModel(
    Guid Id,
    string Code,
    string Name,
    bool IsActive);

public sealed record FinishTypeCatalogReadModel(
    Guid Id,
    string Code,
    string Name,
    string? NormalizedType,
    string? Color,
    string? Texture,
    string? Process,
    string? CommercialCode,
    string? Material,
    bool IsSelectable,
    bool RequiresReview,
    bool IsActive)
{
    public FinishTypeCatalogReadModel(
        Guid id,
        string code,
        string name,
        bool requiresReview,
        bool isActive)
        : this(id, code, name, null, null, null, null, null, null, true,
            requiresReview, isActive)
    {
    }
}

public sealed record CatalogAliasReadModel(
    Guid Id,
    CatalogAliasCategory Category,
    string Alias,
    string NormalizedAlias,
    string CanonicalCode,
    CatalogAliasMatchPolicy MatchPolicy,
    bool RequiresContext,
    decimal Confidence,
    bool IsActive);

public interface IProductSystemCatalogRepository
{
    Task<IReadOnlyList<ProductSystemCatalogReadModel>> ListActiveAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductSystemCatalogReadModel>> ListActiveSelectableAsync(
        CancellationToken cancellationToken);

    Task<ProductSystemCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken);
}

public interface IFrameTypeCatalogRepository
{
    Task<IReadOnlyList<FrameTypeCatalogReadModel>> ListActiveAsync(
        CancellationToken cancellationToken);

    Task<FrameTypeCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken);
}

public interface IFinishTypeCatalogRepository
{
    Task<IReadOnlyList<FinishTypeCatalogReadModel>> ListActiveAsync(
        CancellationToken cancellationToken);

    Task<FinishTypeCatalogReadModel?> FindActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken);
}

public interface ICatalogAliasRepository
{
    Task<IReadOnlyList<CatalogAliasReadModel>> ListActiveAsync(
        CancellationToken cancellationToken);

    Task<CatalogAliasReadModel?> FindActiveByNormalizedAliasAsync(
        CatalogAliasCategory category,
        string normalizedAlias,
        CancellationToken cancellationToken);
}

public sealed class CanonicalCatalogQueryException : Exception
{
    public CanonicalCatalogQueryException(Exception innerException)
        : base("No fue posible consultar el catalogo canonico.", innerException)
    {
    }
}
