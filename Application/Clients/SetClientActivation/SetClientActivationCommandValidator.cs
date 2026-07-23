using FluentValidation;

namespace Application.Clients.SetClientActivation;

public sealed class SetClientActivationCommandValidator
    : AbstractValidator<SetClientActivationCommand>
{
    public SetClientActivationCommandValidator()
    {
        RuleFor(command => command.ClientId)
            .NotEmpty();

        RuleFor(command => command.IsActive)
            .NotNull();
    }
}
