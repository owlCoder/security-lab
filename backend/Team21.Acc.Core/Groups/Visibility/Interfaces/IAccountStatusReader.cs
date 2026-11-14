namespace Team21.Acc.Core.Groups.Visibility.Interfaces;

/// <summary>Reads whether an account is active. Owned by IDN-03.</summary>
public interface IAccountStatusReader
{
    Task<AccountStatus> GetStatusAsync(Guid accountId, CancellationToken ct = default);
}
