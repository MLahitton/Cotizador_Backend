using FluentValidation;

namespace Application.Projects.SetProjectActivation;

public sealed class SetProjectActivationCommandValidator
    : AbstractValidator<SetProjectActivationCommand>
{
    public SetProjectActivationCommandValidator()
    {
        RuleFor(command => command.ProjectId)
            .NotEmpty();

        RuleFor(command => command.IsActive)
            .NotNull();
    }
}
