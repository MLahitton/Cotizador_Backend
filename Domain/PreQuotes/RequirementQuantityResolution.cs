namespace Domain.PreQuotes;

public enum RequirementQuantitySource
{
    EXTRACTED,
    MANUAL,
    ASSUMED_DEFAULT
}

public readonly record struct RequirementQuantityResolution(
    int Value,
    RequirementQuantitySource Source,
    RequirementExtractionValueStatus? ExtractionStatus = null,
    decimal? ExtractionConfidence = null)
{
    public string? ExtractionStatusCode => ExtractionStatus == RequirementExtractionValueStatus.NotApplicable
        ? "NOT_APPLICABLE"
        : ExtractionStatus?.ToString().ToUpperInvariant();

    public string? ReviewReason => Source switch
    {
        RequirementQuantitySource.ASSUMED_DEFAULT => "QUANTITY_ASSUMED",
        RequirementQuantitySource.EXTRACTED when ExtractionStatus == RequirementExtractionValueStatus.Ambiguous => "QUANTITY_AMBIGUOUS",
        RequirementQuantitySource.EXTRACTED when ExtractionStatus == RequirementExtractionValueStatus.Unknown => "QUANTITY_UNKNOWN",
        _ => null
    };

    public static RequirementQuantityResolution Resolve(
        int? extracted,
        int? manual,
        RequirementExtractionValueStatus? extractionStatus = null,
        decimal? extractionConfidence = null) =>
        manual is > 0
            ? new(manual.Value, RequirementQuantitySource.MANUAL, extractionStatus, extractionConfidence)
            : extracted is > 0
                ? new(extracted.Value, RequirementQuantitySource.EXTRACTED, extractionStatus, extractionConfidence)
                : new(1, RequirementQuantitySource.ASSUMED_DEFAULT, extractionStatus, extractionConfidence);
}
