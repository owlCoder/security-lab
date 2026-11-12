using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;

namespace Platform.Api.Services.Identity;

/// <summary>
/// IDN-03 / UC-05 / ADM-01. A successful change is one transaction: the new status, the revocation of all
/// sessions (blocked or archived) and the Audit record. If any step fails nothing is stored.
/// Refusals are recorded in Audit too, but never change anything.
/// </summary>
public sealed partial class ChangeAccountStatus(
    IIdentityContext identity,
    IAccessDecision access,
    IAccountStatusStore store,
    ITransactionalAuditWriter audit,
    ICorrelationContext correlation,
    ILogger<ChangeAccountStatus> logger)
{
    public async Task<ChangeStatusResult> ExecuteAsync(
        Guid targetId, string? requestedStatus, CancellationToken ct = default)
    {
        var correlationId = ResolveCorrelationId();

        var caller = identity.Current;
        if (caller is null || !caller.IsAuthenticated || caller.UserId is not { } actorId)
            return await RefuseAsync(ChangeStatusOutcome.Unauthenticated, AuditActor.Anonymous,
                AuditOutcome.Denied, targetId, correlationId, "unauthenticated", ct);

        var actor = AuditActor.ForUser(actorId);

        if (!access.CanChangeAccountStatus(caller))
            return await RefuseAsync(ChangeStatusOutcome.Forbidden, actor,
                AuditOutcome.Denied, targetId, correlationId, "not_administrator", ct);

        if (!TryParseStatus(requestedStatus, out var newStatus))
            return await RefuseAsync(ChangeStatusOutcome.InvalidStatus, actor,
                AuditOutcome.Failure, targetId, correlationId, "invalid_status", ct);

        if (actorId == targetId)
            return await RefuseAsync(ChangeStatusOutcome.SelfChangeNotAllowed, actor,
                AuditOutcome.Failure, targetId, correlationId, "self_change", ct);

        ChangeStatusOutcome refusal;
        string reasonCode;
        try
        {
            await using var work = await store.BeginAsync(ct);

            var account = await work.FindForUpdateAsync(targetId, ct);
            if (account is null)
            {
                (refusal, reasonCode) = (ChangeStatusOutcome.NotFound, "account_not_found");
            }
            else if (account.Status == newStatus)
            {
                (refusal, reasonCode) = (ChangeStatusOutcome.InvalidTransition, "invalid_transition");
            }
            else
            {
                var oldStatus = account.Status;
                await work.ApplyAsync(account, newStatus, ct);

                // The reason text is not recorded: the Audit contract allows no free text (AUD-03).
                var recorded = await audit.WriteAsync(
                    new AuditEventDraft(
                        AuditEventTypes.AccountStatusChanged,
                        AuditOutcome.Success,
                        Resource: AccountRef(targetId),
                        Details: new Dictionary<string, string>
                        {
                            ["oldStatus"] = StatusName(oldStatus),
                            ["newStatus"] = StatusName(newStatus)
                        }),
                    new AuditContext(actor, correlationId),
                    work.Transaction, ct);

                // No Audit record, no change: leaving the scope without a commit rolls everything back.
                if (!recorded.IsRecorded)
                {
                    logger.LogError("Account status change was rolled back: Audit write ended as {Status}.", recorded.Status);
                    return new ChangeStatusResult(ChangeStatusOutcome.Unavailable);
                }

                await work.CommitAsync(ct);
                return new ChangeStatusResult(ChangeStatusOutcome.Success, newStatus);
            }
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogError(ex, "Account status change was rolled back: the database failed.");
            return new ChangeStatusResult(ChangeStatusOutcome.Unavailable);
        }

        // The transaction above is closed here, so the refusal gets its own short one.
        return await RefuseAsync(refusal, actor, AuditOutcome.Failure, targetId, correlationId, reasonCode, ct);
    }

    /// <summary>Records a refusal. A refusal changes nothing, so a failed Audit write does not change the answer.</summary>
    private async Task<ChangeStatusResult> RefuseAsync(
        ChangeStatusOutcome outcome, AuditActor actor, AuditOutcome auditOutcome,
        Guid targetId, string correlationId, string reasonCode, CancellationToken ct)
    {
        try
        {
            await using var work = await store.BeginAsync(ct);
            var recorded = await audit.WriteAsync(
                new AuditEventDraft(
                    AuditEventTypes.AccountStatusChanged,
                    auditOutcome,
                    Resource: AccountRef(targetId),
                    Details: new Dictionary<string, string> { ["reasonCode"] = reasonCode }),
                new AuditContext(actor, correlationId),
                work.Transaction, ct);

            if (recorded.IsRecorded)
                await work.CommitAsync(ct);
            else
                logger.LogError("A refused account status change ({Outcome}) was not recorded: {Status}.", outcome, recorded.Status);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogError(ex, "A refused account status change ({Outcome}) was not recorded.", outcome);
        }

        return new ChangeStatusResult(outcome);
    }

    private string ResolveCorrelationId() =>
        correlation.CorrelationId is { } id && CorrelationIdPattern().IsMatch(id)
            ? id
            : Guid.NewGuid().ToString("N");

    private static AuditResourceRef AccountRef(Guid accountId) =>
        new(AuditResourceTypes.Account, accountId.ToString("D"));

    // The Audit contract names statuses in lower case: active, blocked, archived.
    private static string StatusName(AccountStatus status) => status.ToString().ToLowerInvariant();

    /// Prihvata samo imena ACTIVE / BLOCKED / ARCHIVED (bilo koja velicina slova).
    /// Brojevi se odbijaju, jer Enum.TryParse inace prihvata i "1" ili "99".
    private static bool TryParseStatus(string? raw, out AccountStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var value = raw.Trim();
        if (!value.All(char.IsLetter)) return false;

        return Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}
