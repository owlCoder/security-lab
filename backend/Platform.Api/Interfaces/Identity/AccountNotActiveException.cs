namespace Platform.Api.Interfaces.Identity;

/// <summary>
/// Thrown by <see cref="ISessionCreator"/> when the account is no longer active at the moment the session would be
/// stored: it was blocked or archived after login checked its status (IDN-03, SEC-03). No session was created.
/// </summary>
public sealed class AccountNotActiveException(Guid accountId)
    : Exception("The account is not active, so no session was created.")
{
    public Guid AccountId { get; } = accountId;
}
