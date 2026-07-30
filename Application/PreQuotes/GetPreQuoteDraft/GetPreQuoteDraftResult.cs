using Domain.PreQuotes;
namespace Application.PreQuotes.GetPreQuoteDraft;
public sealed record GetPreQuoteDraftResult(PreQuoteDraftFailure Failure, PreQuoteDraft? Draft)
{
    public bool IsSuccess => Failure == PreQuoteDraftFailure.None && Draft is not null;
    public static GetPreQuoteDraftResult Success(PreQuoteDraft value) => new(PreQuoteDraftFailure.None, value);
    public static GetPreQuoteDraftResult Failed(PreQuoteDraftFailure value) => new(value, null);
}
