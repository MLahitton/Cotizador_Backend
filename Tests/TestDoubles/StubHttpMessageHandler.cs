using System.Net.Http.Headers;
using System.Text;

namespace CotizadorBackend.Tests.TestDoubles;

public sealed class StubHttpMessageHandler(
    Func<HttpResponseMessage> responseFactory)
    : HttpMessageHandler
{
    public CapturedHttpRequest? LastRequest { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LastRequest = await CapturedHttpRequest.CreateAsync(
            request,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return responseFactory();
    }
}

public sealed record CapturedHttpRequest(
    HttpMethod Method,
    Uri? RequestUri,
    IReadOnlyList<string> Accept,
    IReadOnlyList<string> CorrelationValues,
    string? ContentType,
    IReadOnlyList<CapturedMultipartPart> Parts)
{
    internal static async Task<CapturedHttpRequest> CreateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var accept = request.Headers.Accept
            .Select(value => value.MediaType ?? string.Empty)
            .ToArray();
        var correlationValues = request.Headers.TryGetValues(
            "X-Correlation-ID",
            out var values)
            ? values.ToArray()
            : [];
        var parts = new List<CapturedMultipartPart>();

        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var part in multipart)
            {
                var bytes = await part.ReadAsByteArrayAsync(
                    cancellationToken);
                var disposition = part.Headers.ContentDisposition;

                parts.Add(
                    new CapturedMultipartPart(
                        Unquote(disposition?.Name),
                        Unquote(disposition?.FileName),
                        part.Headers.ContentType?.MediaType,
                        bytes,
                        Encoding.UTF8.GetString(bytes)));
            }
        }

        return new CapturedHttpRequest(
            request.Method,
            request.RequestUri,
            accept,
            correlationValues,
            request.Content?.Headers.ContentType?.MediaType,
            parts);
    }

    private static string? Unquote(string? value)
    {
        return value?.Trim('"');
    }
}

public sealed record CapturedMultipartPart(
    string? Name,
    string? FileName,
    string? ContentType,
    byte[] Bytes,
    string Text);
