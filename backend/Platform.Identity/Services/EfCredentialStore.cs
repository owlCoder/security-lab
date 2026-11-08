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
    public Task ReserveAttemptAsync(
        string normalizedEmail, int maxAttempts, DateTimeOffset now, DateTimeOffset lockoutEnd, CancellationToken ct = default) =>
        NotLocked(now)
            .Where(a => a.Email == normalizedEmail)
            .ExecuteUpdateAsync(set => set
                .SetProperty(a => a.LockoutEnd, a => a.FailedLoginCount >= maxAttempts ? lockoutEnd : a.LockoutEnd)
                .SetProperty(a => a.FailedLoginCount, a => a.FailedLoginCount >= maxAttempts ? 0 : a.FailedLoginCount + 1), ct);

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
