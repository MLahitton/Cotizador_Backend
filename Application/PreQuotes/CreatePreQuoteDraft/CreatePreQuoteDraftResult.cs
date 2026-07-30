using Domain.PreQuotes;
namespace Application.PreQuotes.CreatePreQuoteDraft;
public sealed record CreatePreQuoteDraftResult(
    PreQuoteDraftFailure Failure, PreQuoteDraft? Draft)
{
    public bool IsSuccess => Failure == PreQuoteDraftFailure.None && Draft is not null;
    public static CreatePreQuoteDraftResult Success(PreQuoteDraft value) => new(PreQuoteDraftFailure.None, value);
    public static CreatePreQuoteDraftResult Failed(PreQuoteDraftFailure value) => new(value, null);
}
