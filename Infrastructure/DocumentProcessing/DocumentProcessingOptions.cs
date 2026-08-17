using Microsoft.Extensions.Configuration;

namespace Infrastructure.DocumentProcessing;

public sealed record DocumentProcessingOptions(
    DocumentProcessingProviderKind Provider,
    bool EnableLegacyFallback)
{
    public static DocumentProcessingOptions FromConfiguration(
        IConfiguration configuration)
    {
        var providerValue = configuration["DocumentProcessing:Provider"];
        var provider = providerValue?.Trim().ToUpperInvariant() switch
        {
            "AI2" or null or "" => DocumentProcessingProviderKind.Ai2,
            "LEGACY_AI" or "LEGACY" => DocumentProcessingProviderKind.LegacyAi,
            _ => throw new InvalidOperationException(
                "La configuracion 'DocumentProcessing:Provider' debe ser AI2 o LEGACY_AI.")
        };

        var fallbackValue =
            configuration["DocumentProcessing:EnableLegacyFallback"];
        var enableFallback = !string.IsNullOrWhiteSpace(fallbackValue)
            && bool.TryParse(fallbackValue, out var parsed)
            && parsed;

        return new DocumentProcessingOptions(provider, enableFallback);
    }
}

public enum DocumentProcessingProviderKind
{
    Ai2 = 1,
    LegacyAi = 2
}
