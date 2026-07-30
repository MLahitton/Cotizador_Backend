using FluentValidation;

namespace Application.PreQuotes.GetPreQuoteDocuments;

public sealed class GetPreQuoteDocumentsQueryValidator
    : AbstractValidator<GetPreQuoteDocumentsQuery>
{
    public GetPreQuoteDocumentsQueryValidator()
    {
        RuleFor(query => query.PreQuoteId).NotEmpty();
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
    }
}
