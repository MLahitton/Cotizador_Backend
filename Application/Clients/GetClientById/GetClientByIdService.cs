using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using Domain.Clients;
using FluentValidation;

namespace Application.Clients.GetClientById;

public sealed class GetClientByIdService(
    IValidator<GetClientByIdQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IClientRepository clientRepository)
{
    public async Task<GetClientByIdResult> ExecuteAsync(
        GetClientByIdQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetClientByIdResult.Failed(
                GetClientByIdFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetClientByIdResult.Failed(
                GetClientByIdFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetClientByIdResult.Failed(
                GetClientByIdFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetClientByIdResult.Failed(
                GetClientByIdFailure.InactiveUser);
        }

        Client? client;

        try
        {
            client = await clientRepository.FindByIdAsync(
                query.ClientId,
                cancellationToken);
        }
        catch (ClientQueryException)
        {
            return GetClientByIdResult.Failed(
                GetClientByIdFailure.QueryError);
        }

        if (client is null)
        {
            return GetClientByIdResult.Failed(
                GetClientByIdFailure.NotFound);
        }

        return GetClientByIdResult.Success(
            new ClientByIdResult(
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
