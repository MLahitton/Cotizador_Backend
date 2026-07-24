using FluentValidation;

namespace Application.PreQuotes.CreatePreQuoteDocument;

public sealed class CreatePreQuoteDocumentCommandValidator
    : AbstractValidator<CreatePreQuoteDocumentCommand>
{
    public CreatePreQuoteDocumentCommandValidator()
    {
        RuleFor(command => command.PreQuoteId)
            .NotEmpty();

        RuleFor(command => command.Content)
            .NotNull()
            .Must(content => content is not null && content.CanRead);
    }
}
