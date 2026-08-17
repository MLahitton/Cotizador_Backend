using Microsoft.Extensions.Configuration;

namespace Infrastructure.HistoricalPricing;

public sealed record Ai2SimilarityOptions(Uri? Endpoint)
{
    public static Ai2SimilarityOptions FromConfiguration(IConfiguration configuration)
    {
        var value = configuration["CotizadorAi2:SimilarityEndpoint"];
        return new Ai2SimilarityOptions(
            Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ? endpoint : null);
    }
}
