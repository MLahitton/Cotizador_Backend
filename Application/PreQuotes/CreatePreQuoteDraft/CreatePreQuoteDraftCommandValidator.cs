using FluentValidation;
namespace Application.PreQuotes.CreatePreQuoteDraft;
public sealed class CreatePreQuoteDraftCommandValidator
    : AbstractValidator<CreatePreQuoteDraftCommand>
{
    public CreatePreQuoteDraftCommandValidator()
    {
        RuleFor(x => x.PreQuoteId).NotEmpty();
        RuleFor(x => x.SourceDocumentId).NotEmpty();
        RuleFor(x => x.SourceStructuredExtractionId).NotEmpty();
    }
}
