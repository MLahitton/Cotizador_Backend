using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.DocumentProcessing;

public sealed record CotizadorAi2Options(
    Uri BaseUri,
    int TimeoutSeconds,
    long MaximumResponseBytes,
    bool Enabled)
{
    private const int MaximumTimeoutSeconds = 900;

    public static CotizadorAi2Options FromConfiguration(
        IConfiguration configuration)
    {
        var value = configuration["CotizadorAi2:BaseUrl"];
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var baseUri)
            || baseUri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidOperationException(
                "La configuracion 'CotizadorAi2:BaseUrl' es obligatoria y debe ser una URL HTTP valida.");
        }

        if (!baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            baseUri = new Uri($"{baseUri.AbsoluteUri}/", UriKind.Absolute);
        }

        var timeout = ParseInt(
            configuration,
            "CotizadorAi2:TimeoutSeconds",
            1,
            MaximumTimeoutSeconds);
        var maximumBytes = ParseLong(
            configuration,
            "CotizadorAi2:MaximumResponseBytes",
            1,
            134_217_728);
        var enabledValue = configuration["CotizadorAi2:Enabled"];
        var enabled = string.IsNullOrWhiteSpace(enabledValue)
            || !bool.TryParse(enabledValue, out var parsed)
            || parsed;

        return new CotizadorAi2Options(baseUri, timeout, maximumBytes, enabled);
    }

    private static int ParseInt(
        IConfiguration configuration,
        string key,
        int minimum,
        int maximum)
    {
        if (!int.TryParse(
                configuration[key],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result)
            || result < minimum
            || result > maximum)
        {
            throw new InvalidOperationException(
                $"La configuracion '{key}' debe estar entre {minimum} y {maximum}.");
        }

        return result;
    }

    private static long ParseLong(
        IConfiguration configuration,
        string key,
        long minimum,
        long maximum)
    {
        if (!long.TryParse(
                configuration[key],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result)
            || result < minimum
            || result > maximum)
        {
            throw new InvalidOperationException(
                $"La configuracion '{key}' debe estar entre {minimum} y {maximum}.");
        }

        return result;
    }
}
