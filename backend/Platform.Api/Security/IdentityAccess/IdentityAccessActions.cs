namespace Platform.Api.Security.IdentityAccess;

/// <summary>
/// ACC-05 actions for Identity authentication and account status routes.
/// </summary>
public static class IdentityAccessActions
{
    public const string Login = "identity.login";
    public const string Logout = "identity.logout";
    public const string SessionRead = "identity.session.read";

    /// <summary>
    /// IDN-03: central catalog action (System scope, privileged). A constant, because attributes need one;
    /// it must equal <c>AccessActions.AccountStatusChange.Id</c>, otherwise policy registration fails at startup.
    /// </summary>
    public const string AccountStatusChange = "account.status.change";

    /// <summary>Actions whose subject is the caller identified by the server-side session cookie.</summary>
    public static IReadOnlySet<string> SessionBound { get; } =
        new HashSet<string>(StringComparer.Ordinal) { AccountStatusChange, SessionRead };
}
