using Microsoft.Extensions.Configuration;

namespace Infrastructure.HistoricalPricing;

public sealed record HistoricalPricingOptions(string? QuotesPath, int CandidateTopK)
{
    public static HistoricalPricingOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("HistoricalPricing");
        return new HistoricalPricingOptions(
            section["QuotesPath"],
            Math.Clamp(section.GetValue<int?>("CandidateTopK") ?? 20, 1, 100));
    }
}
