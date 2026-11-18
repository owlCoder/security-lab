namespace Platform.Identity.Entities;

public sealed class MfaCredentials
{
    private MfaCredentials()
    {
    }

    public MfaCredentials(Guid userId)
    {
        UserId = userId;
    }

    public Guid UserId { get; private set; }
    public bool MfaEnabled { get; set; } = false;
    public string? TotpSecretCiphertext { get; set; } = null;
    public int? TotpSecretKeyVersion { get; set; } = null;

    public string? PendingTotpSecretCiphertext { get; set; } = null;
    public int? PendingTotpSecretKeyVersion { get; set; } = null;
    public DateTimeOffset? PendingEnrollmentExpiresAt { get; set; } = null;

    public long? LastTotpTimeStep { get; set; } = null;
    public int MfaFailedAttempts { get; set; } = 0;
    public DateTimeOffset? MfaLockedUntil { get; set; } = null;
}