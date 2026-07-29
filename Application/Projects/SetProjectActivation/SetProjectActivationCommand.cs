namespace Application.Projects.SetProjectActivation;

public sealed record SetProjectActivationCommand(
    Guid ProjectId,
    bool? IsActive);
