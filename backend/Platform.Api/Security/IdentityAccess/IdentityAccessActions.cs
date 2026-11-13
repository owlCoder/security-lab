namespace Platform.Api.Security.IdentityAccess;

/// <summary>
/// ACC-05 actions of the Identity routes owned by Team 2: IDN-03 (account status).
/// </summary>
public static class IdentityAccessActions
{
    /// <summary>
    /// IDN-03: central catalog action (System scope, privileged). A constant, because attributes need one;
    /// it must equal <c>AccessActions.AccountStatusChange.Id</c>, otherwise policy registration fails at startup.
    /// </summary>
    public const string AccountStatusChange = "account.status.change";

    /// <summary>Actions whose subject is the caller identified by the server-side session cookie.</summary>
    public static IReadOnlySet<string> SessionBound { get; } =
        new HashSet<string>(StringComparer.Ordinal) { AccountStatusChange };
}
