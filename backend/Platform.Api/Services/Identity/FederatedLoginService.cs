using System.Security.Claims;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;

namespace Platform.Api.Services.Identity;

/// <summary>
/// IDN-08: validated OIDC principal -> one internal account -> platform session.
/// Every refusal gives the client the same answer; only the audit reason code tells them apart.
/// </summary>
internal sealed class FederatedLoginService(
    IExternalIdentityLinker linker,
    ISessionCreator sessions,
    ISessionManager sessionManager,
    IdentityAuditProducer audit) : IFederatedLoginService
{
    public async Task<LoginResult> ExecuteAsync(
        ClaimsPrincipal principal, string correlationId, CancellationToken ct = default)
    {
        var identity = ReadIdentity(principal);
        if (identity is null)
            return await FailAsync("oidc_invalid_identity", correlationId, null, ct);

        var link = await linker.ResolveAsync(identity, ct);
        if (!link.IsSuccess)
            return await FailAsync(ReasonCode(link.Outcome), correlationId, null, ct);

        var accountId = link.AccountId!.Value;

        // MFA (IDN-06) is not enforced yet for any login. When it is, federated login must follow the same policy.
        IssuedSession? session;
        try
        {
            session = await sessions.CreateAsync(accountId, AuthenticationLevel.Active, ct);
        }
        catch (AccountNotActiveException)
        {
            // Blocked or archived account (IDN-03): the provider proved who the caller is, but no session is issued.
            return await FailAsync("account_inactive", correlationId, accountId, ct, actorConfirmed: true);
        }

        if (session is null)
            return await FailAsync("session_unavailable", correlationId, accountId, ct, actorConfirmed: true,
                outcome: LoginOutcome.Unavailable);

        // Same rule as password login: no session for a login that could not be durably audited.
        var auditResult = await audit.LoginSucceededAsync(accountId, correlationId, CancellationToken.None);
        if (!auditResult.IsRecorded)
        {
            await sessionManager.RevokeSession(accountId, session.Handle, CancellationToken.None);
            return new LoginResult(LoginOutcome.Unavailable);
        }

        return new LoginResult(LoginOutcome.Success, accountId, session);
    }

    /// <summary>
    /// Reads the claims of the validated id_token. Needs MapInboundClaims = false, so "sub" keeps its name.
    /// The issuer comes from the claim itself (set by the token handler from the validated "iss"),
    /// never from the request.
    /// </summary>
    internal static ExternalIdentity? ReadIdentity(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirst("sub");
        if (subject is null ||
            string.IsNullOrWhiteSpace(subject.Value) ||
            string.IsNullOrWhiteSpace(subject.Issuer) ||
            subject.Issuer == ClaimsIdentity.DefaultIssuer)
            return null;

        var email = principal.FindFirst("email")?.Value;
        var emailVerified = string.Equals(
            principal.FindFirst("email_verified")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        return new ExternalIdentity(subject.Issuer, subject.Value, email, emailVerified);
    }

    // Audit reason codes: ^[a-z][a-z0-9_]{0,31}$
    internal static string ReasonCode(FederatedLoginOutcome outcome) => outcome switch
    {
        FederatedLoginOutcome.InvalidIdentity => "oidc_invalid_identity",
        FederatedLoginOutcome.EmailNotVerified => "oidc_email_not_verified",
        FederatedLoginOutcome.NoMatchingAccount => "oidc_no_account",
        FederatedLoginOutcome.AccountAlreadyLinked => "oidc_account_already_linked",
        _ => "oidc_refused"
    };

    private async Task<LoginResult> FailAsync(
        string reasonCode,
        string correlationId,
        Guid? confirmedUserId,
        CancellationToken ct,
        bool actorConfirmed = false,
        LoginOutcome outcome = LoginOutcome.InvalidCredentials)
    {
        await audit.LoginFailedAsync(reasonCode, correlationId, confirmedUserId, ct, actorConfirmed);
        return new LoginResult(outcome);
    }
}