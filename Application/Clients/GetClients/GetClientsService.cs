using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.Clients;
using FluentValidation;

namespace Application.Clients.GetClients;

public sealed class GetClientsService(
    IValidator<GetClientsQuery> validator,
    ICurrentUser currentUser,
    IIdentityRepository identityRepository,
    IClientRepository clientRepository)
{
    public async Task<GetClientsResult> ExecuteAsync(
        GetClientsQuery query,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(
            query,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            return GetClientsResult.Failed(
                GetClientsFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetClientsResult.Failed(
                GetClientsFailure.Unauthorized);
        }

        var user = await identityRepository.FindUserByIdAsync(
            userId,
            cancellationToken);

        if (user is null)
        {
            return GetClientsResult.Failed(
                GetClientsFailure.Unauthorized);
        }

        if (!user.IsActive)
        {
            return GetClientsResult.Failed(
                GetClientsFailure.InactiveUser);
        }

        var search = string.IsNullOrWhiteSpace(query.Search)
            ? null
            : query.Search.Trim();

        var normalizedStatus = string.IsNullOrWhiteSpace(query.Status)
            ? "active"
            : query.Status.Trim().ToLowerInvariant();

        bool? isActive = normalizedStatus switch
        {
            "active" => true,
            "inactive" => false,
            "all" => null,
            _ => throw new InvalidOperationException(
                "El estado de cliente validado no es reconocido.")
        };

        ClientSearchPage clientsPage;

        try
        {
            clientsPage = await clientRepository.SearchAsync(
                search,
                isActive,
                query.Page,
                query.PageSize,
                cancellationToken);
        }
        catch (ClientQueryException)
        {
            return GetClientsResult.Failed(
                GetClientsFailure.QueryError);
        }

        var totalPages = clientsPage.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(
                clientsPage.TotalCount / (double)query.PageSize);

        var items = clientsPage.Items
            .Select(client => new ClientListItemResult(
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
                client.UpdatedAtUtc))
            .ToArray();

        return GetClientsResult.Success(
            new ClientsPageResult(
                items,
                query.Page,
                query.PageSize,
                clientsPage.TotalCount,
                totalPages));
    }
}
