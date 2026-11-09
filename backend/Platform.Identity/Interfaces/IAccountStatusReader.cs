using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

/// <summary>Current server-owned account state, independent of an issued session.</summary>
public interface IAccountStatusReader
{
    Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default);
}
