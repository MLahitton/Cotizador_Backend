namespace Application.Common.Abstractions.PreQuotes;

public interface IRequirementChatAiClient
{
    Task<RequirementChatAiResponse> RespondAsync(
        RequirementChatAiRequest request,
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

public sealed class RequirementChatAiUnavailableException : Exception
{
    public RequirementChatAiUnavailableException(Exception? innerException = null)
        : base("No fue posible obtener respuesta del asistente AI2.", innerException)
    {
    }
}
