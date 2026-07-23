using FluentValidation;

namespace Application.Projects.GetClientProjects;

public sealed class GetClientProjectsQueryValidator
    : AbstractValidator<GetClientProjectsQuery>
{
    public GetClientProjectsQueryValidator()
    {
        RuleFor(query => query.ClientId)
            .NotEmpty();

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(query => query.Search)
            .Must(value =>
                string.IsNullOrWhiteSpace(value)
                || value.Trim().Length <= 200);
    }
}
