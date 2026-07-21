namespace Domain.Identity;

public sealed class ExternalIdentity
{
    private ExternalIdentity()
    {
    }

    private ExternalIdentity(
        Guid id,
        Guid userId,
        ExternalIdentityProvider provider,
        string providerSubject,
        string providerEmail,
        bool emailVerified,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        UserId = userId;
        Provider = provider;
        ProviderSubject = NormalizeRequired(
            providerSubject,
            nameof(providerSubject));

        ProviderEmail = NormalizeEmail(providerEmail);
        EmailVerified = emailVerified;
        CreatedAtUtc = createdAtUtc;
        LastUsedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public ExternalIdentityProvider Provider { get; private set; }

    public string ProviderSubject { get; private set; } = string.Empty;

    public string ProviderEmail { get; private set; } = string.Empty;

    public bool EmailVerified { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset LastUsedAtUtc { get; private set; }

    public User User { get; private set; } = null!;

    public static ExternalIdentity CreateGoogleIdentity(
        Guid userId,
        string providerSubject,
        string providerEmail,
        bool emailVerified,
        DateTimeOffset createdAtUtc)
    {
        return new ExternalIdentity(
            Guid.NewGuid(),
            userId,
            ExternalIdentityProvider.Google,
            providerSubject,
            providerEmail,
            emailVerified,
            createdAtUtc);
    }

    public void RegisterUse(
        string providerEmail,
        bool emailVerified,
        DateTimeOffset usedAtUtc)
    {
        ProviderEmail = NormalizeEmail(providerEmail);
        EmailVerified = emailVerified;
        LastUsedAtUtc = usedAtUtc;
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
}