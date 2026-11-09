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
public sealed class LoginService(
    ICredentialVerifier credentials,
    ISessionCreator sessions,
    IAuditWriter audit,
    TimeProvider clock) : ILoginService
{
    private const string AuditAction = "auth.login";

    public async Task<LoginResult> ExecuteAsync(
        string? email, string? password, string correlationId, CancellationToken ct = default)
    {
        if (!IsAcceptable(email, password))
            return Fail(LoginOutcome.InvalidRequest, null, correlationId, "failure", "malformed request");

        var check = await credentials.VerifyAsync(email!, password!, ct);

        // IDN-05: a locked account gets the same answer as a wrong password; only the audit event differs.
        // The caller is not proven to own the account, so it is the target and not the actor.
        if (check.IsLockedOut)
            return Fail(LoginOutcome.InvalidCredentials, null, correlationId, "failure", "account locked", check.AccountId);

        if (!check.IsValid)
            return Fail(LoginOutcome.InvalidCredentials, null, correlationId, "failure", "invalid credentials");

        if (check.Status != AccountStatus.Active)
            return Fail(LoginOutcome.InvalidCredentials, check.AccountId, correlationId, "denied", $"account is {check.Status}");

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
            return Fail(LoginOutcome.InvalidCredentials, check.AccountId, correlationId, "denied", "account is no longer active");
        }

        if (session is null)
            return Fail(LoginOutcome.Unavailable, check.AccountId, correlationId, "failure", "session store unavailable");

        Write(check.AccountId, correlationId, "success", null);
        return new LoginResult(LoginOutcome.Success, check.AccountId, session);
    }

    private static bool IsAcceptable(string? email, string? password) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= CredentialVerifier.MaxEmailLength &&
        !string.IsNullOrEmpty(password) && password.Length <= CredentialVerifier.MaxPasswordLength;

    private LoginResult Fail(
        LoginOutcome outcome, Guid? actorId, string correlationId, string auditOutcome, string detail, Guid? targetId = null)
    {
        Write(actorId, correlationId, auditOutcome, detail, targetId);
        return new LoginResult(outcome);
    }

    // Never put the e-mail, password, hash or token in an audit event (AUD-03).
    private void Write(Guid? actorId, string correlationId, string outcome, string? detail, Guid? targetId = null) =>
        audit.Write(new AuditEvent(
            clock.GetUtcNow(), actorId, AuditAction, (targetId ?? actorId)?.ToString() ?? "-", outcome, correlationId, detail));
}
