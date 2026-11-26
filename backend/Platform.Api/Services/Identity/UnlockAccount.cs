using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Audit.Contracts;

namespace Platform.Api.Services.Identity;

/// <summary>
/// IDN-05: an administrator clears the lockout of one account. A successful unlock is one transaction: the
/// cleared counter and lockout end, and the Audit record. If either fails nothing is stored.
/// The status and the sessions of the account are not touched. Refusals are recorded in Audit too,
/// but never change anything. Same structure as <see cref="ChangeAccountStatus"/> (IDN-03).
/// </summary>
public sealed partial class UnlockAccount(
    IIdentityContext identity,
    IAccessDecision access,
    IAccountStatusStore store,
    ITransactionalAuditWriter audit,
    ICorrelationContext correlation,
    TimeProvider clock,
    ILogger<UnlockAccount> logger)
{
    public async Task<UnlockAccountOutcome> ExecuteAsync(Guid targetId, CancellationToken ct = default)
    {
        var correlationId = ResolveCorrelationId();

        var caller = identity.Current;
        if (caller is null || !caller.IsAuthenticated || caller.UserId is not { } actorId)
            return await RefuseAsync(UnlockAccountOutcome.Unauthenticated, AuditActor.Anonymous,
                AuditOutcome.Denied, targetId, correlationId, "unauthenticated", ct);

        var actor = AuditActor.ForUser(actorId);

        if (!access.CanUnlockAccount(caller))
            return await RefuseAsync(UnlockAccountOutcome.Forbidden, actor,
                AuditOutcome.Denied, targetId, correlationId, "not_administrator", ct);

        // An administrator under a guessing attack must not reopen their own guess budget from a session.
        if (actorId == targetId)
            return await RefuseAsync(UnlockAccountOutcome.SelfChangeNotAllowed, actor,
                AuditOutcome.Failure, targetId, correlationId, "self_change", ct);

        UnlockAccountOutcome refusal;
        string reasonCode;
        try
        {
            await using var work = await store.BeginAsync(ct);

            var account = await work.FindForUpdateAsync(targetId, ct);
            if (account is null)
            {
                (refusal, reasonCode) = (UnlockAccountOutcome.NotFound, "account_not_found");
            }
            else if (await work.ClearLockoutAsync(account, clock.GetUtcNow(), ct) is not { } clearedEnd)
            {
                (refusal, reasonCode) = (UnlockAccountOutcome.NotLocked, "not_locked");
            }
            else
            {
                var recorded = await audit.WriteAsync(
                    new AuditEventDraft(
                        AuditEventTypes.AccountUnlocked,
                        AuditOutcome.Success,
                        Resource: AccountRef(targetId),
                        Details: new Dictionary<string, string> { ["lockoutEnd"] = UtcTimestamp(clearedEnd) }),
                    new AuditContext(actor, correlationId),
                    work.Transaction, ct);

                // No Audit record, no unlock: leaving the scope without a commit rolls everything back.
                if (!recorded.IsRecorded)
                {
                    logger.LogError("Account unlock was rolled back: Audit write ended as {Status}.", recorded.Status);
                    return UnlockAccountOutcome.Unavailable;
                }

                await work.CommitAsync(ct);
                return UnlockAccountOutcome.Success;
            }
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogError(ex, "Account unlock was rolled back: the database failed.");
            return UnlockAccountOutcome.Unavailable;
        }

        // The transaction above is closed here, so the refusal gets its own short one.
        return await RefuseAsync(refusal, actor, AuditOutcome.Failure, targetId, correlationId, reasonCode, ct);
    }

    /// <summary>Records a refusal. A refusal changes nothing, so a failed Audit write does not change the answer.</summary>
    private async Task<UnlockAccountOutcome> RefuseAsync(
        UnlockAccountOutcome outcome, AuditActor actor, AuditOutcome auditOutcome,
        Guid targetId, string correlationId, string reasonCode, CancellationToken ct)
    {
        try
        {
            await using var work = await store.BeginAsync(ct);
            var recorded = await audit.WriteAsync(
                new AuditEventDraft(
                    AuditEventTypes.AccountUnlocked,
                    auditOutcome,
                    Resource: AccountRef(targetId),
                    Details: new Dictionary<string, string> { ["reasonCode"] = reasonCode }),
                new AuditContext(actor, correlationId),
                work.Transaction, ct);

            if (recorded.IsRecorded)
                await work.CommitAsync(ct);
            else
                logger.LogError("A refused account unlock ({Outcome}) was not recorded: {Status}.", outcome, recorded.Status);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogError(ex, "A refused account unlock ({Outcome}) was not recorded.", outcome);
        }

        return outcome;
    }

    private string ResolveCorrelationId() =>
        correlation.CorrelationId is { } id && CorrelationIdPattern().IsMatch(id)
            ? id
            : Guid.NewGuid().ToString("N");

    private static AuditResourceRef AccountRef(Guid accountId) =>
        new(AuditResourceTypes.Account, accountId.ToString("D"));

    // The Audit contract accepts an ISO 8601 UTC timestamp ending with Z and up to 7 fractional digits
    // (AuditDetailFormat.UtcTimestamp). FFFFFFF keeps the exact value; a zero fraction drops the '.' as well.
    private static string UtcTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}
