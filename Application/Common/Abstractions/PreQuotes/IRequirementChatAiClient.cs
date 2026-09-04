using System.Text.Json.Serialization;

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
    string UserMessage,
    string Scope,
    [property: JsonIgnore]
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
    string? RawUserMessage,
    RequirementChatRequestedAttributes? RequestedAttributes = null);

public sealed record RequirementChatRequestedAttributes(
    RequirementChatRequestedSystemAttributes? System = null,
    RequirementChatRequestedGlassAttributes? Glass = null,
    RequirementChatRequestedFinishAttributes? Finish = null);

public sealed record RequirementChatRequestedSystemAttributes(
    string? FunctionalType = null,
    string? Operation = null,
    string? CommercialName = null,
    string? Family = null,
    string? Variant = null,
    string? CommercialLine = null,
    string? Code = null);

public sealed record RequirementChatRequestedGlassAttributes(
    string? Family = null,
    string? Composition = null,
    string? Treatment = null,
    decimal? OuterThicknessMm = null,
    decimal? InnerThicknessMm = null,
    decimal? PvbThicknessMm = null,
    decimal? ChamberThicknessMm = null,
    string? PvbType = null,
    string? PvbColor = null,
    string? Color = null,
    string? Pattern = null,
    string? ProductLine = null,
    string? ProductToken = null);

public sealed record RequirementChatRequestedFinishAttributes(
    string? NormalizedType = null,
    string? Material = null,
    string? Color = null,
    string? Texture = null,
    string? Process = null,
    string? CommercialCode = null);

public sealed class RequirementChatAiUnavailableException : Exception
{
    public RequirementChatAiUnavailableException(Exception? innerException = null)
        : base("No fue posible obtener respuesta del asistente AI2.", innerException)
    {
    }
}
