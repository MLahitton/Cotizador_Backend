using Application.Common.Abstractions.DocumentProcessing;

namespace Infrastructure.DocumentProcessing;

public sealed class LegacyDocumentProcessingResponseAdapter
{
    public DocumentProcessingResponseData Adapt(
        DocumentProcessingResponseData response)
    {
        var hasCatalogContract = string.Equals(
            response.SchemaVersion,
            "3.0",
            StringComparison.Ordinal);

        return response with
        {
            Provider = DocumentProcessingProvider.LegacyAi,
            RequiresResolvedGlassCatalog = hasCatalogContract,
            SupportsPreliminaryValuation = hasCatalogContract
        };
    }
}
