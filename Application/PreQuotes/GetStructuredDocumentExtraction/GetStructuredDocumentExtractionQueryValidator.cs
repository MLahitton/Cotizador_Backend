using FluentValidation;

namespace Application.PreQuotes.GetStructuredDocumentExtraction;

public sealed class GetStructuredDocumentExtractionQueryValidator
    : AbstractValidator<GetStructuredDocumentExtractionQuery>
{
    public GetStructuredDocumentExtractionQueryValidator()
    {
        RuleFor(query => query.DocumentId).NotEmpty();
    }
}
