using FluentValidation;

namespace Application.Authentication.GoogleSignIn;

public sealed class GoogleSignInCommandValidator
    : AbstractValidator<GoogleSignInCommand>
{
    public GoogleSignInCommandValidator()
    {
        RuleFor(command => command.IdToken)
            .NotEmpty();
    }
}
