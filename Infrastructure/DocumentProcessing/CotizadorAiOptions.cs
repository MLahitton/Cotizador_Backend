using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.DocumentProcessing;

public sealed record CotizadorAiOptions(
    Uri BaseUri,
    int TimeoutSeconds,
    long MaximumResponseBytes,
    int MaximumPageCount)
{
    private const int MaximumTimeoutSeconds = 300;
    private const long MaximumAllowedResponseBytes = 134_217_728;
    private const int MaximumAllowedPageCount = 100;

    public static CotizadorAiOptions FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var baseUrlValue = configuration["CotizadorAi:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrlValue))
        {
            throw new InvalidOperationException(
                "La configuración 'CotizadorAi:BaseUrl' es obligatoria.");
        }

        var normalizedBaseUrl = baseUrlValue.Trim();

        if (!Uri.TryCreate(
                normalizedBaseUrl,
                UriKind.Absolute,
                out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp
                && baseUri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException(
                "La configuración 'CotizadorAi:BaseUrl' no es una URL HTTP válida.");
        }

        if (!baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            baseUri = new Uri(
                $"{baseUri.AbsoluteUri}/",
                UriKind.Absolute);
        }

        var timeoutValue = configuration["CotizadorAi:TimeoutSeconds"];

        if (string.IsNullOrWhiteSpace(timeoutValue)
            || !int.TryParse(
                timeoutValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var timeoutSeconds)
            || timeoutSeconds <= 0
            || timeoutSeconds > MaximumTimeoutSeconds)
        {
            throw new InvalidOperationException(
                "La configuración 'CotizadorAi:TimeoutSeconds' debe ser un entero entre 1 y 300.");
        }

        var maximumResponseBytesValue =
            configuration["CotizadorAi:MaximumResponseBytes"];

        if (string.IsNullOrWhiteSpace(maximumResponseBytesValue)
            || !long.TryParse(
                maximumResponseBytesValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maximumResponseBytes)
            || maximumResponseBytes <= 0
            || maximumResponseBytes > MaximumAllowedResponseBytes)
        {
            throw new InvalidOperationException(
                "La configuración 'CotizadorAi:MaximumResponseBytes' debe estar entre 1 y 134217728 bytes.");
        }

        var maximumPageCountValue =
            configuration["CotizadorAi:MaximumPageCount"];

        if (string.IsNullOrWhiteSpace(maximumPageCountValue)
            || !int.TryParse(
                maximumPageCountValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maximumPageCount)
            || maximumPageCount <= 0
            || maximumPageCount > MaximumAllowedPageCount)
        {
            throw new InvalidOperationException(
                "La configuración 'CotizadorAi:MaximumPageCount' debe ser un entero entre 1 y 100.");
        }

        return new CotizadorAiOptions(
            baseUri,
            timeoutSeconds,
            maximumResponseBytes,
            maximumPageCount);
    }
}
