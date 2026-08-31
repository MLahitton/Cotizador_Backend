using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetRequirementDetails;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Domain.PreQuotes;

namespace Application.PreQuotes.RequirementChat;

public sealed record SendRequirementChatMessageCommand(
    Guid RequirementId,
    Guid? TechnicalProposalItemId,
    string Message);

public sealed record SendRequirementChatMessageResult(
    bool IsSuccess,
    RequirementChatFailure Failure,
    RequirementChatThreadReadModel? Thread)
{
    public static SendRequirementChatMessageResult Success(
        RequirementChatThreadReadModel thread) =>
        new(true, RequirementChatFailure.None, thread);

    public static SendRequirementChatMessageResult Failed(
        RequirementChatFailure failure) =>
        new(false, failure, null);
}

public sealed class SendRequirementChatMessageService(
    ICurrentUser currentUser,
    IRequirementChatRepository chatRepository,
    IRequirementChatAiClient aiClient,
    GetRequirementDetailsService getRequirementDetailsService,
    GetRequirementTechnicalProposalService getTechnicalProposalService,
    IRequirementRepository requirementRepository,
    TimeProvider timeProvider)
{
    private const int ConversationLimit = 20;

    public async Task<SendRequirementChatMessageResult> ExecuteAsync(
        SendRequirementChatMessageCommand command,
        CancellationToken cancellationToken)
    {
        if (command.RequirementId == Guid.Empty
            || command.TechnicalProposalItemId == Guid.Empty)
        {
            return SendRequirementChatMessageResult.Failed(
                RequirementChatFailure.InvalidRequest);
        }

        if (string.IsNullOrWhiteSpace(command.Message)
            || command.Message.Trim().Length
                > RequirementChatMessage.MaximumContentLength)
        {
            return SendRequirementChatMessageResult.Failed(
                RequirementChatFailure.InvalidMessage);
        }

        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid userId)
        {
            return SendRequirementChatMessageResult.Failed(
                RequirementChatFailure.Unauthorized);
        }

        var context = await BuildContextAsync(
            command.RequirementId,
            command.TechnicalProposalItemId,
            cancellationToken);
        if (context.Failure != RequirementChatFailure.None)
        {
            return SendRequirementChatMessageResult.Failed(context.Failure);
        }

        var scope = command.TechnicalProposalItemId is null
            ? RequirementChatScope.Requirement
            : RequirementChatScope.Item;

        RequirementChatThread thread;
        try
        {
            var now = timeProvider.GetUtcNow();
            thread = await chatRepository.FindThreadAsync(
                    command.RequirementId,
                    scope,
                    command.TechnicalProposalItemId,
                    cancellationToken)
                ?? RequirementChatThread.Create(
                    command.RequirementId,
                    scope,
                    command.TechnicalProposalItemId,
                    userId,
                    now);
            if (thread.CreatedAtUtc == now)
            {
                chatRepository.AddThread(thread);
            }

            var sequence = await chatRepository.GetNextSequenceAsync(
                thread.Id,
                cancellationToken);
            chatRepository.AddMessage(RequirementChatMessage.Create(
                thread.Id,
                RequirementChatMessageRole.User,
                command.Message.Trim(),
                sequence,
                now));
            thread.Touch(now);
            await chatRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return SendRequirementChatMessageResult.Failed(
                RequirementChatFailure.PersistenceError);
        }

        IReadOnlyList<RequirementChatMessage> conversation;
        try
        {
            conversation = await chatRepository.ListRecentMessagesAsync(
                thread.Id,
                ConversationLimit,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return SendRequirementChatMessageResult.Failed(
                RequirementChatFailure.QueryError);
        }

        RequirementChatAiResponse response;
        try
        {
            response = await aiClient.RespondAsync(
                new RequirementChatAiRequest(
                    GetRequirementChatService.ToContract(scope),
                    command.Message.Trim(),
                    conversation.Select(message =>
                            new RequirementChatAiConversationMessage(
                                message.Role == RequirementChatMessageRole.User
                                    ? "user"
                                    : "assistant",
                                message.Content))
                        .ToArray(),
                    context.Context!),
                cancellationToken);
        }
        catch (RequirementChatAiUnavailableException)
        {
            return SendRequirementChatMessageResult.Failed(
                RequirementChatFailure.Ai2Unavailable);
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var sequence = await chatRepository.GetNextSequenceAsync(
                thread.Id,
                cancellationToken);
            chatRepository.AddMessage(RequirementChatMessage.Create(
                thread.Id,
                RequirementChatMessageRole.Assistant,
                response.Message,
                sequence,
                now));
            thread.Touch(now);
            await chatRepository.SaveChangesAsync(cancellationToken);

            var messages = await chatRepository.ListMessagesAsync(
                thread.Id,
                cancellationToken);
            return SendRequirementChatMessageResult.Success(
                GetRequirementChatService.MapThread(thread, messages));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return SendRequirementChatMessageResult.Failed(
                RequirementChatFailure.PersistenceError);
        }
    }

    private async Task<ContextBuildResult> BuildContextAsync(
        Guid requirementId,
        Guid? technicalProposalItemId,
        CancellationToken cancellationToken)
    {
        var requirement = await getRequirementDetailsService.ExecuteAsync(
            new GetRequirementDetailsCommand(requirementId),
            cancellationToken);
        if (!requirement.IsSuccess)
        {
            return new(Map(requirement.Failure), null);
        }

        RequirementTechnicalProposalReadModel? proposal = null;
        var proposalResult = await getTechnicalProposalService.ExecuteAsync(
            new GetRequirementTechnicalProposalCommand(requirementId),
            cancellationToken);
        if (proposalResult.IsSuccess)
        {
            proposal = proposalResult.Proposal;
        }
        else if (technicalProposalItemId is not null)
        {
            return new(
                proposalResult.Failure
                    == GetRequirementTechnicalProposalFailure
                        .TechnicalProposalNotFound
                    ? RequirementChatFailure.TechnicalProposalNotFound
                    : RequirementChatFailure.QueryError,
                null);
        }

        var item = technicalProposalItemId is null
            ? null
            : proposal?.Items.SingleOrDefault(value =>
                value.ItemId == technicalProposalItemId.Value);
        if (technicalProposalItemId is not null && item is null)
        {
            return new(RequirementChatFailure.ItemNotFound, null);
        }

        var pricing = await requirementRepository.GetCurrentPricingSnapshotAsync(
            requirementId,
            cancellationToken);

        var context = new
        {
            scope = technicalProposalItemId is null ? "REQUIREMENT" : "ITEM",
            requirement = new
            {
                requirement.Requirement!.RequirementId,
                requirement.Requirement.PreQuoteId,
                status = requirement.Requirement.Status.ToString(),
                commercialLine = requirement.Requirement.CommercialLine?.ToString(),
                requirement.Requirement.IsCurrent,
                requirement.Requirement.CanEditDocuments,
                requirement.Requirement.CanCancel,
                requirement.Requirement.CanReplace,
                documents = requirement.Requirement.Documents.Select(document => new
                {
                    document.RequirementFileId,
                    document.FileName,
                    document.ContentType,
                    document.SizeBytes
                })
            },
            technicalProposal = proposal is null ? null : new
            {
                proposal.TechnicalProposalId,
                proposal.Status,
                proposal.CommercialLine,
                confirmation = proposal.CommercialConfirmation,
                proposal.ItemCount,
                proposal.ItemsRequiringReview,
                proposal.TechnicallyCompleteItems,
                proposal.PriceableItems,
                proposal.Readiness,
                items = technicalProposalItemId is null
                    ? proposal.Items.Select(ToItemContext).ToArray()
                    : null
            },
            item = item is null ? null : ToItemContext(item),
            pricing = pricing is null ? null : new
            {
                pricing.Currency,
                pricing.PricingBasis,
                pricing.OriginalGrandTotal,
                pricing.CurrentGrandTotal,
                pricing.DeltaGrandTotal,
                itemCount = pricing.Items.Count,
                items = technicalProposalItemId is null
                    ? pricing.Items.Select(ToPricingItemContext).ToArray()
                    : pricing.Items
                        .Where(value => value.TechnicalProposalItemId
                            == technicalProposalItemId.Value)
                        .Select(ToPricingItemContext)
                        .ToArray()
            },
            instructions = new[]
            {
                "READ_ONLY_CHAT",
                "USE_ONLY_CONTEXT",
                "DO_NOT_MUTATE_SELECTION",
                "DISTINGUISH_SUGGESTED_SELECTED"
            }
        };

        return new(RequirementChatFailure.None, context);
    }

    private static object ToItemContext(
        RequirementTechnicalProposalItemReadModel item) =>
        new
        {
            item.ItemId,
            item.ExtractedItemId,
            item.ElementId,
            item.Sequence,
            item.Reference,
            item.Description,
            item.ElementType,
            item.Quantity,
            item.WidthMm,
            item.HeightMm,
            item.EffectiveQuantity,
            item.EffectiveWidthMm,
            item.EffectiveHeightMm,
            item.AreaM2,
            item.ExtractionConfidence,
            item.ExtractionStatus,
            item.Suggested,
            item.Selected,
            item.SelectionState,
            alternatives = new
            {
                systems = item.Alternatives.Systems.Take(5).ToArray(),
                glass = item.Alternatives.Glass.Take(5).ToArray(),
                finishes = item.Alternatives.Finishes.Take(5).ToArray()
            },
            item.Confidence,
            item.RequiresReview,
            item.ReviewReasons,
            item.SystemResolutionReasons,
            item.GlassResolutionReasons,
            item.FinishResolutionReasons,
            item.IsTechnicallyComplete,
            item.IsPriceable,
            item.Readiness,
            item.HistoricalEvidence,
            item.Trace,
            evidence = item.Evidence.Take(8).ToArray()
        };

    private static object ToPricingItemContext(
        RequirementPricingItemSnapshot item) =>
        new
        {
            item.TechnicalProposalItemId,
            item.OriginalStatus,
            item.CurrentStatus,
            item.OriginalUnitExpected,
            item.CurrentUnitExpected,
            item.DeltaUnitExpected,
            item.OriginalLineExpected,
            item.CurrentLineExpected,
            item.DeltaLineExpected
        };

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

    private sealed record ContextBuildResult(
        RequirementChatFailure Failure,
        object? Context);
}
