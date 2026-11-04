using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

public interface IIdentityContext
{
    ValueTask<AccountSnapshot?> FindByIdAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}