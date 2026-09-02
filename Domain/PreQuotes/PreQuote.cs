using Domain.Identity;
using Domain.Projects;

namespace Domain.PreQuotes;

public sealed class PreQuote
{
    public const int MaxSerialLength = 20;
    public const int MaxNameLength = 160;

    private PreQuote()
    {
    }

    private PreQuote(
        Guid id,
        Guid projectId,
        Guid createdByUserId,
        string serial,
        string? name,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ProjectId = projectId;
        CreatedByUserId = createdByUserId;
        Serial = NormalizeRequired(serial, nameof(serial), MaxSerialLength);
        Name = NormalizeOptional(name, MaxNameLength);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string Serial { get; private set; } = null!;

    public string? Name { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public Project Project { get; private set; } = null!;

    public User CreatedByUser { get; private set; } = null!;

    public static PreQuote Create(
        Guid projectId,
        Guid createdByUserId,
        string serial,
        string? name,
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
            serial,
            name,
            createdAtUtc);
    }


    public static string FormatSerial(int year, int sequence)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(year),
                "El ano del serial no es valido.");
        }

        if (sequence < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "La secuencia del serial debe ser positiva.");
        }

        return $"PC-{year:D4}-{sequence:D4}";
    }

    public void UpdateName(string? name, DateTimeOffset updatedAtUtc)
    {
        EnsureValidUpdateDate(updatedAtUtc);
        Name = NormalizeOptional(name, MaxNameLength);
        UpdatedAtUtc = updatedAtUtc;
    }

    public void RegisterActivity(DateTimeOffset activityAtUtc)
    {
        if (activityAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actividad no puede ser anterior a la ultima actualizacion.",
                nameof(activityAtUtc));
        }

        UpdatedAtUtc = activityAtUtc;
    }
    private void EnsureValidUpdateDate(DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc < UpdatedAtUtc)
        {
            throw new ArgumentException(
                "La fecha de actualizacion no puede ser anterior a la ultima actualizacion.",
                nameof(updatedAtUtc));
        }
    }

    private static string NormalizeRequired(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("El valor es obligatorio.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                "El valor supera la longitud maxima permitida.",
                parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                "El valor supera la longitud maxima permitida.",
                nameof(value));
        }

        return normalized;
    }
}

public sealed class PreQuoteSerialCounter
{
    private PreQuoteSerialCounter()
    {
    }

    public int Year { get; private set; }

    public int NextSequence { get; private set; }
}