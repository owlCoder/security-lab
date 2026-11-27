using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;

namespace Platform.Api.Services.Identity;

/// <summary>
/// IDN-03: writes the status to <c>identity.accounts</c>, the table login reads. The status change and the
/// session revocation use one <see cref="PlatformIdentityDbContext"/>, so they share one transaction.
/// </summary>
public sealed class EfAccountStatusStore(PlatformIdentityDbContext db) : IAccountStatusStore
{
    public async Task<IAccountStatusUnitOfWork> BeginAsync(CancellationToken ct = default) =>
        new UnitOfWork(db, await db.Database.BeginTransactionAsync(ct));

    private sealed class UnitOfWork(PlatformIdentityDbContext db, IDbContextTransaction transaction)
        : IAccountStatusUnitOfWork
    {
        public DbTransaction Transaction => transaction.GetDbTransaction();

        // FOR UPDATE serializes concurrent changes of the same account (same pattern as REV-05).
        public Task<UserAccount?> FindForUpdateAsync(Guid accountId, CancellationToken ct = default) =>
            db.Accounts.FromSqlInterpolated($"""
                SELECT * FROM identity.accounts WHERE id = {accountId} FOR UPDATE
                """).SingleOrDefaultAsync(ct);

        public async Task<int> ApplyAsync(UserAccount account, AccountStatus newStatus, CancellationToken ct = default)
        {
            account.Status = newStatus;
            await db.SaveChangesAsync(ct);

            // Blocked and archived accounts lose all sessions at once (IDN-03, UC-05).
            return newStatus is AccountStatus.Blocked or AccountStatus.Archived
                ? await new SessionManager(db).RevokeAllForUser(account.Id, "account status change", ct)
                : 0;
        }

        // IDN-05: the row lock from FindForUpdateAsync serializes administrator writes to the account (unlock,
        // status change). It does not hold back logins during an unlock: the account is still locked in the
        // committed row, and the login UPDATEs match only unlocked rows, so they skip it without waiting and
        // charge nothing. CredentialVerifier refuses an attempt that was not charged (also one that only set the
        // lockout cleared here), even when its later read already shows the account unlocked
        // (AccountLockoutTests.Login_in_flight_during_an_admin_unlock_..., Login_that_sets_the_lockout_itself_...).
        public async Task<DateTimeOffset?> ClearLockoutAsync(UserAccount account, DateTimeOffset now, CancellationToken ct = default)
        {
            if (account.LockoutEnd is not { } end || end <= now)
                return null;

            account.ClearLockout();
            await db.SaveChangesAsync(ct);
            return end;
        }

        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
