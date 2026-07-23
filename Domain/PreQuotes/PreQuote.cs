using Domain.Identity;
using Domain.Projects;

namespace Domain.PreQuotes;

public sealed class PreQuote
{
    private PreQuote()
    {
    }

    private PreQuote(
        Guid id,
        Guid projectId,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ProjectId = projectId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Project Project { get; private set; } = null!;

    public User CreatedByUser { get; private set; } = null!;

    public static PreQuote Create(
        Guid projectId,
        Guid createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException(
                "El proyecto es obligatorio.",
                nameof(projectId));
        }

        if (createdByUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "El usuario creador es obligatorio.",
                nameof(createdByUserId));
        }

        return new PreQuote(
            Guid.NewGuid(),
            projectId,
            createdByUserId,
            createdAtUtc);
    }

    public void RegisterActivity(DateTimeOffset activityAtUtc)
    {
        if (activityAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actividad no puede ser anterior a la última actualización.",
                nameof(activityAtUtc));
        }

        UpdatedAtUtc = activityAtUtc;
    }
}
