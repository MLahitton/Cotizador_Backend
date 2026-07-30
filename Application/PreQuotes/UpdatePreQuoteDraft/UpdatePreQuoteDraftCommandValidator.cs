using FluentValidation;
namespace Application.PreQuotes.UpdatePreQuoteDraft;
public sealed class UpdatePreQuoteDraftCommandValidator : AbstractValidator<UpdatePreQuoteDraftCommand>
{
    public UpdatePreQuoteDraftCommandValidator()
    {
        RuleFor(x => x.PreQuoteId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.Items).NotNull();
        RuleFor(x => x.Requirements).NotNull();
        RuleFor(x => x.DocumentReferences).NotNull();
        RuleFor(x => x.Issues).NotNull();
        RuleFor(x => x.Conflicts).NotNull();
        RuleForEach(x => x.Issues)
            .Must(BeValidResolution);
        RuleForEach(x => x.Conflicts)
            .Must(BeValidResolution);
        RuleFor(x => x.Issues)
            .Must(HaveUniqueIds);
        RuleFor(x => x.Conflicts)
            .Must(HaveUniqueIds);
    }

    private static bool BeValidResolution(
        Domain.PreQuotes.PreQuoteDraftResolutionEdit resolution) =>
        resolution.Id != Guid.Empty
        && Enum.IsDefined(resolution.Status)
        && (resolution.Status
                == Domain.PreQuotes.PreQuoteDraftResolutionStatus.Pending
            || !string.IsNullOrWhiteSpace(resolution.Note));

    private static bool HaveUniqueIds(
        IReadOnlyList<Domain.PreQuotes.PreQuoteDraftResolutionEdit> values) =>
        values.Select(x => x.Id).Distinct().Count() == values.Count;
}
