using Platform.Access.Domain.Enums;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;

namespace Platform.Api.Modules.AssignmentsAccess;

// ASN-04 / ASN-06 TIM 15
// Reads the account status from the Identity module through its public contract
// (IIdentityContext). It never exposes passwords, tokens or session identifiers.
public sealed class IdentityAccountStateReader(IIdentityContext identity) : IAccountStateReader
{
    public async ValueTask<AccountState> GetStateAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty)
        {
            return AccountState.Unknown;
        }

        var account = await identity.FindByIdAsync(accountId, cancellationToken).ConfigureAwait(false);

        return account?.Status switch
        {
            AccountStatus.Active => AccountState.Active,
            AccountStatus.Blocked => AccountState.Blocked,
            AccountStatus.Archived => AccountState.Archived,
            _ => AccountState.Unknown
        };
    }
}
