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

        var check = await credentials.VerifyAsync(email!, password!, ct);

        // IDN-05: a locked account gets the same answer as a wrong password; only the audit event differs.
        // The caller is not proven to own the account, so it is the target and not the actor.
        if (check.IsLockedOut)
            return await FailAsync(
                LoginOutcome.InvalidCredentials,
                "account_locked",
                correlationId,
                check.AccountId,
                ct);

        if (!check.IsValid)
            return await FailAsync(LoginOutcome.InvalidCredentials, "invalid_credentials", correlationId, null, ct);

        if (check.Status != AccountStatus.Active)
            return await FailAsync(
                LoginOutcome.InvalidCredentials,
                check.Status == AccountStatus.Blocked ? "account_blocked" : "account_archived",
                correlationId,
                check.AccountId,
                ct,
                actorConfirmed: true);

        // MFA (IDN-06) is not implemented yet: until an MFA policy exists every login is Active.
        IssuedSession? session;
        try
        {
            session = await sessions.CreateAsync(check.AccountId, AuthenticationLevel.Active, ct);
        }
        catch (AccountNotActiveException)
        {
            // The account was blocked or archived after the status check above (IDN-03, SEC-03): same answer as a
            // login that was refused up front, and no session exists that a later reactivation could revive.
            return await FailAsync(
                LoginOutcome.InvalidCredentials,
                "account_inactive",
                correlationId,
                check.AccountId,
                ct,
                actorConfirmed: true);
        }

        if (session is null)
            return await FailAsync(
                LoginOutcome.Unavailable,
                "session_unavailable",
                correlationId,
                check.AccountId,
                ct,
                actorConfirmed: true);

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
        CancellationToken cancellationToken,
        bool actorConfirmed = false)
    {
        await audit.LoginFailedAsync(
            reasonCode,
            correlationId,
            confirmedUserId,
            cancellationToken,
            actorConfirmed);
        return new LoginResult(outcome);
    }
}
