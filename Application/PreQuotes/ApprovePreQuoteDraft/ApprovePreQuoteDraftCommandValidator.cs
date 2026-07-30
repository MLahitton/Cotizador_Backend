using FluentValidation;
namespace Application.PreQuotes.ApprovePreQuoteDraft;
public sealed class ApprovePreQuoteDraftCommandValidator : AbstractValidator<ApprovePreQuoteDraftCommand>
{
    public ApprovePreQuoteDraftCommandValidator()
    {
        RuleFor(x => x.PreQuoteId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
    }
}
