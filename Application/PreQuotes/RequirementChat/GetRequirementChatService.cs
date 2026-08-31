using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetRequirementDetails;
using Domain.PreQuotes;

namespace Application.PreQuotes.RequirementChat;

public sealed record GetRequirementChatCommand(
    Guid RequirementId,
    Guid? TechnicalProposalItemId);

public enum RequirementChatFailure
{
    None = 0,
    InvalidRequest,
    InvalidMessage,
    Unauthorized,
    InactiveUser,
    RequirementNotFound,
    ItemNotFound,
    PreQuoteNotFound,
    ProjectNotFound,
    InactiveProject,
    ClientNotFound,
    InactiveClient,
    TechnicalProposalNotFound,
    Ai2Unavailable,
    PersistenceError,
    QueryError
}

public sealed record GetRequirementChatResult(
    bool IsSuccess,
    RequirementChatFailure Failure,
    RequirementChatThreadReadModel? Thread)
{
    public static GetRequirementChatResult Success(
        RequirementChatThreadReadModel thread) =>
        new(true, RequirementChatFailure.None, thread);

    public static GetRequirementChatResult Failed(RequirementChatFailure failure) =>
        new(false, failure, null);
}

public sealed class GetRequirementChatService(
    ICurrentUser currentUser,
    IRequirementChatRepository chatRepository,
    GetRequirementDetailsService getRequirementDetailsService,
    GetRequirementTechnicalProposal.GetRequirementTechnicalProposalService
        getTechnicalProposalService,
    TimeProvider timeProvider)
{
    public async Task<GetRequirementChatResult> ExecuteAsync(
        GetRequirementChatCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty
            || command.TechnicalProposalItemId == Guid.Empty)
        {
            return GetRequirementChatResult.Failed(
                RequirementChatFailure.InvalidRequest);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return GetRequirementChatResult.Failed(
                RequirementChatFailure.Unauthorized);
        }

        var access = await ValidateAccessAsync(
            command.RequirementId,
            command.TechnicalProposalItemId,
            cancellationToken);
        if (access != RequirementChatFailure.None)
        {
            return GetRequirementChatResult.Failed(access);
        }

        try
        {
            var scope = command.TechnicalProposalItemId is null
                ? RequirementChatScope.Requirement
                : RequirementChatScope.Item;
            var now = timeProvider.GetUtcNow();
            var thread = await chatRepository.FindThreadAsync(
                command.RequirementId,
                scope,
                command.TechnicalProposalItemId,
                cancellationToken);
            if (thread is null)
            {
                thread = RequirementChatThread.Create(
                    command.RequirementId,
                    scope,
                    command.TechnicalProposalItemId,
                    userId,
                    now);
                chatRepository.AddThread(thread);
                await chatRepository.SaveChangesAsync(cancellationToken);
            }

            var messages = await chatRepository.ListMessagesAsync(
                thread.Id,
                cancellationToken);
            return GetRequirementChatResult.Success(MapThread(thread, messages));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return GetRequirementChatResult.Failed(
                RequirementChatFailure.QueryError);
        }
    }

    private async Task<RequirementChatFailure> ValidateAccessAsync(
        Guid requirementId,
        Guid? technicalProposalItemId,
        CancellationToken cancellationToken)
    {
        var requirement = await getRequirementDetailsService.ExecuteAsync(
            new GetRequirementDetailsCommand(requirementId),
            cancellationToken);
        if (!requirement.IsSuccess)
        {
            return Map(requirement.Failure);
        }

        if (technicalProposalItemId is null)
        {
            return RequirementChatFailure.None;
        }

        var proposal = await getTechnicalProposalService.ExecuteAsync(
            new GetRequirementTechnicalProposal.GetRequirementTechnicalProposalCommand(
                requirementId),
            cancellationToken);
        if (!proposal.IsSuccess)
        {
            return proposal.Failure
                == GetRequirementTechnicalProposal
                    .GetRequirementTechnicalProposalFailure.TechnicalProposalNotFound
                    ? RequirementChatFailure.TechnicalProposalNotFound
                    : RequirementChatFailure.QueryError;
        }

        return proposal.Proposal!.Items.Any(item =>
            item.ItemId == technicalProposalItemId.Value)
                ? RequirementChatFailure.None
                : RequirementChatFailure.ItemNotFound;
    }

    internal static RequirementChatThreadReadModel MapThread(
        RequirementChatThread thread,
        IReadOnlyList<RequirementChatMessage> messages) =>
        new(
            thread.Id,
            thread.RequirementId,
            thread.TechnicalProposalItemId,
            ToContract(thread.Scope),
            thread.CreatedAtUtc,
            thread.UpdatedAtUtc,
            messages
                .OrderBy(message => message.Sequence)
                .ThenBy(message => message.CreatedAtUtc)
                .ThenBy(message => message.Id)
                .Select(MapMessage)
                .ToArray());

    internal static RequirementChatMessageReadModel MapMessage(
        RequirementChatMessage message) =>
        new(
            message.Id,
            message.Role == RequirementChatMessageRole.User
                ? "USER"
                : "ASSISTANT",
            message.Content,
            message.Sequence,
            message.CreatedAtUtc);

    internal static string ToContract(RequirementChatScope scope) =>
        scope == RequirementChatScope.Requirement ? "REQUIREMENT" : "ITEM";

    private static RequirementChatFailure Map(GetRequirementDetailsFailure failure) =>
        failure switch
        {
            GetRequirementDetailsFailure.InvalidRequest =>
                RequirementChatFailure.InvalidRequest,
            GetRequirementDetailsFailure.Unauthorized =>
                RequirementChatFailure.Unauthorized,
            GetRequirementDetailsFailure.InactiveUser =>
                RequirementChatFailure.InactiveUser,
            GetRequirementDetailsFailure.RequirementNotFound =>
                RequirementChatFailure.RequirementNotFound,
            GetRequirementDetailsFailure.PreQuoteNotFound =>
                RequirementChatFailure.PreQuoteNotFound,
            GetRequirementDetailsFailure.ProjectNotFound =>
                RequirementChatFailure.ProjectNotFound,
            GetRequirementDetailsFailure.InactiveProject =>
                RequirementChatFailure.InactiveProject,
            GetRequirementDetailsFailure.ClientNotFound =>
                RequirementChatFailure.ClientNotFound,
            GetRequirementDetailsFailure.InactiveClient =>
                RequirementChatFailure.InactiveClient,
            _ => RequirementChatFailure.QueryError
        };
}
