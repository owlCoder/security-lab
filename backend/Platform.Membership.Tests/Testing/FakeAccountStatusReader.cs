using Platform.Access.Domain.Enums;
using Platform.Membership.Contracts;
namespace Platform.Membership.Tests.Testing;

internal sealed class FakeAccountStatusReader : IAccountStatusReader
{
    private readonly Dictionary<Guid, AccountState> accounts = [];
    public FakeAccountStatusReader With(Guid accountId, AccountState state)
    {
        accounts[accountId] = state;
        return this;
    }
    public ValueTask<AccountState?> GetStatusAsync(
    Guid accountId,
    CancellationToken cancellationToken = default) =>
    ValueTask.FromResult(accounts.TryGetValue(accountId, out var state) ? state : (AccountState?)null);
}