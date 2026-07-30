using Domain.PreQuotes;
namespace Application.PreQuotes.UpdatePreQuoteDraft;
public sealed record UpdatePreQuoteDraftResult(PreQuoteDraftFailure Failure, PreQuoteDraft? Draft)
{
    public bool IsSuccess => Failure == PreQuoteDraftFailure.None && Draft is not null;
    public static UpdatePreQuoteDraftResult Success(PreQuoteDraft value) => new(PreQuoteDraftFailure.None, value);
    public static UpdatePreQuoteDraftResult Failed(PreQuoteDraftFailure value) => new(value, null);
}
