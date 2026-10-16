using Domain.PreQuotes;
using Xunit;

namespace CotizadorBackend.Tests.Application.PreQuotes;

public sealed class RequirementQuantityResolutionTests
{
    [Theory]
    [InlineData(3, null, RequirementExtractionValueStatus.Explicit, 3, RequirementQuantitySource.EXTRACTED, null)]
    [InlineData(1, null, RequirementExtractionValueStatus.Inferred, 1, RequirementQuantitySource.EXTRACTED, null)]
    [InlineData(1, null, RequirementExtractionValueStatus.Ambiguous, 1, RequirementQuantitySource.EXTRACTED, "QUANTITY_AMBIGUOUS")]
    [InlineData(1, null, RequirementExtractionValueStatus.Unknown, 1, RequirementQuantitySource.EXTRACTED, "QUANTITY_UNKNOWN")]
    [InlineData(null, null, RequirementExtractionValueStatus.Unknown, 1, RequirementQuantitySource.ASSUMED_DEFAULT, "QUANTITY_ASSUMED")]
    [InlineData(0, null, RequirementExtractionValueStatus.Ambiguous, 1, RequirementQuantitySource.ASSUMED_DEFAULT, "QUANTITY_ASSUMED")]
    [InlineData(1, 1, RequirementExtractionValueStatus.Ambiguous, 1, RequirementQuantitySource.MANUAL, null)]
    [InlineData(1, 2, RequirementExtractionValueStatus.Unknown, 2, RequirementQuantitySource.MANUAL, null)]
    [InlineData(1, null, null, 1, RequirementQuantitySource.EXTRACTED, null)]
    public void Resolve_KeepsCommercialSourceAndExtractionIndependent(
        int? extracted, int? manual, RequirementExtractionValueStatus? status,
        int expected, RequirementQuantitySource source, string? reason)
    {
        var confidence = status is null ? (decimal?)null : 0.5m;
        var result = RequirementQuantityResolution.Resolve(extracted, manual, status, confidence);
        Assert.Equal(expected, result.Value);
        Assert.Equal(source, result.Source);
        Assert.Equal(status, result.ExtractionStatus);
        Assert.Equal(confidence, result.ExtractionConfidence);
        Assert.Equal(reason, result.ReviewReason);
    }
}
