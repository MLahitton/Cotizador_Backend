using FluentValidation;
namespace Application.PreQuotes.GetPreQuoteDraft;
public sealed class GetPreQuoteDraftQueryValidator : AbstractValidator<GetPreQuoteDraftQuery>
{
    public GetPreQuoteDraftQueryValidator() => RuleFor(x => x.PreQuoteId).NotEmpty();
}
