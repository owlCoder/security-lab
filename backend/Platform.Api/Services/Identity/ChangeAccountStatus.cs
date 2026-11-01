using Platform.Api.Domain.Identity;

namespace Platform.Api.Services.Identity;

public enum ChangeStatusOutcome
{
    Success,
    Unauthenticated,
    Forbidden,
    NotFound,
    InvalidStatus,
    InvalidTransition,
    SelfChangeNotAllowed
}

public sealed record ChangeStatusResult(ChangeStatusOutcome Outcome, AccountStatus? NewStatus = null);

public sealed class ChangeAccountStatus(
    IIdentityContext identity,
    IAccessDecision access,
    IAccountRepository accounts,
    ISessionManager sessions,
    IAuditWriter audit,
    TimeProvider clock)
{
    private const string AuditAction = "account.status.change";

    public ChangeStatusResult Execute(Guid targetId, string? requestedStatus, string? reason, string correlationId)
    {
        var caller = identity.Current;
        if (caller is null || !caller.IsAuthenticated)
            return Reject(ChangeStatusOutcome.Unauthenticated, null, targetId, correlationId, "denied", "no authenticated caller");

        if (!access.CanChangeAccountStatus(caller))
            return Reject(ChangeStatusOutcome.Forbidden, caller.UserId, targetId, correlationId, "denied", "caller is not an administrator");

        if (!TryParseStatus(requestedStatus, out var newStatus))
            return Reject(ChangeStatusOutcome.InvalidStatus, caller.UserId, targetId, correlationId, "failure", "invalid status value");

        var account = accounts.Find(targetId);
        if (account is null)
            return Reject(ChangeStatusOutcome.NotFound, caller.UserId, targetId, correlationId, "failure", "account not found");

        if (caller.UserId == targetId)
            return Reject(ChangeStatusOutcome.SelfChangeNotAllowed, caller.UserId, targetId, correlationId, "failure", "administrator cannot change own status");

        if (account.Status == newStatus)
            return Reject(ChangeStatusOutcome.InvalidTransition, caller.UserId, targetId, correlationId, "failure", "status is already " + newStatus);

        var oldStatus = account.Status;
        account.Status = newStatus;
        accounts.Save(account);

        // Blokiran ili arhiviran nalog gubi sve aktivne sesije odmah (spec IDN-03).
        if (newStatus is AccountStatus.Blocked or AccountStatus.Archived)
            sessions.RevokeAllForUser(targetId, "");

        var cleanReason = CleanReason(reason);
        var detail = $"{oldStatus}->{newStatus}" + (cleanReason is null ? "" : $" reason={cleanReason}");
        Write(caller.UserId, targetId, correlationId, "success", detail);

        return new ChangeStatusResult(ChangeStatusOutcome.Success, newStatus);
    }

    /// Prihvata samo imena ACTIVE / BLOCKED / ARCHIVED (bilo koja veličina slova).
    /// Brojevi se odbijaju, jer Enum.TryParse inače prihvata i "1" ili "99".
    private static bool TryParseStatus(string? raw, out AccountStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var value = raw.Trim();
        if (!value.All(char.IsLetter)) return false;

        return Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);
    }

    /// Razlog je opcioni. Skidamo kontrolne karaktere (novi red) da niko ne ubaci lažnu liniju u audit log.
    private static string? CleanReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;

        var cleaned = new string(reason.Where(c => !char.IsControl(c)).Take(200).ToArray()).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }

    private ChangeStatusResult Reject(
        ChangeStatusOutcome outcome, Guid? actorId, Guid targetId, string correlationId, string auditOutcome, string detail)
    {
        Write(actorId, targetId, correlationId, auditOutcome, detail);
        return new ChangeStatusResult(outcome);
    }

    private void Write(Guid? actorId, Guid targetId, string correlationId, string outcome, string detail) =>
        audit.Write(new AuditEvent(clock.GetUtcNow(), actorId, AuditAction, targetId.ToString(), outcome, correlationId, detail));
}