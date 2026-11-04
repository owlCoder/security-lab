using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;

namespace Platform.Api.Services.Identity;

public enum LoginOutcome
{
    Success,
    InvalidCredentials,
    AccountBlocked,
    AccountArchived,
    Unavailable
}

public sealed record LoginResult(LoginOutcome Outcome, string? AccessToken = null);

public sealed class LoginAccount(
    ICredentialAuthenticator credentials,
    IAccountRepository accounts,
    ISessionIssuer sessions)
{
    public LoginResult Execute(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return new LoginResult(LoginOutcome.InvalidCredentials);

        var verification = credentials.Authenticate(email.Trim(), password);
        if (verification.Outcome == CredentialAuthenticationOutcome.Unavailable)
            return new LoginResult(LoginOutcome.Unavailable);

        if (verification.Outcome != CredentialAuthenticationOutcome.Valid || verification.AccountId is not Guid accountId)
            return new LoginResult(LoginOutcome.InvalidCredentials);

        // Status is checked only after valid credentials, so the endpoint does not
        // disclose account state to callers who do not know the password.
        var account = accounts.Find(accountId);
        if (account is null)
            return new LoginResult(LoginOutcome.InvalidCredentials);

        if (account.Status == AccountStatus.Blocked)
            return new LoginResult(LoginOutcome.AccountBlocked);

        if (account.Status == AccountStatus.Archived)
            return new LoginResult(LoginOutcome.AccountArchived);

        if (account.Status != AccountStatus.Active)
            return new LoginResult(LoginOutcome.Unavailable);

        var session = sessions.Issue(accountId);
        if (!session.Succeeded || string.IsNullOrWhiteSpace(session.AccessToken))
            return new LoginResult(LoginOutcome.Unavailable);

        return new LoginResult(LoginOutcome.Success, session.AccessToken);
    }
}
