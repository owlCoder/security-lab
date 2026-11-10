using System.Data.Common;
using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Api.Interfaces.Identity;

/// <summary>
/// IDN-03 / ADM-02: the single place where an account status is written. It is the same PostgreSQL
/// store (<c>identity.accounts</c>) that login reads, so a changed status is seen by the next login.
/// </summary>
public interface IAccountStatusStore
{
    /// <summary>Opens one transaction. Nothing is stored unless <see cref="IAccountStatusUnitOfWork.CommitAsync"/> is called.</summary>
    Task<IAccountStatusUnitOfWork> BeginAsync(CancellationToken ct = default);
}

/// <summary>Disposing without a commit rolls everything back.</summary>
public interface IAccountStatusUnitOfWork : IAsyncDisposable
{
    /// <summary>The open database transaction, so that the Audit record can be written in it.</summary>
    DbTransaction Transaction { get; }

    /// <summary>Reads the account and locks its row until the transaction ends, so two changes cannot interleave.</summary>
    Task<UserAccount?> FindForUpdateAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Stores the new status. Blocking or archiving also revokes every active session of the account,
    /// in the same transaction. Returns the number of revoked sessions.
    /// </summary>
    Task<int> ApplyAsync(UserAccount account, AccountStatus newStatus, CancellationToken ct = default);

    Task CommitAsync(CancellationToken ct = default);
}
