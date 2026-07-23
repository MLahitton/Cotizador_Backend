using Domain.Clients;
using FluentValidation;

namespace Application.Clients.CreateClient;

public sealed class CreateClientCommandValidator
    : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(command => command.ClientType)
            .Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .Must(BeValidClientType);

        RuleFor(command => command.LegalName)
            .Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrWhiteSpace(value))
            .MaximumLength(200);

        RuleFor(command => command.TradeName)
            .MaximumLength(200)
            .When(command => HasValue(command.TradeName));

        RuleFor(command => command.DocumentType)
            .Must(BeValidOptionalDocumentType);

        RuleFor(command => command.DocumentNumber)
            .MaximumLength(50)
            .When(command => HasValue(command.DocumentNumber));

        RuleFor(command => command)
            .Must(command =>
                HasValue(command.DocumentType)
                == HasValue(command.DocumentNumber));

        RuleFor(command => command.Email)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(320)
            .EmailAddress()
            .When(command => HasValue(command.Email));

        RuleFor(command => command.Phone)
            .MaximumLength(50)
            .When(command => HasValue(command.Phone));

        RuleFor(command => command.Address)
            .MaximumLength(300)
            .When(command => HasValue(command.Address));

        RuleFor(command => command.City)
            .MaximumLength(100)
            .When(command => HasValue(command.City));
    }

    private static bool BeValidClientType(string? value)
    {
        return TryParseNamedEnum<ClientType>(value, out _);
    }

    private static bool BeValidOptionalDocumentType(string? value)
    {
        return !HasValue(value)
            || TryParseNamedEnum<ClientDocumentType>(value, out _);
    }

    private static bool TryParseNamedEnum<TEnum>(
        string? value,
        out TEnum parsedValue)
        where TEnum : struct, Enum
    {
        parsedValue = default;

        if (!HasValue(value))
        {
            return false;
        }

        var normalizedValue = value!.Trim();

        return !int.TryParse(normalizedValue, out _)
            && Enum.TryParse(
                normalizedValue,
                ignoreCase: true,
                out parsedValue)
            && Enum.IsDefined(parsedValue);
    }

    private static bool HasValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }
}
