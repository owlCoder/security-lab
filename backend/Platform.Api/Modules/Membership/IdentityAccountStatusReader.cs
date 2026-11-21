using Platform.Access.Domain.Enums;
using Platform.Membership.Contracts;
using IdentityAccountStatus = Platform.Identity.Enums.AccountStatus;
using IdentityStatusReader = Platform.Identity.Interfaces.IAccountStatusReader;

namespace Platform.Api.Modules.Membership;

/// <summary>
/// ACC-02: status naloga koji se upisuje citamo preko ugovora Identity modula (IDN-03),
/// iz istog izvora koji koristi prijava (identity.accounts). Scoped, jer Identity citac koristi DbContext.
/// Nepostojeci nalog vraca null, pa upis ne prolazi (fail-closed).
/// </summary>
public sealed class IdentityAccountStatusReader(IdentityStatusReader identity) : IAccountStatusReader
{
    public async ValueTask<AccountState?> GetStatusAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await identity.FindAsync(accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        return account.Status switch
        {
            IdentityAccountStatus.Active => AccountState.Active,
            IdentityAccountStatus.Blocked => AccountState.Blocked,
            _ => AccountState.Archived
        };
    }
}
