using System.Globalization;
using System.Text.RegularExpressions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Audit.Contracts;

namespace Platform.Api.Services.Identity;

/// <summary>
/// US-32: an administrator removes the second factor of another account. Order: authenticated, then administrator
/// (second line after the central ACC-05 check), then a reason code from the fixed list, then the MFA service (which also revokes all
/// sessions of the target). One audit event is written per attempt, refusals included, and it never contains a
/// password, token, TOTP secret or code. Same structure as <see cref="UnlockAccount"/> (IDN-05).
/// </summary>
public sealed partial class ResetMfa(
    IIdentityContext identity,
    IAccessDecision access,
    IMfaResetService service,
    ITrustedAuditWriter audit,
    ICorrelationContext correlation,
    ILogger<ResetMfa> logger)
{
    // Needs AuditEventTypes.AccountMfaReset and its schema in AuditEventCatalog (Audit team, see docs/US-32_WIRING.md).

    public async Task<ResetMfaResult> ExecuteAsync(Guid targetId, string? reason, CancellationToken ct = default)
    {
        var correlationId = ResolveCorrelationId();

        var caller = identity.Current;
        if (caller is null || !caller.IsAuthenticated || caller.UserId is not { } actorId)
            return await RefuseAsync(ResetMfaOutcome.Unauthenticated, AuditActor.Anonymous,
                AuditOutcome.Denied, targetId, correlationId, "unauthenticated", ct);

        var actor = AuditActor.ForUser(actorId);

        if (!access.CanResetMfa(caller))
            return await RefuseAsync(ResetMfaOutcome.Forbidden, actor,
                AuditOutcome.Denied, targetId, correlationId, "not_administrator", ct);

        if (!MfaResetReasons.IsValid(reason))
            return await RefuseAsync(ResetMfaOutcome.InvalidReason, actor,
                AuditOutcome.Failure, targetId, correlationId, "invalid_reason", ct);

        MfaResetResult result;
        try
        {
            result = await service.ResetAsync(actorId, targetId, reason!, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MFA reset failed unexpectedly; correlation {CorrelationId}", correlationId);
            return await RefuseAsync(ResetMfaOutcome.Unavailable, actor,
                AuditOutcome.Failure, targetId, correlationId, "unavailable", ct);
        }

        switch (result.Outcome)
        {
            case MfaResetOutcome.Success:
                await WriteAuditAsync(AuditOutcome.Success, actor, targetId, correlationId,
                    new Dictionary<string, string>
                    {
                        // Audit details accept codes and counts only, never free text.
                        ["resetReason"] = reason!,
                        ["mfaWasEnabled"] = result.MfaWasEnabled ? "true" : "false",
                        ["sessionsRevoked"] = result.SessionsRevoked.ToString(CultureInfo.InvariantCulture)
                    }, ct);
                return new ResetMfaResult(ResetMfaOutcome.Success, result.SessionsRevoked, result.MfaWasEnabled);

            case MfaResetOutcome.NotAuthorized:
                return await RefuseAsync(ResetMfaOutcome.Forbidden, actor,
                    AuditOutcome.Denied, targetId, correlationId, "not_administrator", ct);
            case MfaResetOutcome.SelfResetNotAllowed:
                return await RefuseAsync(ResetMfaOutcome.SelfChangeNotAllowed, actor,
                    AuditOutcome.Failure, targetId, correlationId, "self_change", ct);
            case MfaResetOutcome.TargetNotFound:
                return await RefuseAsync(ResetMfaOutcome.NotFound, actor,
                    AuditOutcome.Failure, targetId, correlationId, "account_not_found", ct);
            case MfaResetOutcome.InvalidReason:
                return await RefuseAsync(ResetMfaOutcome.InvalidReason, actor,
                    AuditOutcome.Failure, targetId, correlationId, "invalid_reason", ct);
            default:
                return await RefuseAsync(ResetMfaOutcome.Unavailable, actor,
                    AuditOutcome.Failure, targetId, correlationId, "unavailable", ct);
        }
    }

    private async Task<ResetMfaResult> RefuseAsync(
        ResetMfaOutcome outcome, AuditActor actor, AuditOutcome auditOutcome,
        Guid targetId, string correlationId, string reasonCode, CancellationToken ct)
    {
        await WriteAuditAsync(auditOutcome, actor, targetId, correlationId,
            new Dictionary<string, string> { ["reasonCode"] = reasonCode }, ct);
        return new ResetMfaResult(outcome);
    }

    /// <summary>
    /// A failed audit write is logged and does not change the answer: the reset itself is not in the audit
    /// transaction (the MFA store belongs to the MFA service). Known limitation, see docs/contracts/IMfaResetService.md.
    /// </summary>
    private async Task WriteAuditAsync(
        AuditOutcome outcome, AuditActor actor, Guid targetId, string correlationId,
        IReadOnlyDictionary<string, string> details, CancellationToken ct)
    {
        try
        {
            var recorded = await audit.WriteAsync(
                new AuditEventDraft(
                    AuditEventTypes.AccountMfaReset,
                    outcome,
                    Resource: new AuditResourceRef(AuditResourceTypes.Account, targetId.ToString("D")),
                    Details: details),
                new AuditContext(actor, correlationId),
                ct);

            if (!recorded.IsRecorded)
                logger.LogError("MFA reset audit was not recorded: {Status}; correlation {CorrelationId}",
                    recorded.Status, correlationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MFA reset audit writer failed; correlation {CorrelationId}", correlationId);
        }
    }

    private string ResolveCorrelationId() =>
        correlation.CorrelationId is { } id && CorrelationIdPattern().IsMatch(id)
            ? id
            : Guid.NewGuid().ToString("N");

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();
}
