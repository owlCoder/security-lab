using Microsoft.EntityFrameworkCore;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

public sealed class SessionNotificationPreferences(PlatformIdentityDbContext db)
    : ISessionNotificationPreferences
{
    public Task<bool?> GetAsync(Guid accountId, CancellationToken ct = default) =>
        db.Accounts
            .Where(account => account.Id == accountId)
            .Select(account => (bool?)account.NewSessionNotificationsEnabled)
            .SingleOrDefaultAsync(ct);

    public async Task<bool> SetAsync(Guid accountId, bool enabled, CancellationToken ct = default)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
            return false;

        account.SetNewSessionNotificationsEnabled(enabled);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
