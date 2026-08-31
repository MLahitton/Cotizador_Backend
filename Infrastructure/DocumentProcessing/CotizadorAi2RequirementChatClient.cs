using System.Net;
using System.Net.Http.Json;
using Application.Common.Abstractions.PreQuotes;

namespace Infrastructure.DocumentProcessing;

public sealed class CotizadorAi2RequirementChatClient(
    HttpClient httpClient,
    CotizadorAi2Options options)
    : IRequirementChatAiClient
{
    private const string ChatPath = "chat/respond";

    public async Task<RequirementChatAiResponse> RespondAsync(
        RequirementChatAiRequest request,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                ChatPath,
                request,
                timeoutSource.Token);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new RequirementChatAiUnavailableException();
            }

            var body = await response.Content
                .ReadFromJsonAsync<RequirementChatAiResponse>(
                    cancellationToken: timeoutSource.Token);
            if (body is null || string.IsNullOrWhiteSpace(body.Message))
            {
                throw new RequirementChatAiUnavailableException();
            }

            return new RequirementChatAiResponse(body.Message.Trim());
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested
                  && timeoutSource.IsCancellationRequested)
        {
            throw new RequirementChatAiUnavailableException(exception);
        }
        catch (HttpRequestException exception)
        {
            throw new RequirementChatAiUnavailableException(exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new RequirementChatAiUnavailableException(exception);
        }
    }
}
