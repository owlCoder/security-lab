using Microsoft.Extensions.Options;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Identity.Services;

public sealed class CredentialVerifier(
    ICredentialStore store,
    IPasswordHasher hasher,
    IOptions<IdentityLockoutOptions> lockout,
    TimeProvider clock) : ICredentialVerifier
{
    public const int MaxEmailLength = 320;
    public const int MaxPasswordLength = 128;

    /// <summary>Accounts must be stored with this form of the e-mail (also when seeded).</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public async Task<CredentialResult> VerifyAsync(string email, string password, CancellationToken ct = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var options = lockout.Value;
        var lockoutSpan = TimeSpan.FromMinutes(options.LockoutMinutes);

        // IDN-05: the attempt is charged before the slow hash. A check made first would let every parallel
        // request pass it, and an abandoned request would be a free guess. A failure stays charged; a success clears it.
        var now = clock.GetUtcNow();
        await store.ReserveAttemptAsync(normalizedEmail, options.MaxFailedAccessAttempts, now, now + lockoutSpan, ct);
        var record = await store.FindByEmailAsync(normalizedEmail, ct);
        var wasLocked = record?.LockoutEnd > now;

        // Verify runs even when the account is unknown or locked, so timing does not reveal which e-mails exist.
        var passwordMatches = hasher.Verify(password, record?.PasswordHash);

        now = clock.GetUtcNow();

        // The account may have locked during the hash, so the store decides whether this login still counts.
        if (record is not null && passwordMatches && !wasLocked
            && await store.TryClearFailuresAsync(record.AccountId, now, ct))
            return new CredentialResult(true, record.AccountId, record.Status);

        // Unknown e-mails and locked accounts run the same statements and change nothing, so timing stays equal.
        var lockoutEnd = await store.LockIfLimitReachedAsync(
            normalizedEmail, options.MaxFailedAccessAttempts, now, now + lockoutSpan, ct);

        return record is not null && (wasLocked || lockoutEnd > now)
            ? CredentialResult.LockedOut(record.AccountId)
            : CredentialResult.Invalid;
    }
}
