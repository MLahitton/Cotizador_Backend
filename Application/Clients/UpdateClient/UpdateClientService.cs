using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Domain.Clients;
using FluentValidation;

namespace Application.Clients.UpdateClient;

public sealed class UpdateClientService(
    IValidator<UpdateClientCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IClientRepository clientRepository)
{
    public async Task<UpdateClientResult> ExecuteAsync(
        UpdateClientCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return UpdateClientResult.Failed(
                UpdateClientFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return UpdateClientResult.Failed(
                UpdateClientFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return UpdateClientResult.Failed(
                UpdateClientFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return UpdateClientResult.Failed(
                UpdateClientFailure.InactiveUser);
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
            return UpdateClientResult.Failed(
                UpdateClientFailure.QueryError);
        }

        if (client is null)
        {
            return UpdateClientResult.Failed(
                UpdateClientFailure.NotFound);
        }

        var clientType = Enum.Parse<ClientType>(
            command.ClientType!.Trim(),
            ignoreCase: true);

        var documentTypeValue = NormalizeOptional(
            command.DocumentType);

        ClientDocumentType? documentType =
            documentTypeValue is null
                ? null
                : Enum.Parse<ClientDocumentType>(
                    documentTypeValue,
                    ignoreCase: true);

        var documentNumber = NormalizeOptional(
            command.DocumentNumber);

        if (documentType is ClientDocumentType existingDocumentType)
        {
            bool documentExists;

            try
            {
                documentExists =
                    await clientRepository
                        .ExistsByDocumentForOtherClientAsync(
                            client.Id,
                            existingDocumentType,
                            documentNumber!,
                            cancellationToken);
            }
            catch (ClientQueryException)
            {
                return UpdateClientResult.Failed(
                    UpdateClientFailure.QueryError);
            }

            if (documentExists)
            {
                return UpdateClientResult.Failed(
                    UpdateClientFailure.DuplicateDocument);
            }
        }

        var now = DateTimeOffset.UtcNow;

        client.UpdateDetails(
            clientType,
            command.LegalName!,
            NormalizeOptional(command.TradeName),
            documentType,
            documentNumber,
            NormalizeOptional(command.Email),
            NormalizeOptional(command.Phone),
            NormalizeOptional(command.Address),
            NormalizeOptional(command.City),
            user.Id,
            now);

        try
        {
            await clientRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch (ClientConflictException)
        {
            return UpdateClientResult.Failed(
                UpdateClientFailure.DuplicateDocument);
        }
        catch (ClientPersistenceException)
        {
            return UpdateClientResult.Failed(
                UpdateClientFailure.PersistenceError);
        }

        return UpdateClientResult.Success(
            new UpdatedClientResult(
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

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
