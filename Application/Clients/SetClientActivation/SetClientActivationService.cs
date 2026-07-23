using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Domain.Clients;
using FluentValidation;

namespace Application.Clients.SetClientActivation;

public sealed class SetClientActivationService(
    IValidator<SetClientActivationCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IClientRepository clientRepository)
{
    public async Task<SetClientActivationResult> ExecuteAsync(
        SetClientActivationCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return SetClientActivationResult.Failed(
                SetClientActivationFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return SetClientActivationResult.Failed(
                SetClientActivationFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return SetClientActivationResult.Failed(
                SetClientActivationFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return SetClientActivationResult.Failed(
                SetClientActivationFailure.InactiveUser);
        }

        Client? client;

        try
        {
            client = await clientRepository.FindForUpdateByIdAsync(
                command.ClientId,
                cancellationToken);
        }
        catch (ClientQueryException)
        {
            return SetClientActivationResult.Failed(
                SetClientActivationFailure.QueryError);
        }

        if (client is null)
        {
            return SetClientActivationResult.Failed(
                SetClientActivationFailure.NotFound);
        }

        var desiredState = command.IsActive!.Value;
        var stateChanged = client.IsActive != desiredState;
        var now = DateTimeOffset.UtcNow;

        client.SetActive(
            desiredState,
            user.Id,
            now);

        if (stateChanged)
        {
            try
            {
                await clientRepository.SaveChangesAsync(
                    cancellationToken);
            }
            catch (ClientPersistenceException)
            {
                return SetClientActivationResult.Failed(
                    SetClientActivationFailure.PersistenceError);
            }
            catch (ClientConflictException)
            {
                return SetClientActivationResult.Failed(
                    SetClientActivationFailure.PersistenceError);
            }
        }

        return SetClientActivationResult.Success(
            new ClientActivationResult(
                client.Id,
                client.ClientType,
                client.LegalName,
                client.TradeName,
                client.DocumentType,
                client.DocumentNumber,
                client.Email,
                client.Phone,
                client.Address,
                client.City,
                client.IsActive,
                client.CreatedAtUtc,
                client.UpdatedAtUtc));
    }
}
