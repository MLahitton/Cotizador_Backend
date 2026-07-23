using FluentValidation;

namespace Application.PreQuotes.CreatePreQuote;

public sealed class CreatePreQuoteCommandValidator
    : AbstractValidator<CreatePreQuoteCommand>
{
    public CreatePreQuoteCommandValidator()
    {
        RuleFor(command => command.ProjectId)
            .NotEqual(Guid.Empty);
    }
}
