namespace Application.Clients.SetClientActivation;

public sealed record SetClientActivationCommand(
    Guid ClientId,
    bool? IsActive);
