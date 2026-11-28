namespace Platform.Identity.Options.Mfa;

/// <summary>
/// IDN-06: where the Data Protection key ring that encrypts TOTP secrets is kept, and the certificate that encrypts
/// the keys at rest. Losing the key ring makes every stored TOTP secret unreadable, so outside Development both
/// values are required and the host does not start without them. Secrets (the certificate password) come from
/// environment variables or user-secrets, never from the repository (ADM-04).
/// </summary>
public sealed class MfaKeyRingOptions
{
    public const string Section = "Identity:DataProtection";

    /// <summary>Directory for the key ring. Must survive restarts and be part of the backup with the database.</summary>
    public string? KeysPath { get; set; }

    /// <summary>PFX file with an RSA private key; it encrypts every key written to <see cref="KeysPath"/>.</summary>
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    internal bool IsConfigured =>
        !string.IsNullOrWhiteSpace(KeysPath) && !string.IsNullOrWhiteSpace(CertificatePath);
}
