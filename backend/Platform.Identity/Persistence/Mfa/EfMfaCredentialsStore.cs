using Microsoft.EntityFrameworkCore;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces.Mfa;

namespace Platform.Identity.Persistence.Mfa;

public class EfMfaCredentialsStore(
    PlatformIdentityDbContext db,
    TimeProvider clock) : IMfaCredentialsStore
{
    private readonly PlatformIdentityDbContext _db = db;
    private readonly TimeProvider _clock = clock;

    public async Task<MfaCredentials?> GetAsync(Guid userId, CancellationToken ct)
    {
        return await _db.MfaCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
    }

    public async Task<string?> GetEmailAsync(Guid userId, CancellationToken ct)
    {
        return await _db.Accounts
            .Where(account => account.Id == userId)
            .Select(account => account.Email)
            .SingleOrDefaultAsync(ct);
    }

    public async Task<bool> CreatePendingAsync(
        Guid userId,
        string encryptedSecret,
        int keyVersion,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        var mfaCredentials = await GetAsync(userId, ct);
        if (mfaCredentials is null)
        {
            mfaCredentials = new MfaCredentials(userId);
            await _db.MfaCredentials.AddAsync(mfaCredentials, ct);
        }

        if (mfaCredentials.MfaEnabled)
            return false;

        if (mfaCredentials.PendingTotpSecretCiphertext is not null
            && mfaCredentials.PendingEnrollmentExpiresAt > _clock.GetUtcNow())
            return false;

        mfaCredentials.PendingTotpSecretCiphertext = encryptedSecret;
        mfaCredentials.PendingTotpSecretKeyVersion = keyVersion;
        mfaCredentials.PendingEnrollmentExpiresAt = expiresAt;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ConfirmAsync(Guid userId, long timeStepMatched, CancellationToken ct)
    {
        var mfaCredentials = await GetAsync(userId, ct);
        if (mfaCredentials is null)
            return false;

        mfaCredentials.MfaEnabled = true;
        mfaCredentials.TotpSecretCiphertext = mfaCredentials.PendingTotpSecretCiphertext;
        mfaCredentials.TotpSecretKeyVersion = mfaCredentials.PendingTotpSecretKeyVersion;
        mfaCredentials.LastTotpTimeStep = timeStepMatched;
        mfaCredentials.PendingTotpSecretCiphertext = null;
        mfaCredentials.PendingTotpSecretKeyVersion = null;
        mfaCredentials.PendingEnrollmentExpiresAt = null;
        mfaCredentials.MfaFailedAttempts = 0;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> CompleteVerificationAsync(Guid userId, long timeStepMatched, CancellationToken ct)
    {
        var credentials = await GetAsync(userId, ct);
        if (credentials is null || !credentials.MfaEnabled)
            return false;

        credentials.LastTotpTimeStep = timeStepMatched;
        credentials.MfaFailedAttempts = 0;
        credentials.MfaLockedUntil = null;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RecordFailedAttemptAsync(
        Guid userId,
        int maxAttempts,
        DateTimeOffset lockedUntil,
        CancellationToken ct)
    {
        var credentials = await GetAsync(userId, ct);
        if (credentials is null || !credentials.MfaEnabled)
            return false;

        credentials.MfaFailedAttempts++;
        if (credentials.MfaFailedAttempts >= maxAttempts)
            credentials.MfaLockedUntil = lockedUntil;

        await _db.SaveChangesAsync(ct);
        return credentials.MfaFailedAttempts >= maxAttempts;
    }
}