using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Services;

namespace Platform.Api.Services.Identity;

/// <summary>
/// US-04: e-mail + password login (DFD process P1). Unknown e-mail, wrong password, missing hash and
/// blocked/archived accounts all return the same InvalidCredentials; only the audit event tells them apart.
/// </summary>
internal sealed class LoginService(
    ICredentialVerifier credentials,
    ISessionCreator sessions,
    ISessionManager sessionManager,
    IdentityAuditProducer audit) : ILoginService
{
    public async Task<LoginResult> ExecuteAsync(
        string? email, string? password, string correlationId, CancellationToken ct = default)
    {
        if (!IsAcceptable(email, password))
            return await FailAsync(LoginOutcome.InvalidRequest, "invalid_request", correlationId, null, ct);

        // IDN-05 (attempt limiting) is not part of this story. It would be called here, before the check.
        var check = await credentials.VerifyAsync(email!, password!, ct);
        if (!check.IsValid)
            return await FailAsync(LoginOutcome.InvalidCredentials, "invalid_credentials", correlationId, null, ct);

        if (check.Status != AccountStatus.Active)
            return await FailAsync(
                LoginOutcome.InvalidCredentials,
                check.Status == AccountStatus.Blocked ? "account_blocked" : "account_archived",
                correlationId,
                check.AccountId,
                ct);

        // MFA (IDN-06) is not implemented yet: until an MFA policy exists every login is Active.
        var session = await sessions.CreateAsync(check.AccountId, AuthenticationLevel.Active, ct);
        if (session is null)
            return await FailAsync(
                LoginOutcome.Unavailable, "session_unavailable", correlationId, check.AccountId, ct);

        // The session has already been persisted. A disconnected client must not cancel
        // the evidence write for an action that has taken effect.
        var auditResult = await audit.LoginSucceededAsync(
            check.AccountId, correlationId, CancellationToken.None);
        if (!auditResult.IsRecorded)
        {
            // The client must never receive a session for a login that could not be
            // durably audited. Revoke the already-persisted session by its opaque
            // handle; neither the raw token nor the handle is written to Audit.
            await sessionManager.RevokeSession(
                check.AccountId, session.Handle, CancellationToken.None);
            return new LoginResult(LoginOutcome.Unavailable);
        }

        return new LoginResult(LoginOutcome.Success, check.AccountId, session);
    }

    private static bool IsAcceptable(string? email, string? password) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= CredentialVerifier.MaxEmailLength &&
        !string.IsNullOrEmpty(password) && password.Length <= CredentialVerifier.MaxPasswordLength;

    private async Task<LoginResult> FailAsync(
        LoginOutcome outcome,
        string reasonCode,
        string correlationId,
        Guid? confirmedUserId,
        CancellationToken cancellationToken)
    {
        await audit.LoginFailedAsync(
            reasonCode, correlationId, confirmedUserId, cancellationToken);
        return new LoginResult(outcome);
    }
}
