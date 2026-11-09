using Platform.Identity.Enums;

namespace Platform.Identity.Models;

/// <param name="IsValid">True only when the account exists, is not locked and the password matches.</param>
/// <param name="IsLockedOut">IDN-05: for the audit only. The client must get the same answer as for a wrong password.</param>
public sealed record CredentialResult(
    bool IsValid,
    Guid AccountId = default,
    AccountStatus Status = default,
    bool IsLockedOut = false)
{
    public static CredentialResult Invalid { get; } = new(false);

    public static CredentialResult LockedOut(Guid accountId) => new(false, accountId, IsLockedOut: true);
}
