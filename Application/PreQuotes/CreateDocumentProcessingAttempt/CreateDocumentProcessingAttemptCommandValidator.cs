using FluentValidation;

namespace Application.PreQuotes.CreateDocumentProcessingAttempt;

public sealed class CreateDocumentProcessingAttemptCommandValidator
    : AbstractValidator<CreateDocumentProcessingAttemptCommand>
{
    public CreateDocumentProcessingAttemptCommandValidator()
    {
        RuleFor(command => command.DocumentId)
            .NotEmpty();
    }
}
