using Domain.Clients;
using FluentValidation;

namespace Application.Projects.GetProjects;

public sealed class GetProjectsQueryValidator
    : AbstractValidator<GetProjectsQuery>
{
    public GetProjectsQueryValidator()
    {
        RuleFor(query => query.Search)
            .Must(value =>
                string.IsNullOrWhiteSpace(value)
                || value.Trim().Length <= 200);

        RuleFor(query => query.Status)
            .Must(BeValidStatus);

        RuleFor(query => query.ClientId)
            .Must(value => value is null || value != Guid.Empty);

        RuleFor(query => query.ClientType)
            .Must(value => BeValidEnum<ClientType>(value));

        RuleFor(query => query.DocumentType)
            .Must(value => BeValidEnum<ClientDocumentType>(value));

        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100);
    }

    private static bool BeValidStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.Trim().ToLowerInvariant()
            is "active" or "inactive" or "all";
    }

    private static bool BeValidEnum<TEnum>(string? value)
        where TEnum : struct, Enum
    {
        return string.IsNullOrWhiteSpace(value)
            || Enum.TryParse<TEnum>(value.Trim(), true, out var parsed)
            && Enum.IsDefined(parsed);
    }
}
