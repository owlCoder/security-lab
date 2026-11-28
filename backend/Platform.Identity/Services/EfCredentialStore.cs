using Microsoft.EntityFrameworkCore;
using Platform.Identity.Entities;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

internal sealed class EfCredentialStore(PlatformIdentityDbContext db) : ICredentialStore
{
    public Task<CredentialRecord?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        db.Accounts
            .AsNoTracking()
            .Where(account => account.Email == normalizedEmail)
            .Select(account => new CredentialRecord(
                account.Id,
                account.Status,
                account.PasswordHash,
                account.FailedLoginCount,
                account.LockoutEnd,
                account.Email,
                account.EmailConfirmed,
                account.NewSessionNotificationsEnabled))
            .FirstOrDefaultAsync(ct);

    // Each change below is one UPDATE, so parallel requests see each other's writes.
    // ExecuteUpdate reports only the number of rows, which is 1 for a charge and for a lockout alike. RETURNING gives
    // the counter this UPDATE wrote: a charge leaves at least 1, a lockout restarts it at 0, no row means locked or unknown.
    public async Task<bool> ReserveAttemptAsync(
        string normalizedEmail, int maxAttempts, DateTimeOffset now, DateTimeOffset lockoutEnd, CancellationToken ct = default)
    {
        var counters = await db.Database.SqlQuery<int>($"""
            UPDATE identity.accounts
            SET lockout_end = CASE WHEN failed_login_count >= {maxAttempts} THEN {lockoutEnd} ELSE lockout_end END,
                failed_login_count = CASE WHEN failed_login_count >= {maxAttempts} THEN 0 ELSE failed_login_count + 1 END
            WHERE email = {normalizedEmail} AND (lockout_end IS NULL OR lockout_end <= {now})
            RETURNING failed_login_count AS "Value"
            """).ToListAsync(ct);

        return counters is [> 0];
    }

    public async Task<DateTimeOffset?> LockIfLimitReachedAsync(
        string normalizedEmail, int maxAttempts, DateTimeOffset now, DateTimeOffset lockoutEnd, CancellationToken ct = default)
    {
        await NotLocked(now)
            .Where(a => a.Email == normalizedEmail && a.FailedLoginCount >= maxAttempts)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.LockoutEnd, lockoutEnd)
                .SetProperty(a => a.FailedLoginCount, 0), ct);

        return await db.Accounts
            .Where(a => a.Email == normalizedEmail)
            .Select(a => a.LockoutEnd)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TryClearFailuresAsync(Guid accountId, DateTimeOffset now, CancellationToken ct = default) =>
        await NotLocked(now)
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.FailedLoginCount, 0)
                .SetProperty(a => a.LockoutEnd, (DateTimeOffset?)null), ct) == 1;

    private IQueryable<UserAccount> NotLocked(DateTimeOffset now) =>
        db.Accounts.Where(a => a.LockoutEnd == null || a.LockoutEnd <= now);
}
