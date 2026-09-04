namespace Application.Common.Abstractions.PreQuotes;

public interface IRequirementChatAiClient
{
    Task<RequirementChatAiResponse> RespondAsync(
        RequirementChatAiRequest request,
        CancellationToken cancellationToken);

    Task<RequirementChatActionIntent> InterpretActionAsync(
        RequirementChatActionInterpretationRequest request,
        CancellationToken cancellationToken);
}

public sealed record RequirementChatAiRequest(
    string Scope,
    string UserMessage,
    IReadOnlyList<RequirementChatAiConversationMessage> Conversation,
    object Context);

public sealed record RequirementChatAiConversationMessage(
    string Role,
    string Content);

public sealed record RequirementChatAiResponse(string Message);

public sealed record RequirementChatActionInterpretationRequest(
    string Message,
    string Scope,
    Guid? TechnicalProposalItemId,
    IReadOnlyList<RequirementChatAiConversationMessage> Conversation,
    object Context);

public sealed record RequirementChatActionIntent(
    bool IsAction,
    string? ActionType,
    string? Scope,
    string? TargetReference,
    string? RequestedValue,
    int? RequestedQuantity,
    int? RequestedWidthMm,
    int? RequestedHeightMm,
    decimal? Confidence,
    bool RequiresClarification,
    string? ClarificationReason,
    string? RawUserMessage);

public sealed class RequirementChatAiUnavailableException : Exception
{
    public RequirementChatAiUnavailableException(Exception? innerException = null)
        : base("No fue posible obtener respuesta del asistente AI2.", innerException)
    {
    }
}
