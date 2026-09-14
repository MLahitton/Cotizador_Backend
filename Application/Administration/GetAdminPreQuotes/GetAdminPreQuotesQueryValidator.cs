using FluentValidation;

namespace Application.Administration.GetAdminPreQuotes;

public sealed class GetAdminPreQuotesQueryValidator
    : AbstractValidator<GetAdminPreQuotesQuery>
{
    public GetAdminPreQuotesQueryValidator()
    {
        RuleFor(query => query.Search)
            .Must(value =>
                string.IsNullOrWhiteSpace(value)
                || value.Trim().Length <= 200);

        RuleFor(query => query.UserId)
            .Must(value => value is null || value != Guid.Empty);

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(query => query)
            .Must(query =>
                query.FromUtc is null
                || query.ToUtc is null
                || query.FromUtc <= query.ToUtc);
    }
}