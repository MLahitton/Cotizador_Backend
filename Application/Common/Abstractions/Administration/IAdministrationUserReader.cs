using Domain.Identity;

namespace Application.Common.Abstractions.Administration;

public interface IAdministrationUserReader
{
    Task<AdministrationUserSearchPage> SearchAsync(
        AdministrationUserSearchCriteria criteria,
        CancellationToken cancellationToken);
}

public sealed record AdministrationUserSearchCriteria(
    string? Search,
    bool? IsActive,
    UserRole? Role,
    int Page,
    int PageSize);

public sealed record AdministrationUserSearchItem(
    Guid Id,
    string Email,
    string FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    bool IsActive,
    UserRole Role,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int PreQuoteCount);

public sealed record AdministrationUserSearchPage(
    IReadOnlyList<AdministrationUserSearchItem> Items,
    int TotalCount);

public sealed class AdministrationUserQueryException : Exception
{
    public AdministrationUserQueryException(Exception innerException)
        : base(
            "No fue posible consultar los usuarios administrativos.",
            innerException)
    {
    }
}