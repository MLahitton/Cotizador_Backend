using FluentValidation;

namespace Application.Clients.GetClients;

public sealed class GetClientsQueryValidator
    : AbstractValidator<GetClientsQuery>
{
    public GetClientsQueryValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(query => query.Search)
            .Must(value =>
                string.IsNullOrWhiteSpace(value)
                || value.Trim().Length <= 200);

        RuleFor(query => query.Status)
            .Must(BeValidStatus);

        RuleFor(query => query.ClientType)
            .Must(BeValidClientType);

        RuleFor(query => query.DocumentType)
            .Must(BeValidDocumentType);

        RuleFor(query => query.DocumentNumber)
            .Must(value =>
                string.IsNullOrWhiteSpace(value)
                || value.Trim().Length <= 100);
    }

    private static bool BeValidStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalizedValue = value.Trim();

        return normalizedValue.Equals(
                "active",
                StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals(
                "inactive",
                StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Equals(
                "all",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool BeValidClientType(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || Enum.TryParse<Domain.Clients.ClientType>(
                value.Trim(),
                true,
                out var parsed)
            && Enum.IsDefined(parsed);
    }

    private static bool BeValidDocumentType(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || Enum.TryParse<Domain.Clients.ClientDocumentType>(
                value.Trim(),
                true,
                out var parsed)
            && Enum.IsDefined(parsed);
    }
}
