using Domain.PreQuotes;
namespace Application.PreQuotes.ApprovePreQuoteDraft;
public sealed record ApprovePreQuoteDraftResult(PreQuoteDraftFailure Failure, PreQuoteDraft? Draft)
{
    public bool IsSuccess => Failure == PreQuoteDraftFailure.None && Draft is not null;
    public static ApprovePreQuoteDraftResult Success(PreQuoteDraft value) => new(PreQuoteDraftFailure.None, value);
    public static ApprovePreQuoteDraftResult Failed(PreQuoteDraftFailure value) => new(value, null);
}
