using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;

namespace Platform.Api.Services.Identity;

public sealed class ChangeAccountStatus(
    IIdentityContext identity,
    IAccessDecision access,
    IAccountRepository accounts,
    ISessionManager sessions,
    IdentityAuditProducer audit)
{
    public async Task<ChangeStatusResult> ExecuteAsync(
        Guid targetId,
        string? requestedStatus,
        string? reason,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _ = reason; // Free text is deliberately never copied into AUD-01 records.
        var caller = identity.Current;
        if (caller is null || !caller.IsAuthenticated || caller.UserId is not Guid actorId)
            return await RejectAsync(ChangeStatusOutcome.Unauthenticated, null, targetId,
                AuditOutcome.Denied, "unauthenticated", correlationId, cancellationToken);

        if (!access.CanChangeAccountStatus(caller))
            return await RejectAsync(ChangeStatusOutcome.Forbidden, actorId, targetId,
                AuditOutcome.Denied, "forbidden", correlationId, cancellationToken);

        if (!TryParseStatus(requestedStatus, out var newStatus))
            return await RejectAsync(ChangeStatusOutcome.InvalidStatus, actorId, targetId,
                AuditOutcome.Failure, "invalid_status", correlationId, cancellationToken);

        var account = accounts.Find(targetId);
        if (account is null)
            return await RejectAsync(ChangeStatusOutcome.NotFound, actorId, targetId,
                AuditOutcome.Failure, "not_found", correlationId, cancellationToken);

        if (actorId == targetId)
            return await RejectAsync(ChangeStatusOutcome.SelfChangeNotAllowed, actorId, targetId,
                AuditOutcome.Failure, "self_change_not_allowed", correlationId, cancellationToken);

        if (account.Status == newStatus)
            return await RejectAsync(ChangeStatusOutcome.InvalidTransition, actorId, targetId,
                AuditOutcome.Failure, "invalid_transition", correlationId, cancellationToken);

        var oldStatus = account.Status;
        account.Status = newStatus;
        accounts.Save(account);

        var auditResult = await audit.AccountStatusChangedAsync(
            actorId, targetId, oldStatus, newStatus, correlationId,
            cancellationToken: CancellationToken.None);
        if (!auditResult.IsRecorded)
        {
            // S-12 demo fail-closed compensation. S-16 replaces this with one
            // PostgreSQL transaction and verifies state from a new DB session.
            account.Status = oldStatus;
            accounts.Save(account);
            return new ChangeStatusResult(ChangeStatusOutcome.Unavailable);
        }

        if (newStatus is AccountStatus.Blocked or AccountStatus.Archived)
            await sessions.RevokeAllForUser(targetId, "status_changed", cancellationToken);

        return new ChangeStatusResult(ChangeStatusOutcome.Success, newStatus);
    }

    private static bool TryParseStatus(string? raw, out AccountStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var value = raw.Trim();
        if (!value.All(char.IsLetter)) return false;
        return Enum.TryParse(value, true, out status) && Enum.IsDefined(status);
    }

    private async Task<ChangeStatusResult> RejectAsync(
        ChangeStatusOutcome outcome,
        Guid? actorId,
        Guid targetId,
        AuditOutcome auditOutcome,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await audit.AccountStatusRejectedAsync(
            targetId, auditOutcome, reasonCode, correlationId, actorId, cancellationToken);
        return new ChangeStatusResult(outcome);
    }
}
