using Domain.Identity;
using FluentValidation;

namespace Application.Administration.GetAdminUsers;

public sealed class GetAdminUsersQueryValidator
    : AbstractValidator<GetAdminUsersQuery>
{
    public GetAdminUsersQueryValidator()
    {
        RuleFor(query => query.Search)
            .Must(value =>
                string.IsNullOrWhiteSpace(value)
                || value.Trim().Length <= 200);

        RuleFor(query => query.Status)
            .Must(BeValidStatus);

        RuleFor(query => query.Role)
            .Must(BeValidRole);

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);
    }

    private static bool BeValidStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant()
            is "active" or "inactive" or "all";
    }

    private static bool BeValidRole(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || Enum.TryParse<UserRole>(
                value.Trim(),
                true,
                out var parsed)
            && Enum.IsDefined(parsed);
    }
}