using System.Security.Claims;
using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

/// <summary>
/// IDN-08: login with an identity the OIDC handler has already validated (signature, issuer, audience,
/// expiry, nonce, PKCE). The result and the session are the same as for password login (IDN-02).
/// </summary>
public interface IFederatedLoginService
{
    Task<LoginResult> ExecuteAsync(ClaimsPrincipal principal, string correlationId, CancellationToken ct = default);
}