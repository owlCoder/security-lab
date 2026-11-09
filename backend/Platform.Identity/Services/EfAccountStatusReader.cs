using Microsoft.EntityFrameworkCore;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Persistence;

namespace Platform.Identity.Services;

internal sealed class EfAccountStatusReader(PlatformIdentityDbContext db) : IAccountStatusReader
{
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        db.Accounts.AsNoTracking().Where(account => account.Id == accountId)
            .Select(account => new AccountSnapshot(account.Id, account.Status))
            .SingleOrDefaultAsync(cancellationToken);
}
