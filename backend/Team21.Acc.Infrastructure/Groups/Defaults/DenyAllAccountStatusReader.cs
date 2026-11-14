using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;

namespace Team21.Acc.Infrastructure.Groups.Defaults;

/// <summary>
/// Used outside Development until the IDN-03 module is connected. Every account is inactive.
/// </summary>
public sealed class DenyAllAccountStatusReader : IAccountStatusReader
{
    public Task<AccountStatus> GetStatusAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(AccountStatus.Inactive);
}
