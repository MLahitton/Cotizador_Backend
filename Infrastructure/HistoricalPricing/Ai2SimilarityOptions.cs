using Microsoft.Extensions.Configuration;

namespace Infrastructure.HistoricalPricing;

public sealed record Ai2SimilarityOptions(Uri? Endpoint, Uri? BatchEndpoint)
{
    public Ai2SimilarityOptions(Uri? endpoint)
        : this(endpoint, DeriveBatchEndpoint(endpoint))
    {
    }

    public static Ai2SimilarityOptions FromConfiguration(IConfiguration configuration)
    {
        var value = configuration["CotizadorAi2:SimilarityEndpoint"];
        var batchValue = configuration["CotizadorAi2:SimilarityBatchEndpoint"];
        var endpoint = Uri.TryCreate(value, UriKind.Absolute, out var parsedEndpoint)
            ? parsedEndpoint
            : null;
        var batchEndpoint = Uri.TryCreate(
            batchValue,
            UriKind.Absolute,
            out var parsedBatchEndpoint)
            ? parsedBatchEndpoint
            : DeriveBatchEndpoint(endpoint);
        return new Ai2SimilarityOptions(endpoint, batchEndpoint);
    }

    private static Uri? DeriveBatchEndpoint(Uri? endpoint)
    {
        if (endpoint is null)
        {
            return null;
        }

        var value = endpoint.ToString();
        const string suffix = "/evaluate";
        if (!value.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        return new Uri(value[..^suffix.Length] + "/evaluate-batch");
    }
}
