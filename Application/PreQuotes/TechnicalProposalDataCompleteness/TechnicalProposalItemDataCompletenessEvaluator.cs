using Domain.PreQuotes;

namespace Application.PreQuotes.TechnicalProposalDataCompleteness;

public static class TechnicalProposalItemDataCompletenessEvaluator
{
    public const string Complete = "COMPLETE";
    public const string Incomplete = "INCOMPLETE";
    public const string Quantity = "QUANTITY";
    public const string Width = "WIDTH";
    public const string Height = "HEIGHT";

    public static RequirementTechnicalProposalItemDataCompletenessReadModel Evaluate(
        RequirementTechnicalProposalItem item)
    {
        var missingFields = new List<string>(capacity: 3);

        if (item.EffectiveQuantity is null or <= 0)
        {
            missingFields.Add(Quantity);
        }

        if (item.EffectiveWidthMillimeters is null or <= 0)
        {
            missingFields.Add(Width);
        }

        if (item.EffectiveHeightMillimeters is null or <= 0)
        {
            missingFields.Add(Height);
        }

        return new RequirementTechnicalProposalItemDataCompletenessReadModel(
            missingFields.Count == 0 ? Complete : Incomplete,
            missingFields.Count == 0,
            missingFields);
    }
}

public sealed record RequirementTechnicalProposalItemDataCompletenessReadModel(
    string State,
    bool IsComplete,
    IReadOnlyList<string> MissingFields);