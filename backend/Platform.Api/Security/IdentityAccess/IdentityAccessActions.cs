using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.LabLocalization;
using Platform.LabVisibility.Contracts;
using Platform.Identity.Services;
using Platform.Api.Security.RevisionAuthorization;

namespace Platform.Api.Security.IdentityAccess;

/// <summary>
/// ACC-05 actions for Identity authentication and account status routes.
/// </summary>
public static class IdentityAccessActions
{
    public const string Login = "identity.login";
    public const string Logout = "identity.logout";
    public const string SessionRead = "identity.session.read";

    /// <summary>IDN-07: the caller lists their own active sessions (<c>GET /api/auth/sessions</c>).</summary>
    public const string SessionList = "identity.session.list";

    /// <summary>IDN-07: the caller revokes one of their own sessions (<c>DELETE /api/auth/sessions/{handle}</c>).</summary>
    public const string SessionRevoke = "identity.session.revoke";

    /// <summary>
    /// IDN-03: central catalog action (System scope, privileged). A constant, because attributes need one;
    /// it must equal <c>AccessActions.AccountStatusChange.Id</c>, otherwise policy registration fails at startup.
    /// </summary>
    public const string AccountStatusChange = "account.status.change";

    /// <summary>
    /// IDN-05: an administrator unlocks a locked account (<c>POST /api/admin/accounts/{id}/unlock</c>).
    /// This is the single action id agreed with REV-05 (#85), which registers it (System scope, privileged) with
    /// <c>RevisionActionPolicy</c>; it is not registered a second time here.
    /// </summary>
    public const string AccountUnlock = RevisionActions.UnlockAccount;

    /// <summary>
    /// Routes of the Identity module itself (a subset of <see cref="SessionBound"/>). IDN-07 promises these a stable
    /// <c>401 session_invalid</c> (cookie cleared, <c>no-store</c>) for a missing, unknown, revoked or expired session.
    /// </summary>
    public static IReadOnlySet<string> IdentityOwned { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            AccountStatusChange,
            AccountUnlock,
            SessionRead,
            SessionList,
            SessionRevoke,
        };

    /// <summary>Actions whose subject is the caller identified by the server-side session cookie.</summary>
    public static IReadOnlySet<string> SessionBound { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            AccountStatusChange,
            AccountUnlock,
            CreateAccountPolicy.ActionId,
            SessionRead,
            SessionList,
            SessionRevoke,
            // #185: /student-view i /preview (tim 6) dobijaju subjekat iz prave sesije, ne iz razvojnog zaglavlja.
            LabVisibilityActions.Entry,
            // LAB-07: sadržaj i prevodi laboratorije.
            LabLocalizationActions.Entry,
            CatalogReadPolicy.ActionId,
        };
}
