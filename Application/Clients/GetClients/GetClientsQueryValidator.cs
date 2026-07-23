using FluentValidation;

namespace Application.Clients.GetClients;

public sealed class GetClientsQueryValidator
    : AbstractValidator<GetClientsQuery>
{
    public GetClientsQueryValidator()
    {
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
