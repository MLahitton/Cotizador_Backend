using System.Diagnostics;
using Application.Common.Abstractions.Authentication;
using FluentValidation;

namespace Application.PreQuotes.ProcessRequirement;

public sealed class ProcessRequirementService(
    IValidator<ProcessRequirementCommand> validator,
    ICurrentUser currentUser,
    ProcessRequirementCoreService core)
{
    public async Task<ProcessRequirementResult> ExecuteAsync(
        ProcessRequirementCommand command, CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return ProcessRequirementResult.Failed(ProcessRequirementFailure.InvalidRequest);
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId)
            return ProcessRequirementResult.Failed(ProcessRequirementFailure.Unauthorized);

        // Identity, active-user checks and domain ownership are shared with internal callers.
        return await core.ExecuteAsync(command.RequirementId, userId, totalStopwatch, cancellationToken);
    }
}
