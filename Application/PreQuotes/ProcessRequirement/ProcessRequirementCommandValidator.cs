using FluentValidation;

namespace Application.PreQuotes.ProcessRequirement;

public sealed class ProcessRequirementCommandValidator
    : AbstractValidator<ProcessRequirementCommand>
{
    public ProcessRequirementCommandValidator()
    {
        RuleFor(command => command.RequirementId)
            .NotEmpty();
    }
}
