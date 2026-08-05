namespace Domain.PreQuotes;

public enum PreQuoteDraftPricingConfidenceLevel
{
    Low = 1,
    Medium,
    Good,
    High
}

public sealed record PreQuotePreliminaryPricingResult(
    string PricingProfileVersion,
    decimal BillableAreaUnitSquareMeters,
    string? GlassCode,
    int? GlassPriceRangeVersion,
    decimal GlassMinimumPricePerSquareMeter,
    decimal GlassExpectedPricePerSquareMeter,
    decimal GlassMaximumPricePerSquareMeter,
    string? SystemCode,
    TechnicalClassificationSource? SystemSource,
    string? FrameCode,
    string? FinishCode,
    string LaborProfileCode,
    string AssemblyProfileCode,
    decimal FinishFactorMinimum,
    decimal FinishFactorExpected,
    decimal FinishFactorMaximum,
    decimal AccessoryFactor,
    decimal GlassMinimumAmount,
    decimal GlassExpectedAmount,
    decimal GlassMaximumAmount,
    decimal LaborMinimumAmount,
    decimal LaborExpectedAmount,
    decimal LaborMaximumAmount,
    decimal AssemblyMinimumAmount,
    decimal AssemblyExpectedAmount,
    decimal AssemblyMaximumAmount,
    decimal AccessoriesMinimumAmount,
    decimal AccessoriesExpectedAmount,
    decimal AccessoriesMaximumAmount,
    decimal ItemMinimumAmount,
    decimal ItemExpectedAmount,
    decimal ItemMaximumAmount,
    int ConfidenceScore,
    PreQuoteDraftPricingConfidenceLevel ConfidenceLevel,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    bool RequiresReview);

public static class PreQuotePreliminaryPricing
{
    public const string ProfileVersion = "PREQUOTE_V1_2026_08";
    public const string Currency = "COP";
    public const decimal MinimumBillableAreaM2 = 1.00m;
    public const decimal AdministrationPercentage = 0.04m;
    public const decimal ContingencyPercentage = 0.01m;
    public const decimal ProfitPercentage = 0.05m;
    public const decimal VatOnProfitPercentage = 0.19m;
    public const decimal LaborIncreasePercentage = 0.20m;

    public static PreQuotePreliminaryPricingResult? TryCalculate(
        StructuredElementType elementType,
        string? description,
        PreQuoteDraftItemValuationSnapshotSource source,
        PreQuoteDraftItemTechnicalSnapshot? technicalSnapshot)
    {
        if (source.Status != PreQuoteDraftValuationStatus.Valued
            || source.WidthMillimetersUsed is not { } width
            || source.HeightMillimetersUsed is not { } height
            || source.QuantityUsed is not { } quantity
            || source.UnitAreaSquareMeters is not { } areaUnit
            || source.TotalAreaSquareMeters is not { } legacyTotalArea
            || source.UnitPricePerSquareMeter is not { } glassMinimumPrice
            || source.ExpectedPricePerSquareMeter is not { } glassExpectedPrice
            || source.MaximumPricePerSquareMeter is not { } glassMaximumPrice
            || source.UnitAmount is null
            || source.ExpectedAmount is null
            || source.TotalAmount is null
            || width <= 0
            || height <= 0
            || quantity <= 0
            || areaUnit <= 0
            || legacyTotalArea <= 0)
        {
            return null;
        }

        var assumptions = new List<string>
        {
            "PRELIMINARY_RANGE_NOT_CONTRACTUAL",
            "ALUMINUM_BASE_RATE_NOT_CONFIGURED"
        };
        var missingData = new List<string>();
        var confidence = 60;

        var billableAreaUnit = Math.Max(areaUnit, MinimumBillableAreaM2);
        var areaTotal = billableAreaUnit * quantity;
        var glassMinimum = Money(areaTotal * glassMinimumPrice);
        var glassExpected = Money(areaTotal * glassExpectedPrice);
        var glassMaximum = Money(areaTotal * glassMaximumPrice);

        var labor = LaborProfile(elementType);
        if (labor.Assumption is { } laborAssumption)
        {
            assumptions.Add(laborAssumption);
            confidence -= 5;
        }

        var assembly = AssemblyProfile(
            elementType,
            description,
            technicalSnapshot?.SystemCode);
        assumptions.AddRange(assembly.Assumptions);
        confidence -= assembly.ConfidencePenalty;

        var finish = FinishFactor(technicalSnapshot?.FinishCode);
        assumptions.AddRange(finish.Assumptions);
        missingData.AddRange(finish.MissingData);
        confidence -= finish.ConfidencePenalty;

        var accessoryFactor = AccessoryFactor(elementType);
        if (elementType == StructuredElementType.Other)
        {
            assumptions.Add("ACCESSORY_FACTOR_ESTIMATED_BY_UNKNOWN_ELEMENT_TYPE");
            confidence -= 5;
        }

        if (technicalSnapshot?.SystemCode is not null)
        {
            confidence += 10;
        }
        if (technicalSnapshot?.FinishCode is not null)
        {
            confidence += 5;
        }
        if (technicalSnapshot?.FrameCode is not null)
        {
            confidence += 5;
        }
        if (technicalSnapshot?.SystemSource == TechnicalClassificationSource.Inferred)
        {
            confidence -= 8;
        }
        if (technicalSnapshot?.FrameCode is null)
        {
            missingData.Add("FRAME_NOT_CONFIRMED");
            confidence -= 5;
        }

        missingData.Add("LEAF_COUNT_NOT_AVAILABLE");
        confidence -= 5;
        confidence -= 10;

        if (technicalSnapshot?.RequiresReview == true)
        {
            assumptions.AddRange(technicalSnapshot.ReviewReasons);
            confidence -= 5;
        }

        var laborMinimum = Money(areaTotal * labor.Minimum);
        var laborExpected = Money(areaTotal * labor.Expected);
        var laborMaximum = Money(areaTotal * labor.Maximum);
        var assemblyMinimum = Money(assembly.Minimum * quantity);
        var assemblyExpected = Money(assembly.Expected * quantity);
        var assemblyMaximum = Money(assembly.Maximum * quantity);

        var preFinishMinimum = glassMinimum + laborMinimum + assemblyMinimum;
        var preFinishExpected = glassExpected + laborExpected + assemblyExpected;
        var preFinishMaximum = glassMaximum + laborMaximum + assemblyMaximum;
        var finishMinimum = Money(preFinishMinimum * finish.Minimum);
        var finishExpected = Money(preFinishExpected * finish.Expected);
        var finishMaximum = Money(preFinishMaximum * finish.Maximum);
        var accessoriesMinimum = Money(finishMinimum * accessoryFactor);
        var accessoriesExpected = Money(finishExpected * accessoryFactor);
        var accessoriesMaximum = Money(finishMaximum * accessoryFactor);
        var itemMinimum = finishMinimum + accessoriesMinimum;
        var itemExpected = finishExpected + accessoriesExpected;
        var itemMaximum = finishMaximum + accessoriesMaximum;

        var distinctAssumptions = assumptions
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var distinctMissing = missingData
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var score = Math.Clamp(confidence, 0, 100);
        var level = score >= 85
            ? PreQuoteDraftPricingConfidenceLevel.High
            : score >= 70
                ? PreQuoteDraftPricingConfidenceLevel.Good
                : score >= 50
                    ? PreQuoteDraftPricingConfidenceLevel.Medium
                    : PreQuoteDraftPricingConfidenceLevel.Low;

        return new(
            ProfileVersion,
            billableAreaUnit,
            null,
            source.PriceRangeVersion,
            glassMinimumPrice,
            glassExpectedPrice,
            glassMaximumPrice,
            technicalSnapshot?.SystemCode,
            technicalSnapshot?.SystemSource,
            technicalSnapshot?.FrameCode,
            technicalSnapshot?.FinishCode,
            labor.Code,
            assembly.Code,
            finish.Minimum,
            finish.Expected,
            finish.Maximum,
            accessoryFactor,
            glassMinimum,
            glassExpected,
            glassMaximum,
            laborMinimum,
            laborExpected,
            laborMaximum,
            assemblyMinimum,
            assemblyExpected,
            assemblyMaximum,
            accessoriesMinimum,
            accessoriesExpected,
            accessoriesMaximum,
            itemMinimum,
            itemExpected,
            itemMaximum,
            score,
            level,
            distinctAssumptions,
            distinctMissing,
            distinctAssumptions.Length > 0 || distinctMissing.Length > 0);
    }

    public static PreQuoteDraftEconomicSummaryTotals CalculateTotals(
        IReadOnlyList<PreQuoteDraftItemValuationSnapshot> snapshots,
        string? location)
    {
        var totalArea = snapshots.Sum(x => x.TotalAreaSquareMeters ?? 0);
        var minimumSubtotal = snapshots.Sum(x => x.ItemMinimumAmount ?? 0);
        var expectedSubtotal = snapshots.Sum(x => x.ItemExpectedAmount ?? 0);
        var maximumSubtotal = snapshots.Sum(x => x.ItemMaximumAmount ?? 0);
        var transport = Transport(location);
        var administrationMinimum = Money(minimumSubtotal * AdministrationPercentage);
        var administrationExpected = Money(expectedSubtotal * AdministrationPercentage);
        var administrationMaximum = Money(maximumSubtotal * AdministrationPercentage);
        var contingencyMinimum = Money(minimumSubtotal * ContingencyPercentage);
        var contingencyExpected = Money(expectedSubtotal * ContingencyPercentage);
        var contingencyMaximum = Money(maximumSubtotal * ContingencyPercentage);
        var profitMinimum = Money(minimumSubtotal * ProfitPercentage);
        var profitExpected = Money(expectedSubtotal * ProfitPercentage);
        var profitMaximum = Money(maximumSubtotal * ProfitPercentage);
        var vatMinimum = Money(profitMinimum * VatOnProfitPercentage);
        var vatExpected = Money(profitExpected * VatOnProfitPercentage);
        var vatMaximum = Money(profitMaximum * VatOnProfitPercentage);
        var finalMinimum = minimumSubtotal + transport.Minimum
            + administrationMinimum + contingencyMinimum + profitMinimum
            + vatMinimum;
        var finalExpected = expectedSubtotal + transport.Expected
            + administrationExpected + contingencyExpected + profitExpected
            + vatExpected;
        var finalMaximum = maximumSubtotal + transport.Maximum
            + administrationMaximum + contingencyMaximum + profitMaximum
            + vatMaximum;
        var confidence = OverallConfidence(snapshots);
        return new(
            totalArea,
            minimumSubtotal,
            expectedSubtotal,
            maximumSubtotal,
            transport.Minimum,
            transport.Expected,
            transport.Maximum,
            administrationMinimum,
            administrationExpected,
            administrationMaximum,
            contingencyMinimum,
            contingencyExpected,
            contingencyMaximum,
            profitMinimum,
            profitExpected,
            profitMaximum,
            vatMinimum,
            vatExpected,
            vatMaximum,
            finalMinimum,
            finalExpected,
            finalMaximum,
            confidence.Score,
            confidence.Level,
            transport.Assumptions,
            transport.MissingData,
            true);
    }

    private static (string Code, decimal Minimum, decimal Expected,
        decimal Maximum, string? Assumption) LaborProfile(
        StructuredElementType elementType) => elementType switch
    {
        StructuredElementType.Facade => ("FACADE", 50646m, 50646m, 50646m, null),
        StructuredElementType.Other => ("VENTANERIA_STANDARD", 38640m, 38640m, 38640m,
            "UNKNOWN_ELEMENT_TYPE_LABOR_PROFILE_ASSUMED"),
        _ => ("VENTANERIA_STANDARD", 38640m, 38640m, 38640m, null)
    };

    private static (string Code, decimal Minimum, decimal Expected,
        decimal Maximum, IReadOnlyList<string> Assumptions,
        int ConfidencePenalty) AssemblyProfile(
        StructuredElementType elementType,
        string? description,
        string? systemCode)
    {
        var text = description?.ToUpperInvariant() ?? string.Empty;
        var sliding = text.Contains("CORRED", StringComparison.Ordinal)
            || text.Contains("SLID", StringComparison.Ordinal);
        return (systemCode, elementType, sliding) switch
        {
            ("K70", StructuredElementType.Door, _) =>
                ("K70_SLIDING_DOOR", 55200m, 69600m, 84000m, [], 0),
            ("K100", StructuredElementType.Door, _) =>
                ("K100_SLIDING_DOOR", 60000m, 78000m, 96000m, [], 0),
            ("K40", StructuredElementType.Door, false) =>
                ("K40_SWING_DOOR", 72000m, 114000m, 150000m, [], 0),
            ("SG45", _, _) =>
                ("SG45_FACADE", 14400m, 14400m, 14400m,
                    ["SG45_ASSEMBLY_ONLY_LOW_RATE_CONFIRMED"], 5),
            (_, StructuredElementType.Window, true) =>
                ("WINDOW_SLIDING_TRADITIONAL", 18000m, 26400m, 30000m, [], 0),
            (_, StructuredElementType.Door, true) =>
                ("DOOR_SLIDING_TRADITIONAL", 42000m, 54000m, 66000m, [], 0),
            (_, StructuredElementType.Facade, _) =>
                ("SG45_FACADE", 14400m, 14400m, 14400m,
                    ["ASSEMBLY_PROFILE_ESTIMATED_BY_ELEMENT_TYPE"], 5),
            (_, StructuredElementType.Window, _) or (_, StructuredElementType.Partition, _) =>
                ("FIXED_BODY", 16800m, 21600m, 26400m, [], 0),
            _ => ("FIXED_BODY", 16800m, 21600m, 26400m,
                ["ASSEMBLY_PROFILE_ESTIMATED_BY_ELEMENT_TYPE"], 5)
        };
    }

    private static (decimal Minimum, decimal Expected, decimal Maximum,
        IReadOnlyList<string> Assumptions, IReadOnlyList<string> MissingData,
        int ConfidencePenalty) FinishFactor(string? finishCode) => finishCode switch
    {
        "STANDARD_NATURAL" => (1.00m, 1.00m, 1.00m, [], [], 0),
        "ANODIZED_GRAY" => (1.03m, 1.05m, 1.08m, [], [], 0),
        "BLACK_MATTE" => (1.08m, 1.12m, 1.15m, [], [], 0),
        "SPECIAL" => (1.05m, 1.10m, 1.20m, [], [], 0),
        _ => (1.00m, 1.08m, 1.15m,
            ["UNKNOWN_FINISH_FACTOR_APPLIED"], ["FINISH_NOT_CONFIRMED"], 5)
    };

    private static decimal AccessoryFactor(StructuredElementType elementType) =>
        elementType switch
        {
            StructuredElementType.Partition => 0.05m,
            StructuredElementType.Window => 0.08m,
            StructuredElementType.Door or StructuredElementType.Facade => 0.12m,
            StructuredElementType.Other => 0.15m,
            _ => 0.08m
        };

    private static PreQuoteDraftTransportTotals Transport(string? location)
    {
        var text = location?.Trim().ToUpperInvariant();
        return text switch
        {
            "BOGOTA" or "BOGOTA D.C." or "BOGOTÁ" or "BOGOTÁ D.C." =>
                new(1500000m, 1500000m, 1500000m, [], []),
            "BUCARAMANGA" => new(200000m, 200000m, 200000m, [], []),
            "FLORIDABLANCA" => new(140000m, 140000m, 140000m, [], []),
            _ => new(0m, 0m, 0m,
                ["TRANSPORT_NOT_CONFIRMED"],
                ["PROJECT_LOCATION_NOT_CONFIRMED"])
        };
    }

    private static (int? Score, PreQuoteDraftPricingConfidenceLevel? Level)
        OverallConfidence(IReadOnlyList<PreQuoteDraftItemValuationSnapshot> snapshots)
    {
        var valued = snapshots
            .Where(x => x.ConfidenceScore is not null)
            .ToArray();
        if (valued.Length == 0)
        {
            return (null, null);
        }

        var areaWeighted = valued
            .Where(x => x.TotalAreaSquareMeters is > 0)
            .ToArray();
        var score = areaWeighted.Length > 0
            ? areaWeighted.Sum(x => x.ConfidenceScore!.Value
                * x.TotalAreaSquareMeters!.Value)
                / areaWeighted.Sum(x => x.TotalAreaSquareMeters!.Value)
            : (decimal)valued.Average(x => x.ConfidenceScore!.Value);
        var rounded = (int)Math.Round(score, 0, MidpointRounding.AwayFromZero);
        return (rounded, rounded >= 85
            ? PreQuoteDraftPricingConfidenceLevel.High
            : rounded >= 70
                ? PreQuoteDraftPricingConfidenceLevel.Good
                : rounded >= 50
                    ? PreQuoteDraftPricingConfidenceLevel.Medium
                    : PreQuoteDraftPricingConfidenceLevel.Low);
    }

    private static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed record PreQuoteDraftEconomicSummaryTotals(
    decimal TotalAreaM2,
    decimal MinimumTechnicalSubtotal,
    decimal ExpectedTechnicalSubtotal,
    decimal MaximumTechnicalSubtotal,
    decimal TransportMinimum,
    decimal TransportExpected,
    decimal TransportMaximum,
    decimal AdministrationMinimum,
    decimal AdministrationExpected,
    decimal AdministrationMaximum,
    decimal ContingencyMinimum,
    decimal ContingencyExpected,
    decimal ContingencyMaximum,
    decimal ProfitMinimum,
    decimal ProfitExpected,
    decimal ProfitMaximum,
    decimal VatMinimum,
    decimal VatExpected,
    decimal VatMaximum,
    decimal FinalMinimum,
    decimal FinalExpected,
    decimal FinalMaximum,
    int? OverallConfidence,
    PreQuoteDraftPricingConfidenceLevel? ConfidenceLevel,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData,
    bool HasLimitedPricingScope);

public sealed record PreQuoteDraftTransportTotals(
    decimal Minimum,
    decimal Expected,
    decimal Maximum,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> MissingData);
