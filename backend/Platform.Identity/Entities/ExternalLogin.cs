namespace Platform.Identity.Entities;

/// <summary>
/// IDN-08: one external OIDC identity (issuer + subject) linked to exactly one internal account.
/// The pair (Issuer, Subject) is unique, so a repeated identity always maps to the same account.
/// </summary>
public sealed class ExternalLogin
{
    public const int MaxIssuerLength = 512;
    public const int MaxSubjectLength = 255; // OIDC Core: sub is at most 255 ASCII characters

    private ExternalLogin() { } // EF Core

    public ExternalLogin(Guid id, Guid accountId, string issuer, string subject, DateTimeOffset linkedAt)
    {
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > MaxIssuerLength)
            throw new ArgumentException("Invalid issuer.", nameof(issuer));
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength)
            throw new ArgumentException("Invalid subject.", nameof(subject));

        Id = id;
        AccountId = accountId;
        Issuer = issuer;
        Subject = subject;
        LinkedAt = linkedAt;
        LastLoginAt = linkedAt;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }

    /// <summary>The "iss" claim, exactly as validated by the OIDC handler (case-sensitive).</summary>
    public string Issuer { get; private set; } = default!;

    /// <summary>The "sub" claim. Stable per issuer; never reassigned by the provider.</summary>
    public string Subject { get; private set; } = default!;

    public DateTimeOffset LinkedAt { get; private set; }
    public DateTimeOffset LastLoginAt { get; private set; }

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;
}