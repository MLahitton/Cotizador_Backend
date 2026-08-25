using FluentValidation;

namespace Application.PreQuotes.CreateRequirement;

public sealed class CreateRequirementCommandValidator
    : AbstractValidator<CreateRequirementCommand>
{
    public CreateRequirementCommandValidator()
    {
        RuleFor(command => command.PreQuoteId)
            .NotEmpty();

        RuleFor(command => command.CommercialLine)
            .NotEmpty();

        RuleFor(command => command.Files)
            .NotNull();

        RuleForEach(command => command.Files)
            .ChildRules(file =>
            {
                file.RuleFor(value => value.Content)
                    .NotNull()
                    .Must(content => content is not null && content.CanRead);
            });
    }
}
