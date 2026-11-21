using Platform.Identity.Models;

namespace Platform.Identity.Interfaces;

/// <summary>
/// IDN-08: maps a validated external identity to exactly one internal account.
/// Repeated calls with the same (issuer, subject) always return the same account id
/// and never create a second link, also when two first logins run concurrently.
/// Account status is not decided here: the session store re-checks it (IDN-03).
/// </summary>
public interface IExternalIdentityLinker
{
    Task<FederatedLoginResult> ResolveAsync(ExternalIdentity identity, CancellationToken ct = default);
}