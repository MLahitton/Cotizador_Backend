using System.Net;
using System.Net.Http.Json;
using Application.Common.Abstractions.PreQuotes;
using Microsoft.Extensions.Logging;

namespace Infrastructure.DocumentProcessing;

public sealed class CotizadorAi2RequirementChatClient(
    HttpClient httpClient,
    CotizadorAi2Options options,
    ILogger<CotizadorAi2RequirementChatClient> logger)
    : IRequirementChatAiClient
{
    private const string ChatPath = "chat/respond";
    private const string InterpretPath = "chat/actions/interpret";

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
                var errorBody = await response.Content.ReadAsStringAsync(
                    timeoutSource.Token);
                logger.LogWarning(
                    "Cotizador_AI2 chat respond failed. Endpoint={Endpoint} StatusCode={StatusCode} ResponseBody={ResponseBody}",
                    "/" + ChatPath,
                    (int)response.StatusCode,
                    Truncate(errorBody));
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

    public async Task<RequirementChatActionIntent> InterpretActionAsync(
        RequirementChatActionInterpretationRequest request,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                InterpretPath,
                request,
                timeoutSource.Token);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                var errorBody = await response.Content.ReadAsStringAsync(
                    timeoutSource.Token);
                logger.LogWarning(
                    "Cotizador_AI2 chat action interpret failed. Endpoint={Endpoint} StatusCode={StatusCode} ResponseBody={ResponseBody}",
                    "/" + InterpretPath,
                    (int)response.StatusCode,
                    Truncate(errorBody));
                throw new RequirementChatAiUnavailableException(
                    new InvalidDataException(
                        $"Cotizador_AI2 chat action interpret returned {(int)response.StatusCode}: {Truncate(errorBody)}"));
            }

            var body = await response.Content
                .ReadFromJsonAsync<RequirementChatActionIntent>(
                    cancellationToken: timeoutSource.Token);
            if (body is null)
            {
                throw new RequirementChatAiUnavailableException();
            }

            return body;
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

    private static string Truncate(string value)
    {
        const int maximumLength = 4_000;
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
        {
            return value;
        }

        return value[..maximumLength];
    }
}
