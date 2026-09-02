using Domain.PreQuotes;
using FluentValidation;

namespace Application.PreQuotes.UpdatePreQuoteName;

public sealed class UpdatePreQuoteNameCommandValidator
    : AbstractValidator<UpdatePreQuoteNameCommand>
{
    public UpdatePreQuoteNameCommandValidator()
    {
        RuleFor(command => command.PreQuoteId)
            .NotEmpty();

        RuleFor(command => command.Name)
            .MaximumLength(PreQuote.MaxNameLength)
            .When(command => command.Name is not null);
    }
}