namespace Platform.Api.Security.RevisionAuthorization;

public static class RevisionActions
{
    public const string Entry = "revision.entry";
    public const string Create = "revision.create";
    public const string Read = "revision.read";
    public const string Mine = "revision.mine";
    public const string PendingReviews = "revision.pending.reviews";

    public const string Submit = "revision.submit";
    public const string Approve = "revision.approve";
    public const string Return = "revision.return";

    public const string UnlockAccount = "account.unlock";

    public static bool IsCourseAction(string action) => action is
        Create or Read or Mine or PendingReviews or Submit or Approve or Return;

    public static bool IsKnown(string action) => IsCourseAction(action) || action is Entry or UnlockAccount;
}
