using FluentValidation;

namespace Application.PreQuotes.GetPreQuoteById;

public sealed class GetPreQuoteByIdQueryValidator
    : AbstractValidator<GetPreQuoteByIdQuery>
{
    public GetPreQuoteByIdQueryValidator()
    {
        RuleFor(query => query.PreQuoteId)
            .NotEmpty();
    }
}
