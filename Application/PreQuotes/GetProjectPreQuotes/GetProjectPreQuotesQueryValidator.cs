using FluentValidation;

namespace Application.PreQuotes.GetProjectPreQuotes;

public sealed class GetProjectPreQuotesQueryValidator
    : AbstractValidator<GetProjectPreQuotesQuery>
{
    public GetProjectPreQuotesQueryValidator()
    {
        RuleFor(query => query.ProjectId)
            .NotEmpty();

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);
    }
}
