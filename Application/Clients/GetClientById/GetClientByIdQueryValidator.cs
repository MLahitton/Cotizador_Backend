using FluentValidation;

namespace Application.Clients.GetClientById;

public sealed class GetClientByIdQueryValidator
    : AbstractValidator<GetClientByIdQuery>
{
    public GetClientByIdQueryValidator()
    {
        RuleFor(query => query.ClientId)
            .NotEmpty();
    }
}
