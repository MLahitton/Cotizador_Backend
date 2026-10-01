using Domain.Identity;

namespace Domain.PreQuotes;

public sealed class RequirementItemExperienceDraft
{
    private readonly List<RequirementItemExperienceAnswer> _answers = [];

    private RequirementItemExperienceDraft()
    {
        CatalogVersion = string.Empty;
        ResolutionState = RequirementItemExperienceResolutionState.Pending;
    }

    private RequirementItemExperienceDraft(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        string catalogVersion,
        string? spaceTypeCode,
        IReadOnlyCollection<RequirementItemExperienceAnswerValue> answers,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        Id = Guid.NewGuid();
        TechnicalProposalId = technicalProposalId;
        TechnicalProposalItemId = technicalProposalItemId;
        CatalogVersion = catalogVersion;
        SpaceTypeCode = NormalizeOptional(spaceTypeCode);
        ResolutionState = RequirementItemExperienceResolutionState.Pending;
        Revision = 1;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc;
        ReplaceAnswers(answers);
    }

    public Guid Id { get; private set; }

    public Guid TechnicalProposalId { get; private set; }

    public RequirementTechnicalProposal TechnicalProposal { get; private set; } = null!;

    public Guid TechnicalProposalItemId { get; private set; }

    public RequirementTechnicalProposalItem TechnicalProposalItem { get; private set; } = null!;

    public string CatalogVersion { get; private set; }

    public string? SpaceTypeCode { get; private set; }

    public RequirementItemExperienceResolutionState ResolutionState { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public User UpdatedByUser { get; private set; } = null!;

    public IReadOnlyCollection<RequirementItemExperienceAnswer> Answers => _answers.AsReadOnly();

    public static RequirementItemExperienceDraft Create(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        string catalogVersion,
        string? spaceTypeCode,
        IReadOnlyCollection<RequirementItemExperienceAnswerValue> answers,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        if (technicalProposalId == Guid.Empty)
        {
            throw new ArgumentException("Technical proposal id is required.", nameof(technicalProposalId));
        }

        if (technicalProposalItemId == Guid.Empty)
        {
            throw new ArgumentException("Technical proposal item id is required.", nameof(technicalProposalItemId));
        }

        if (string.IsNullOrWhiteSpace(catalogVersion))
        {
            throw new ArgumentException("Catalog version is required.", nameof(catalogVersion));
        }

        if (updatedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Updated by user id is required.", nameof(updatedByUserId));
        }

        EnsureUniqueBenefits(answers);

        return new RequirementItemExperienceDraft(
            technicalProposalId,
            technicalProposalItemId,
            catalogVersion.Trim(),
            spaceTypeCode,
            answers,
            updatedByUserId,
            updatedAtUtc);
    }

    public bool ReplaceDraft(
        string catalogVersion,
        string? spaceTypeCode,
        IReadOnlyCollection<RequirementItemExperienceAnswerValue> answers,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(catalogVersion))
        {
            throw new ArgumentException("Catalog version is required.", nameof(catalogVersion));
        }

        if (updatedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Updated by user id is required.", nameof(updatedByUserId));
        }

        EnsureUniqueBenefits(answers);

        var normalizedCatalogVersion = catalogVersion.Trim();
        var normalizedSpaceTypeCode = NormalizeOptional(spaceTypeCode);
        if (IsSameDraft(normalizedCatalogVersion, normalizedSpaceTypeCode, answers))
        {
            return false;
        }

        CatalogVersion = normalizedCatalogVersion;
        SpaceTypeCode = normalizedSpaceTypeCode;
        ResolutionState = RequirementItemExperienceResolutionState.Pending;
        Revision += 1;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc;
        ReplaceAnswers(answers);
        return true;
    }

    private bool IsSameDraft(
        string catalogVersion,
        string? spaceTypeCode,
        IReadOnlyCollection<RequirementItemExperienceAnswerValue> answers)
    {
        if (!string.Equals(CatalogVersion, catalogVersion, StringComparison.Ordinal)
            || !string.Equals(SpaceTypeCode, spaceTypeCode, StringComparison.Ordinal))
        {
            return false;
        }

        if (_answers.Count != answers.Count)
        {
            return false;
        }

        var current = _answers
            .Select(value => (value.BenefitCode, value.OptionCode))
            .OrderBy(value => value.BenefitCode, StringComparer.Ordinal)
            .ThenBy(value => value.OptionCode, StringComparer.Ordinal)
            .ToArray();
        var next = answers
            .Select(value => (BenefitCode: value.BenefitCode.Trim(), OptionCode: value.OptionCode.Trim()))
            .OrderBy(value => value.BenefitCode, StringComparer.Ordinal)
            .ThenBy(value => value.OptionCode, StringComparer.Ordinal)
            .ToArray();

        return current.SequenceEqual(next);
    }

    private void ReplaceAnswers(IReadOnlyCollection<RequirementItemExperienceAnswerValue> answers)
    {
        _answers.Clear();
        foreach (var answer in answers.OrderBy(value => value.BenefitCode, StringComparer.Ordinal))
        {
            _answers.Add(RequirementItemExperienceAnswer.Create(answer.BenefitCode, answer.OptionCode));
        }
    }

    private static void EnsureUniqueBenefits(IReadOnlyCollection<RequirementItemExperienceAnswerValue> answers)
    {
        var duplicate = answers
            .GroupBy(value => value.BenefitCode.Trim(), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate experience benefit '{duplicate.Key}'.", nameof(answers));
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed class RequirementItemExperienceAnswer
{
    private RequirementItemExperienceAnswer()
    {
        BenefitCode = string.Empty;
        OptionCode = string.Empty;
    }

    private RequirementItemExperienceAnswer(string benefitCode, string optionCode)
    {
        Id = Guid.NewGuid();
        BenefitCode = benefitCode.Trim();
        OptionCode = optionCode.Trim();
    }

    public Guid Id { get; private set; }

    public Guid DraftId { get; private set; }

    public RequirementItemExperienceDraft Draft { get; private set; } = null!;

    public string BenefitCode { get; private set; }

    public string OptionCode { get; private set; }

    public static RequirementItemExperienceAnswer Create(string benefitCode, string optionCode)
    {
        if (string.IsNullOrWhiteSpace(benefitCode))
        {
            throw new ArgumentException("Benefit code is required.", nameof(benefitCode));
        }

        if (string.IsNullOrWhiteSpace(optionCode))
        {
            throw new ArgumentException("Option code is required.", nameof(optionCode));
        }

        return new RequirementItemExperienceAnswer(benefitCode, optionCode);
    }
}

public readonly record struct RequirementItemExperienceAnswerValue(string BenefitCode, string OptionCode);

public enum RequirementItemExperienceResolutionState
{
    Pending = 0
}
