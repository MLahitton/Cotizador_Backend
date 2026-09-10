using Application.Common.Abstractions.Authentication;
using Application.Common.Abstractions.PreQuotes;
using Application.PreQuotes.GetRequirementDetails;
using Application.PreQuotes.GetRequirementTechnicalProposal;
using Application.PreQuotes.RequirementChatActions;
using Domain.PreQuotes;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace Application.PreQuotes.RequirementChat;

public sealed record SendRequirementChatMessageCommand(
    Guid RequirementId,
    Guid? TechnicalProposalItemId,
    string Message);

public sealed record SendRequirementChatMessageResult(
    bool IsSuccess,
    RequirementChatFailure Failure,
    RequirementChatThreadReadModel? Thread,
    RequirementChatInteractionReadModel? LastInteraction = null)
{
    public static SendRequirementChatMessageResult Success(
        RequirementChatThreadReadModel thread,
        RequirementChatInteractionReadModel? lastInteraction = null) =>
        new(true, RequirementChatFailure.None, thread, lastInteraction);

    public static SendRequirementChatMessageResult Failed(
        RequirementChatFailure failure) =>
        new(false, failure, null, null);
}

public sealed record RequirementChatInteractionReadModel(
    string MessageType,
    Guid? PlanId,
    bool RequiresConfirmation,
    string? ActionType,
    Guid? TargetTechnicalProposalItemId,
    string? TargetReference,
    string? CurrentValue,
    string? RequestedValue,
    string? PricingImpactExpected,
    string? PricingStatus,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<RequirementChatInteractionOptionReadModel> AvailableOptions,
    int ActionCount = 0,
    IReadOnlyList<RequirementChatInteractionActionReadModel>? Actions = null);

public sealed record RequirementChatInteractionActionReadModel(
    Guid ActionId,
    string ActionType,
    Guid? TargetTechnicalProposalItemId,
    string? TargetReference,
    string? CurrentValue,
    string? RequestedValue,
    RequirementChatInteractionResolvedCatalogEntityReadModel? ResolvedCatalogEntity,
    string ValidationState,
    IReadOnlyList<string> ValidationReasons,
    bool RequiresConfirmation,
    IReadOnlyList<RequirementChatInteractionOptionReadModel> AvailableOptions);

public sealed record RequirementChatInteractionResolvedCatalogEntityReadModel(
    Guid Id,
    string Code,
    string DisplayName,
    string EntityType);

public sealed record RequirementChatInteractionOptionReadModel(
    Guid? Id,
    string? Code,
    string DisplayName,
    string OptionType);

public sealed class SendRequirementChatMessageService(
    ICurrentUser currentUser,
    IRequirementChatRepository chatRepository,
    IRequirementChatAiClient aiClient,
    GetRequirementDetailsService getRequirementDetailsService,
    GetRequirementTechnicalProposalService getTechnicalProposalService,
    IRequirementRepository requirementRepository,
    PlanRequirementChatActionService planActionService,
    ConfirmRequirementChatActionService confirmActionService,
    IRequirementChatActionPlanStore actionPlanStore,
    TimeProvider timeProvider,
    ILogger<SendRequirementChatMessageService> logger)
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

        var scopeContract = GetRequirementChatService.ToContract(scope);
        var pendingConfirmations = actionPlanStore.FindPendingConfirmations(
            command.RequirementId,
            scopeContract,
            command.TechnicalProposalItemId,
            thread.Id);
        var confirmationFollowUp = DetectConfirmationFollowUp(command.Message);
        RequirementChatAiResponse? response = null;
        RequirementChatInteractionReadModel? interaction = null;
        if (confirmationFollowUp != ConfirmationFollowUp.None)
        {
            if (pendingConfirmations.Count == 1)
            {
                var pendingConfirmation = pendingConfirmations[0];
                if (confirmationFollowUp == ConfirmationFollowUp.Affirmative)
                {
                    var confirmResult = await confirmActionService.ExecuteAsync(
                        new ConfirmRequirementChatActionCommand(
                            command.RequirementId,
                            pendingConfirmation.PlanId),
                        cancellationToken);
                    if (!confirmResult.IsSuccess || confirmResult.Plan is null)
                    {
                        return SendRequirementChatMessageResult.Failed(
                            RequirementChatFailure.QueryError);
                    }

                    interaction = ToExecutedInteraction(confirmResult.Plan);
                    response = new RequirementChatAiResponse(
                        ToExecutedAssistantMessage(confirmResult.Plan));
                }
                else
                {
                    var cancelled = actionPlanStore.CancelPendingConfirmation(
                        command.RequirementId,
                        pendingConfirmation.PlanId);
                    interaction = cancelled is null
                        ? InformationalInteraction()
                        : ToCancelledInteraction(cancelled);
                    response = new RequirementChatAiResponse(
                        "Listo. No aplique ningun cambio.");
                }
            }
            else
            {
                interaction = InformationalInteraction();
                response = new RequirementChatAiResponse(
                    pendingConfirmations.Count == 0
                        ? "No encontre un cambio pendiente para confirmar."
                        : "Hay mas de un cambio pendiente. Abre la tarjeta correspondiente y confirma desde ahi.");
            }
        }

        var pendingPlan = actionPlanStore.FindPendingClarification(
            command.RequirementId,
            scopeContract,
            command.TechnicalProposalItemId,
            thread.Id);
        var aiContext = pendingPlan is null
            ? context.Context!
            : WithPendingAction(context.Context!, pendingPlan);
        RequirementChatActionIntent intent;
        if (response is null)
        {
            try
            {
                intent = await aiClient.InterpretActionAsync(
                new RequirementChatActionInterpretationRequest(
                    command.Message.Trim(),
                    scopeContract,
                    command.TechnicalProposalItemId,
                    conversation.Select(message =>
                            new RequirementChatAiConversationMessage(
                                message.Role == RequirementChatMessageRole.User
                                    ? "user"
                                    : "assistant",
                                message.Content))
                        .ToArray(),
                    aiContext),
                cancellationToken);
        }
            catch (RequirementChatAiUnavailableException)
            {
                return SendRequirementChatMessageResult.Failed(
                    RequirementChatFailure.Ai2Unavailable);
            }
        }
        else
        {
            intent = new RequirementChatActionIntent(false, null, null, null, null, null, null, null, 1m, false, null, command.Message.Trim());
        }

        if (response is null && !intent.IsAction)
        {
            try
            {
                logger.LogInformation(
                    "Requirement chat informational context built. RequirementId={RequirementId} TechnicalProposalId={TechnicalProposalId} IncludedItems={IncludedItems} ExcludedItems={ExcludedItems} PriceableItems={PriceableItems} PricingSnapshotId={PricingSnapshotId} CommercialRevision={CommercialRevision}",
                    command.RequirementId,
                    context.TechnicalProposalId,
                    context.IncludedItems,
                    context.ExcludedItems,
                    context.PriceableItems,
                    context.PricingSnapshotId,
                    context.CommercialRevision);

                response = await aiClient.RespondAsync(
                    new RequirementChatAiRequest(
                        scopeContract,
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

            interaction = new RequirementChatInteractionReadModel(
                "INFORMATIONAL",
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                []);
        }
        else if (response is null && (intent.RequiresClarification
            || string.Equals(intent.ActionType, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(intent.ActionType)))
        {
            var message = string.IsNullOrWhiteSpace(intent.ClarificationReason)
                ? "Necesito un poco mas de informacion para preparar esa accion."
                : intent.ClarificationReason.Trim();
            response = new RequirementChatAiResponse(message);
            interaction = new RequirementChatInteractionReadModel(
                "CLARIFICATION",
                null,
                false,
                intent.ActionType,
                null,
                intent.TargetReference,
                null,
                intent.RequestedValue,
                null,
                null,
                string.IsNullOrWhiteSpace(intent.ClarificationReason)
                    ? []
                    : [intent.ClarificationReason.Trim()],
                pendingPlan?.Actions
                    .SelectMany(value => value.AvailableOptions)
                    .Select(ToInteractionOption)
                    .ToArray() ?? []);
        }
        else if (response is null)
        {
            var pendingAction = pendingPlan?.Actions.FirstOrDefault();
            var planResult = await planActionService.ExecuteAsync(
                new PlanRequirementChatActionCommand(
                    command.RequirementId,
                    thread.Id,
                    pendingPlan?.PlanId,
                    command.TechnicalProposalItemId
                        ?? pendingAction?.TargetTechnicalProposalItemId,
                    intent.Scope ?? pendingPlan?.Scope ?? scopeContract,
                    intent.ActionType ?? pendingAction?.ActionType!,
                    null,
                    intent.TargetReference ?? pendingAction?.TargetReference,
                    intent.TargetReferences,
                    intent.RequestedValue
                        ?? pendingAction?.RequestedValue
                        ?? command.Message.Trim(),
                    intent.RequestedQuantity,
                    intent.RequestedWidthMm,
                    intent.RequestedHeightMm,
                    intent.RawUserMessage ?? command.Message.Trim(),
                    intent.RequestedAttributes),
                cancellationToken);
            if (!planResult.IsSuccess || planResult.Plan is null)
            {
                return SendRequirementChatMessageResult.Failed(
                    RequirementChatFailure.QueryError);
            }

            interaction = ToInteraction(planResult.Plan);
            response = new RequirementChatAiResponse(ToAssistantMessage(planResult.Plan));
        }

        if (response is null || interaction is null)
        {
            return SendRequirementChatMessageResult.Failed(
                RequirementChatFailure.QueryError);
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
                GetRequirementChatService.MapThread(thread, messages),
                interaction);
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
        var totalItems = proposal?.Items.Count;
        var includedItems = proposal?.Items.Count(value => value.IsIncluded);
        var excludedItems = proposal?.Items.Count(value => !value.IsIncluded);
        var priceableItems = proposal?.Items.Count(value =>
            value.IsIncluded && value.IsPriceable);
        var blockingItems = proposal?.Items.Count(IsBlockingItem);
        var excludedReferences = proposal?.Items
            .Where(value => !value.IsIncluded)
            .Select(value => value.Reference ?? value.ItemId.ToString())
            .ToArray();

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
                currentState = new
                {
                    totalItems,
                    includedItems,
                    excludedItems,
                    excludedReferences,
                    priceableItems,
                    blockingItems
                },
                items = technicalProposalItemId is null
                    ? proposal.Items.Select(ToItemContext).ToArray()
                    : null
            },
            item = item is null ? null : ToItemContext(item),
            pricing = pricing is null ? null : new
            {
                pricingSnapshotId = pricing.Id,
                technicalProposalId = pricing.TechnicalProposalId,
                commercialRevision = pricing.TechnicalProposalCommercialRevision,
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
                "USE_ONLY_CONTEXT",
                "CAN_PREPARE_SUPPORTED_ACTIONS",
                "MUTATIONS_REQUIRE_CONFIRMATION",
                "BACKEND_VALIDATES_AND_EXECUTES_ACTIONS",
                "UNSUPPORTED_ACTIONS_MUST_BE_DECLARED_UNAVAILABLE",
                "DISTINGUISH_SUGGESTED_SELECTED"
            }
        };

        return new(
            RequirementChatFailure.None,
            context,
            proposal?.TechnicalProposalId,
            includedItems,
            excludedItems,
            priceableItems,
            pricing?.Id,
            pricing?.TechnicalProposalCommercialRevision);
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
            effective = new
            {
                system = item.Selected?.System ?? item.Suggested.System,
                glass = item.Selected?.Glass ?? item.Suggested.Glass,
                finish = item.Selected?.Finish ?? item.Suggested.Finish,
                quantity = item.EffectiveQuantity,
                widthMm = item.EffectiveWidthMm,
                heightMm = item.EffectiveHeightMm,
                areaM2 = item.AreaM2
            },
            item.SelectionState,
            inclusion = new
            {
                item.IsIncluded,
                item.ExcludedAtUtc,
                item.ExcludedByUserId,
                item.ExclusionReason
            },
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

    private static bool IsBlockingItem(
        RequirementTechnicalProposalItemReadModel item) =>
        item.IsIncluded
        && (!item.IsPriceable
            || !item.IsTechnicallyComplete
            || item.RequiresReview
            || !string.Equals(
                item.Readiness.State,
                "READY",
                StringComparison.OrdinalIgnoreCase));

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
        object? Context,
        Guid? TechnicalProposalId = null,
        int? IncludedItems = null,
        int? ExcludedItems = null,
        int? PriceableItems = null,
        Guid? PricingSnapshotId = null,
        long? CommercialRevision = null);

    private enum ConfirmationFollowUp
    {
        None,
        Affirmative,
        Negative
    }

    private static ConfirmationFollowUp DetectConfirmationFollowUp(string message)
    {
        if (message.Contains('?') || message.Contains('\u00bf'))
        {
            return ConfirmationFollowUp.None;
        }

        var normalized = NormalizeConfirmationText(message);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ConfirmationFollowUp.None;
        }

        var negative = new HashSet<string>(StringComparer.Ordinal)
        {
            "no",
            "no lo hagas",
            "cancela",
            "cancelalo",
            "mejor no",
            "no gracias"
        };
        if (negative.Contains(normalized))
        {
            return ConfirmationFollowUp.Negative;
        }

        var affirmative = new HashSet<string>(StringComparer.Ordinal)
        {
            "si",
            "si hazlo",
            "hazlo",
            "confirmo",
            "adelante",
            "aplicalo",
            "aplica el cambio",
            "de acuerdo",
            "dale",
            "ok"
        };
        return affirmative.Contains(normalized)
            ? ConfirmationFollowUp.Affirmative
            : ConfirmationFollowUp.None;
    }

    private static string NormalizeConfirmationText(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = true;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static RequirementChatInteractionReadModel InformationalInteraction() =>
        new(
            "INFORMATIONAL",
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            []);

    private static RequirementChatInteractionReadModel ToCancelledInteraction(
        ChatActionPlanReadModel plan) =>
        InformationalInteraction() with
        {
            PlanId = plan.PlanId,
            Reasons = plan.ExecutionReasons,
            ActionCount = plan.Actions.Count,
            Actions = plan.Actions.Select(ToInteractionAction).ToArray()
        };

    private static RequirementChatInteractionReadModel ToExecutedInteraction(
        ChatActionPlanReadModel plan)
    {
        var primaryAction = plan.Actions.FirstOrDefault();
        var actions = plan.Actions.Select(ToInteractionAction).ToArray();
        return new RequirementChatInteractionReadModel(
            "INFORMATIONAL",
            plan.PlanId,
            false,
            primaryAction?.ActionType,
            primaryAction?.TargetTechnicalProposalItemId,
            primaryAction?.TargetReference,
            primaryAction?.CurrentValue,
            primaryAction?.RequestedValue,
            null,
            plan.PricingStatus,
            plan.ExecutionReasons
                .Concat(["CHAT_ACTION_EXECUTED"])
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            [],
            actions.Length,
            actions);
    }
    private static RequirementChatInteractionReadModel ToInteraction(
        ChatActionPlanReadModel plan)
    {
        var primaryAction = plan.Actions.FirstOrDefault();
        var actions = plan.Actions.Select(ToInteractionAction).ToArray();
        var messageType = plan.Status == "READY_FOR_CONFIRMATION"
            ? "ACTION_PLAN"
            : "CLARIFICATION";
        return new RequirementChatInteractionReadModel(
            messageType,
            plan.PlanId,
            plan.RequiresConfirmation,
            primaryAction?.ActionType,
            primaryAction?.TargetTechnicalProposalItemId,
            primaryAction?.TargetReference,
            primaryAction?.CurrentValue,
            primaryAction?.RequestedValue,
            plan.Status == "READY_FOR_CONFIRMATION" ? "POSSIBLE_REPRICE" : null,
                plan.PricingStatus,
                plan.Actions
                    .SelectMany(value => value.ValidationReasons)
                    .Concat(plan.ExecutionReasons)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                plan.Actions
                    .SelectMany(value => value.AvailableOptions)
                    .Select(ToInteractionOption)
                    .ToArray(),
                actions.Length,
                actions);
    }

    private static object WithPendingAction(object context, ChatActionPlanReadModel plan)
    {
        var primaryAction = plan.Actions.FirstOrDefault();
        return new
        {
            originalContext = context,
            pendingAction = new
            {
                plan.PlanId,
                plan.RequirementId,
                plan.TechnicalProposalId,
                plan.Scope,
                actionCount = plan.Actions.Count,
                actions = plan.Actions,
                primaryAction?.ActionType,
                primaryAction?.TargetTechnicalProposalItemId,
                primaryAction?.TargetReference,
                primaryAction?.RequestedValue,
                primaryAction?.CurrentValue,
                clarificationExpected = primaryAction is null
                    ? "unknown"
                    : ExpectedField(primaryAction),
                clarificationReason = primaryAction?.ValidationReasons.FirstOrDefault(),
                validationReasons = primaryAction?.ValidationReasons ?? [],
                availableOptions = primaryAction?.AvailableOptions ?? [],
                plan.CreatedAtUtc,
                plan.ExpiresAtUtc
            }
        };
    }

    private static string ExpectedField(ChatActionPlanActionReadModel action) =>
        action.ValidationReasons.Any(reason => reason.EndsWith(
            "_VALUE_REQUIRED",
            StringComparison.Ordinal))
            || action.ValidationReasons.Any(reason => reason.EndsWith(
                "_NOT_FOUND",
                StringComparison.Ordinal))
            || action.ValidationReasons.Any(reason => reason.EndsWith(
                "_AMBIGUOUS",
                StringComparison.Ordinal))
            ? "requestedValue"
            : action.ValidationReasons.Any(reason => reason.StartsWith(
                "TARGET_",
                StringComparison.Ordinal))
                ? "target"
                : "unknown";

    private static RequirementChatInteractionOptionReadModel ToInteractionOption(
        ChatActionOptionReadModel option) =>
        new(option.Id, option.Code, option.DisplayName, option.OptionType);

    private static RequirementChatInteractionActionReadModel ToInteractionAction(
        ChatActionPlanActionReadModel action) =>
        new(
            action.ActionId,
            action.ActionType,
            action.TargetTechnicalProposalItemId,
            action.TargetReference,
            action.CurrentValue,
            action.RequestedValue,
            action.ResolvedCatalogEntity is null
                ? null
                : new RequirementChatInteractionResolvedCatalogEntityReadModel(
                    action.ResolvedCatalogEntity.Id,
                    action.ResolvedCatalogEntity.Code,
                    action.ResolvedCatalogEntity.DisplayName,
                    action.ResolvedCatalogEntity.EntityType),
            action.ValidationState,
            action.ValidationReasons,
            action.RequiresConfirmation,
            action.AvailableOptions.Select(ToInteractionOption).ToArray());

    private static string ToExecutedAssistantMessage(ChatActionPlanReadModel plan)
    {
        if (plan.Status is not "EXECUTED" and not "EXECUTED_WITH_PRICING_PENDING")
        {
            return "No fue posible completar la accion. Revisa el mensaje e intenta nuevamente.";
        }

        var prefix = plan.Actions.Count > 1
            ? $"Listo. Aplique {plan.Actions.Count} cambios."
            : "Listo. Aplique el cambio.";
        if (plan.PricingStatus == "PRICING_UPDATED")
        {
            return $"{prefix} El precio tambien fue actualizado.";
        }

        if (plan.PricingStatus == "PRICING_PENDING")
        {
            return $"{prefix} El precio queda pendiente de actualizacion.";
        }

        if (plan.PricingStatus == "NOT_YET_PRICED")
        {
            return $"{prefix} La propuesta aun no tiene pricing generado.";
        }

        return prefix;
    }
    private static string ToAssistantMessage(ChatActionPlanReadModel plan)
    {
        if (plan.Actions.Count == 0)
        {
            return "No pude preparar una accion segura con ese mensaje.";
        }

        if (plan.Status == "READY_FOR_CONFIRMATION")
        {
            if (plan.Actions.Count == 1)
            {
                var action = plan.Actions[0];
                return $"Voy a aplicar {action.ActionType} en {TargetLabel(action)}: {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}. Confirma para ejecutarlo.";
            }

            var lines = plan.Actions
                .Select(action => $"- {RenderBatchAction(action)}");
            return $"Voy a aplicar {plan.Actions.Count} cambios:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}{Environment.NewLine}Confirma para ejecutarlos.";
        }

        var reasons = plan.Actions
            .SelectMany(action => action.ValidationReasons)
            .Concat(plan.ExecutionReasons)
            .ToArray();
        if (reasons.Contains("CHANGE_COMMERCIAL_LINE_NOT_SUPPORTED_YET", StringComparer.Ordinal))
        {
            return "Esa accion aun no esta disponible en el chat.";
        }

        var availableOptions = plan.Actions
            .SelectMany(action => action.AvailableOptions)
            .ToArray();
        if (availableOptions.Length > 0)
        {
            var options = string.Join(
                ", ",
                availableOptions.Take(5).Select(option => option.DisplayName));
            return $"Necesito confirmar la accion antes de continuar. Opciones disponibles: {options}.";
        }

        return reasons.FirstOrDefault()
            ?? "No pude preparar una accion segura con ese mensaje.";
    }

    private static string RenderBatchAction(ChatActionPlanActionReadModel action)
    {
        var target = TargetLabel(action);
        return action.ActionType switch
        {
            "CHANGE_SYSTEM" => $"{target}: sistema {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}",
            "CHANGE_GLASS" => $"{target}: cristal {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}",
            "CHANGE_FINISH" => $"{target}: acabado {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}",
            "CHANGE_QUANTITY" => $"{target}: cantidad {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}",
            "CHANGE_DIMENSIONS" => $"{target}: dimensiones {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}",
            "INCLUDE_ITEM" => $"INCLUDE_ITEM en {target}: {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}",
            "EXCLUDE_ITEM" => $"EXCLUDE_ITEM en {target}: {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}",
            _ => $"{action.ActionType} en {target}: {action.CurrentValue ?? "sin valor actual"} -> {RequestedLabel(action)}"
        };
    }

    private static string TargetLabel(ChatActionPlanActionReadModel action) =>
        string.IsNullOrWhiteSpace(action.TargetReference)
            ? action.TargetTechnicalProposalItemId?.ToString() ?? "sin target"
            : action.TargetReference;

    private static string RequestedLabel(ChatActionPlanActionReadModel action) =>
        action.ResolvedCatalogEntity?.DisplayName
        ?? action.RequestedValue
        ?? "el valor solicitado";
}
