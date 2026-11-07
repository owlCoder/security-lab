using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;

namespace Team21.Acc.Infrastructure.Groups.Fakes;

/// <summary>Development only. Stands in for the IDN-03 account status.</summary>
public sealed class FakeAccountStatusReader : IAccountStatusReader
{
    private readonly FakeDirectory _directory;

    public FakeAccountStatusReader(FakeDirectory directory)
    {
        _directory = directory;
    }

    public Task<AccountStatus> GetStatusAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(_directory.StatusOf(accountId));
}
