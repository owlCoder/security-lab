using Platform.Api.Domain.Authentication;
using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Api.Services.Identity;

public enum LoginOutcome
{
    Success,
    InvalidRequest,
    InvalidCredentials,
    Unavailable
}

public sealed record LoginResult(LoginOutcome Outcome, Guid? UserId = null, IssuedSession? Session = null);

/// <summary>
/// US-04: e-mail + password login (DFD process P1). Unknown e-mail, wrong password, missing hash and
/// blocked/archived accounts all return the same InvalidCredentials; only the audit event tells them apart.
/// </summary>
public sealed class LoginAccount(
    ICredentialVerifier credentials,
    ISessionCreator sessions,
    IAuditWriter audit,
    TimeProvider clock)
{
    private const string AuditAction = "auth.login";

    public async Task<LoginResult> ExecuteAsync(
        string? email, string? password, string correlationId, CancellationToken ct = default)
    {
        if (!IsAcceptable(email, password))
            return Fail(LoginOutcome.InvalidRequest, null, correlationId, "failure", "malformed request");

        // IDN-05 (attempt limiting) is not part of this story. It would be called here, before the check.
        var check = await credentials.VerifyAsync(email!, password!, ct);
        if (!check.IsValid)
            return Fail(LoginOutcome.InvalidCredentials, null, correlationId, "failure", "invalid credentials");

        if (check.Status != AccountStatus.Active)
            return Fail(LoginOutcome.InvalidCredentials, check.AccountId, correlationId, "denied", $"account is {check.Status}");

        // MFA (IDN-06) is not implemented yet: until an MFA policy exists every login is Active.
        var session = await sessions.CreateAsync(check.AccountId, AuthenticationLevel.Active, ct);
        if (session is null)
            return Fail(LoginOutcome.Unavailable, check.AccountId, correlationId, "failure", "session store unavailable");

        Write(check.AccountId, correlationId, "success", null);
        return new LoginResult(LoginOutcome.Success, check.AccountId, session);
    }

    private static bool IsAcceptable(string? email, string? password) =>
        !string.IsNullOrWhiteSpace(email) && email.Length <= CredentialVerifier.MaxEmailLength &&
        !string.IsNullOrEmpty(password) && password.Length <= CredentialVerifier.MaxPasswordLength;

    private LoginResult Fail(LoginOutcome outcome, Guid? actorId, string correlationId, string auditOutcome, string detail)
    {
        Write(actorId, correlationId, auditOutcome, detail);
        return new LoginResult(outcome);
    }

    // Never put the e-mail, password, hash or token in an audit event (AUD-03).
    private void Write(Guid? actorId, string correlationId, string outcome, string? detail) =>
        audit.Write(new AuditEvent(clock.GetUtcNow(), actorId, AuditAction, actorId?.ToString() ?? "-", outcome, correlationId, detail));
}
