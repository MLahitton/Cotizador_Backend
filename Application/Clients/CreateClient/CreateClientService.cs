using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Domain.Clients;
using FluentValidation;

namespace Application.Clients.CreateClient;

public sealed class CreateClientService(
    IValidator<CreateClientCommand> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IClientRepository clientRepository)
{
    public async Task<CreateClientResult> ExecuteAsync(
        CreateClientCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            command,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return CreateClientResult.Failed(
                CreateClientFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return CreateClientResult.Failed(
                CreateClientFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return CreateClientResult.Failed(
                CreateClientFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return CreateClientResult.Failed(
                CreateClientFailure.InactiveUser);
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

        if (documentType is ClientDocumentType existingDocumentType
            && await clientRepository.ExistsByDocumentAsync(
                existingDocumentType,
                documentNumber!,
                cancellationToken))
        {
            return CreateClientResult.Failed(
                CreateClientFailure.DuplicateDocument);
        }

        var now = DateTimeOffset.UtcNow;

        var client = Client.Create(
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

        clientRepository.Add(client);

        try
        {
            await clientRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch (ClientConflictException)
        {
            return CreateClientResult.Failed(
                CreateClientFailure.DuplicateDocument);
        }
        catch (ClientPersistenceException)
        {
            return CreateClientResult.Failed(
                CreateClientFailure.PersistenceError);
        }

        return CreateClientResult.Success(
            new CreatedClientResult(
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

    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}