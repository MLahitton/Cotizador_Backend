using Domain.Identity;

namespace Domain.PreQuotes;

public sealed class RequirementItemExperienceTechnicalSelection
{
    private RequirementItemExperienceTechnicalSelection() { }

    private RequirementItemExperienceTechnicalSelection(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        Guid? systemId,
        Guid? glassTypeId,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        Id = Guid.NewGuid();
        TechnicalProposalId = technicalProposalId;
        TechnicalProposalItemId = technicalProposalItemId;
        SystemId = EmptyToNull(systemId);
        GlassTypeId = EmptyToNull(glassTypeId);
        Revision = 1;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TechnicalProposalId { get; private set; }

    public RequirementTechnicalProposal TechnicalProposal { get; private set; } = null!;

    public Guid TechnicalProposalItemId { get; private set; }

    public RequirementTechnicalProposalItem TechnicalProposalItem { get; private set; } = null!;

    public Guid? SystemId { get; private set; }

    public Guid? GlassTypeId { get; private set; }

    public long Revision { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public User UpdatedByUser { get; private set; } = null!;

    public static RequirementItemExperienceTechnicalSelection Create(
        Guid technicalProposalId,
        Guid technicalProposalItemId,
        Guid? systemId,
        Guid? glassTypeId,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        EnsureRequired(technicalProposalId, nameof(technicalProposalId));
        EnsureRequired(technicalProposalItemId, nameof(technicalProposalItemId));
        EnsureRequired(updatedByUserId, nameof(updatedByUserId));
        Requirement.EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));

        return new RequirementItemExperienceTechnicalSelection(
            technicalProposalId,
            technicalProposalItemId,
            systemId,
            glassTypeId,
            updatedByUserId,
            updatedAtUtc);
    }

    public bool ReplaceSystem(
        Guid? systemId,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        return Replace(systemId, GlassTypeId, updatedByUserId, updatedAtUtc);
    }

    public bool ReplaceGlass(
        Guid? glassTypeId,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        return Replace(SystemId, glassTypeId, updatedByUserId, updatedAtUtc);
    }

    private bool Replace(
        Guid? systemId,
        Guid? glassTypeId,
        Guid updatedByUserId,
        DateTimeOffset updatedAtUtc)
    {
        EnsureRequired(updatedByUserId, nameof(updatedByUserId));
        Requirement.EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));

        systemId = EmptyToNull(systemId);
        glassTypeId = EmptyToNull(glassTypeId);
        if (SystemId == systemId && GlassTypeId == glassTypeId)
        {
            return false;
        }

        SystemId = systemId;
        GlassTypeId = glassTypeId;
        checked
        {
            Revision += 1;
        }

        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = updatedAtUtc;
        return true;
    }

    private static Guid? EmptyToNull(Guid? value) =>
        value == Guid.Empty ? null : value;

    private static void EnsureRequired(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("El identificador es obligatorio.", parameterName);
        }
    }
}
