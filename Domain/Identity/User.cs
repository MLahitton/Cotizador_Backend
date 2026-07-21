namespace Domain.Identity;

public sealed class User
{
    private User()
    {
    }

    private User(
        Guid id,
        string email,
        string firstName,
        string? lastName,
        string? profilePictureUrl,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Email = NormalizeEmail(email);
        FirstName = NormalizeRequired(firstName, nameof(firstName));
        LastName = NormalizeOptional(lastName);
        ProfilePictureUrl = NormalizeOptional(profilePictureUrl);
        IsActive = true;
        LastLoginAtUtc = createdAtUtc;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string? LastName { get; private set; }

    public string? ProfilePictureUrl { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static User CreateFromGoogle(
        string email,
        string firstName,
        string? lastName,
        string? profilePictureUrl,
        DateTimeOffset createdAtUtc)
    {
        return new User(
            Guid.NewGuid(),
            email,
            firstName,
            lastName,
            profilePictureUrl,
            createdAtUtc);
    }

    public void RegisterGoogleLogin(
        string email,
        string firstName,
        string? lastName,
        string? profilePictureUrl,
        DateTimeOffset loginAtUtc)
    {
        Email = NormalizeEmail(email);
        FirstName = NormalizeRequired(firstName, nameof(firstName));
        LastName = NormalizeOptional(lastName);
        ProfilePictureUrl = NormalizeOptional(profilePictureUrl);
        LastLoginAtUtc = loginAtUtc;
        UpdatedAtUtc = loginAtUtc;
    }

    public void Activate(DateTimeOffset updatedAtUtc)
    {
        IsActive = true;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Deactivate(DateTimeOffset updatedAtUtc)
    {
        IsActive = false;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static string NormalizeEmail(string value)
    {
        return NormalizeRequired(value, nameof(value)).ToLowerInvariant();
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "El valor es obligatorio.",
                parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}