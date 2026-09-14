namespace Application.Common.Abstractions.Administration;

public interface IAdministrationPreQuoteReader
{
    Task<AdministrationPreQuoteSearchPage> SearchAsync(
        AdministrationPreQuoteSearchCriteria criteria,
        CancellationToken cancellationToken);
}

public sealed record AdministrationPreQuoteSearchCriteria(
    string? Search,
    Guid? UserId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Page,
    int PageSize);

public sealed record AdministrationPreQuoteUser(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName);

public sealed record AdministrationPreQuoteProject(
    Guid Id,
    string Code,
    string Name);

public sealed record AdministrationPreQuoteSearchItem(
    Guid Id,
    Guid ProjectId,
    string Serial,
    string? Name,
    int DocumentCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    AdministrationPreQuoteUser CreatedBy,
    AdministrationPreQuoteProject Project,
    bool HasRequirement,
    Guid? LatestRequirementId,
    string? LatestRequirementStatus,
    bool HasTechnicalProposal,
    Guid? TechnicalProposalId,
    int? TechnicalProposalItemCount,
    string? LatestAttemptState,
    string? LatestAttemptOutcome,
    string? LatestAttemptErrorCode);

public sealed record AdministrationPreQuoteSearchPage(
    IReadOnlyList<AdministrationPreQuoteSearchItem> Items,
    int TotalCount);

public sealed class AdministrationPreQuoteQueryException : Exception
{
    public AdministrationPreQuoteQueryException(Exception innerException)
        : base(
            "No fue posible consultar las precotizaciones administrativas.",
            innerException)
    {
    }
}