namespace Contracts.Clients;

public sealed record SetClientActivationRequest(
    bool? IsActive);
