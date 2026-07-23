using FluentValidation;

namespace Application.Projects.CreateProject;

public sealed class CreateProjectCommandValidator
    : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(command => command.ClientId)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEqual(Guid.Empty);

        RuleFor(command => command.Code)
            .Must(BeValidProjectCode);

        RuleFor(command => command.Name)
            .Cascade(CascadeMode.Stop)
            .Must(HasValue)
            .Must(value => HasMaximumTrimmedLength(value, 200));

        RuleFor(command => command.Description)
            .Must(value => HasMaximumTrimmedLength(value, 1000));

        RuleFor(command => command.Location)
            .Must(value => HasMaximumTrimmedLength(value, 250));
    }

    private static bool BeValidProjectCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();

        return normalized.Length <= 30
            && normalized.All(character =>
                character is >= 'A' and <= 'Z'
                || character is >= 'a' and <= 'z'
                || character is >= '0' and <= '9'
                || character == '-'
                || character == '_');
    }

    private static bool HasValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool HasMaximumTrimmedLength(
        string? value,
        int maximumLength)
    {
        return string.IsNullOrWhiteSpace(value)
            || value.Trim().Length <= maximumLength;
    }
}
