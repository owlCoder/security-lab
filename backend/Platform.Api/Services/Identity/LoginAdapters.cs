using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
namespace Platform.Api.Services.Identity;

/// <summary>
/// Safe default until IDN-02 supplies real credential verification.
/// Login fails closed instead of accepting an unverified password.
/// </summary>
public sealed class UnavailableCredentialAuthenticator : ICredentialAuthenticator
{
    public CredentialAuthenticationResult Authenticate(string email, string password) =>
        new(CredentialAuthenticationOutcome.Unavailable);
}

/// <summary>
/// Safe default until IDN-02 supplies persistent, revocable sessions.
/// </summary>
public sealed class UnavailableSessionIssuer : ISessionIssuer
{
    public SessionIssueResult Issue(Guid accountId) => new(false);
}
