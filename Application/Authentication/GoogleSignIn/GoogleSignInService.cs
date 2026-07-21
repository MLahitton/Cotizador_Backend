using Application.Common.Abstractions.Authentication;
using Domain.Identity;
using FluentValidation;

namespace Application.Authentication.GoogleSignIn;

public sealed class GoogleSignInService(
    IValidator<GoogleSignInCommand> validator,
    IGoogleTokenValidator googleTokenValidator,
    IIdentityRepository identityRepository,
    IAccessTokenGenerator accessTokenGenerator)
{
    public async Task<GoogleSignInResult> ExecuteAsync(
        GoogleSignInCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GoogleSignInResult.Failed(
                GoogleSignInFailure.InvalidRequest);
        }

        GoogleIdentityData googleIdentity;

        try
        {
            googleIdentity = await googleTokenValidator.ValidateAsync(
                command.IdToken,
                cancellationToken);
        }
        catch (GoogleTokenValidationException)
        {
            return GoogleSignInResult.Failed(
                GoogleSignInFailure.InvalidGoogleToken);
        }
        catch (GoogleTokenValidationUnavailableException)
        {
            return GoogleSignInResult.Failed(
                GoogleSignInFailure.ProviderUnavailable);
        }

        var now = DateTimeOffset.UtcNow;
        var externalIdentity =
            await identityRepository.FindExternalIdentityForUpdateAsync(
                ExternalIdentityProvider.Google,
                googleIdentity.Subject,
                cancellationToken);

        User user;
        var isNewUser = false;

        if (externalIdentity is not null)
        {
            user = externalIdentity.User;

            if (!user.IsActive)
            {
                return GoogleSignInResult.Failed(
                    GoogleSignInFailure.InactiveUser);
            }

            user.RegisterGoogleLogin(
                googleIdentity.Email,
                googleIdentity.FirstName,
                googleIdentity.LastName,
                googleIdentity.ProfilePictureUrl,
                now);

            externalIdentity.RegisterUse(
                googleIdentity.Email,
                googleIdentity.EmailVerified,
                now);
        }
        else
        {
            user = await identityRepository
                .FindUserByNormalizedEmailForUpdateAsync(
                    googleIdentity.Email,
                    cancellationToken)
                ?? User.CreateFromGoogle(
                    googleIdentity.Email,
                    googleIdentity.FirstName,
                    googleIdentity.LastName,
                    googleIdentity.ProfilePictureUrl,
                    now);

            if (!user.IsActive)
            {
                return GoogleSignInResult.Failed(
                    GoogleSignInFailure.InactiveUser);
            }

            isNewUser = user.CreatedAtUtc == now;

            if (isNewUser)
            {
                identityRepository.Add(user);
            }
            else
            {
                user.RegisterGoogleLogin(
                    googleIdentity.Email,
                    googleIdentity.FirstName,
                    googleIdentity.LastName,
                    googleIdentity.ProfilePictureUrl,
                    now);
            }

            externalIdentity = ExternalIdentity.CreateGoogleIdentity(
                user.Id,
                googleIdentity.Subject,
                googleIdentity.Email,
                googleIdentity.EmailVerified,
                now);

            identityRepository.Add(externalIdentity);
        }

        try
        {
            await identityRepository.SaveChangesAsync(cancellationToken);
        }
        catch (IdentityConflictException)
        {
            return GoogleSignInResult.Failed(
                GoogleSignInFailure.IdentityConflict);
        }
        catch (IdentityPersistenceException)
        {
            return GoogleSignInResult.Failed(
                GoogleSignInFailure.PersistenceError);
        }

        var accessToken = accessTokenGenerator.Generate(user);

        return GoogleSignInResult.Success(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            isNewUser,
            new GoogleSignInUserResult(
                user.Id,
                user.Email,
                user.FirstName,
                user.LastName,
                user.ProfilePictureUrl,
                user.IsActive));
    }
}
