using Platform.Access.Domain.Enums;
using Platform.Membership.Contracts;
namespace Platform.Membership.Infrastructure.Accounts;
/// <summary>
/// Podrazumevana implementacija dok Platform.Api ne registruje pravi adapter:
/// nijedan nalog "ne postoji", pa nijedan upis ne prolazi (fail-closed).
/// </summary>
public sealed class UnavailableAccountStatusReader : IAccountStatusReader
{
    public ValueTask<AccountState?> GetStatusAsync(
    Guid accountId,
    CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<AccountState?>(null);
    }
}