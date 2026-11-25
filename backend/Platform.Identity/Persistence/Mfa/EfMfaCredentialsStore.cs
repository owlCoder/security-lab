using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
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
        // One statement, so parallel enrollments cannot both pass a read-then-write check. Without a row the INSERT
        // wins; a parallel INSERT waits for it and then takes the UPDATE path. The UPDATE runs only while MFA is
        // disabled and no unexpired enrollment is pending, checked again on the committed row after the wait.
        // 0 rows: MFA is enabled or an enrollment is still pending.
        var now = _clock.GetUtcNow();
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO identity.mfa_credentials AS c
                (user_id, pending_totp_secret_ciphertext, pending_totp_secret_key_version, pending_enrollment_expires_at)
            VALUES ({userId}, {encryptedSecret}, {keyVersion}, {expiresAt})
            ON CONFLICT (user_id) DO UPDATE
            SET pending_totp_secret_ciphertext = EXCLUDED.pending_totp_secret_ciphertext,
                pending_totp_secret_key_version = EXCLUDED.pending_totp_secret_key_version,
                pending_enrollment_expires_at = EXCLUDED.pending_enrollment_expires_at
            WHERE NOT c.mfa_enabled
              AND (c.pending_totp_secret_ciphertext IS NULL OR c.pending_enrollment_expires_at <= {now})
            """, ct);
        return rows == 1;
    }

    public async Task<bool> RecordFailedAttemptAsync(
        Guid userId,
        int maxAttempts,
        DateTimeOffset now,
        DateTimeOffset lockedUntil,
        CancellationToken ct)
    {
        // One statement, so the row lock serializes parallel failures and each one sees the count of the one
        // before it. WHERE skips an account that is still locked, so any lockout left in a matching row has
        // expired: the counter starts again at 1 and the old lockout end is cleared. SET sees the old row, so
        // the new count is written out twice.
        var lockedAfter = await _db.Database.SqlQuery<bool>($"""
            UPDATE identity.mfa_credentials
            SET mfa_failed_attempts =
                    CASE WHEN mfa_locked_until IS NULL THEN mfa_failed_attempts + 1 ELSE 1 END,
                mfa_locked_until =
                    CASE WHEN (CASE WHEN mfa_locked_until IS NULL THEN mfa_failed_attempts + 1 ELSE 1 END) >= {maxAttempts}
                         THEN {lockedUntil}
                         ELSE NULL
                    END
            WHERE user_id = {userId}
              AND mfa_enabled
              AND (mfa_locked_until IS NULL OR mfa_locked_until <= {now})
            RETURNING mfa_locked_until IS NOT NULL AS "Value"
            """).ToListAsync(ct);

        // No row: MFA is not enabled, or the account is still locked. Nothing was counted; refuse as locked.
        return lockedAfter.Count == 0 || lockedAfter[0];
    }

    public async Task<IMfaUnitOfWork> BeginAsync(CancellationToken ct) =>
        new UnitOfWork(_db, await _db.Database.BeginTransactionAsync(ct));

    // Every operation is one UPDATE with its condition in WHERE. Under READ COMMITTED PostgreSQL locks the row;
    // a parallel UPDATE of the same row waits, then checks WHERE again against the committed row and matches
    // nothing. So the check and the change cannot be split by another request (no read-then-write race).
    private sealed class UnitOfWork(PlatformIdentityDbContext db, IDbContextTransaction transaction) : IMfaUnitOfWork
    {
        public DbTransaction Transaction => transaction.GetDbTransaction();

        public async Task<bool> TryConfirmEnrollmentAsync(
            Guid userId,
            string pendingCiphertext,
            long timeStep,
            DateTimeOffset now,
            CancellationToken ct)
        {
            var rows = await db.MfaCredentials
                .Where(c => c.UserId == userId
                            && !c.MfaEnabled
                            && c.PendingTotpSecretCiphertext == pendingCiphertext
                            && c.PendingEnrollmentExpiresAt > now
                            && (c.LastTotpTimeStep == null || c.LastTotpTimeStep < timeStep))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(c => c.MfaEnabled, true)
                    .SetProperty(c => c.TotpSecretCiphertext, c => c.PendingTotpSecretCiphertext)
                    .SetProperty(c => c.TotpSecretKeyVersion, c => c.PendingTotpSecretKeyVersion)
                    .SetProperty(c => c.LastTotpTimeStep, timeStep)
                    .SetProperty(c => c.PendingTotpSecretCiphertext, (string?)null)
                    .SetProperty(c => c.PendingTotpSecretKeyVersion, (int?)null)
                    .SetProperty(c => c.PendingEnrollmentExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(c => c.MfaFailedAttempts, 0)
                    .SetProperty(c => c.MfaLockedUntil, (DateTimeOffset?)null), ct);
            return rows == 1;
        }

        public async Task<bool> TryConsumeTimeStepAsync(
            Guid userId,
            long timeStep,
            DateTimeOffset now,
            CancellationToken ct)
        {
            var rows = await db.MfaCredentials
                .Where(c => c.UserId == userId
                            && c.MfaEnabled
                            && (c.LastTotpTimeStep == null || c.LastTotpTimeStep < timeStep)
                            && (c.MfaLockedUntil == null || c.MfaLockedUntil <= now))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(c => c.LastTotpTimeStep, timeStep)
                    .SetProperty(c => c.MfaFailedAttempts, 0)
                    .SetProperty(c => c.MfaLockedUntil, (DateTimeOffset?)null), ct);
            return rows == 1;
        }

        public async Task<bool> TryPromoteSessionAsync(Guid sessionId, DateTimeOffset now, CancellationToken ct)
        {
            var rows = await db.Sessions
                .Where(s => s.Id == sessionId
                            && s.AuthLevel == AuthLevel.MfaPending
                            && s.RevokedAt == null
                            && s.ExpiresAt > now)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.AuthLevel, AuthLevel.Active), ct);
            return rows == 1;
        }

        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
