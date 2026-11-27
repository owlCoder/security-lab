using System.Data.Common;
using Platform.Audit.Contracts;
using Platform.Identity.Enums;
using Microsoft.Extensions.Logging;

namespace Platform.Api.Services.Identity;

/// <summary>
/// The single mapping boundary from Identity outcomes to the shared Audit catalog.
/// It accepts only server-confirmed identifiers and stable reason codes; credentials,
/// tokens, typed e-mail addresses and free text have no place in this API.
/// </summary>
public sealed class IdentityAuditProducer(
    ITrustedAuditWriter writer,
    ITransactionalAuditWriter transactionalWriter,
    ILogger<IdentityAuditProducer> logger)
{
    public ValueTask<AuditWriteResult> LoginThrottledAsync(
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(AuditEventTypes.AuthLoginThrottled, AuditOutcome.Denied),
            AnonymousContext(correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> LoginSucceededAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthLogin,
                AuditOutcome.Success,
                Resource: Account(userId)),
            UserContext(userId, correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> LoginFailedAsync(
        string reasonCode,
        string correlationId,
        Guid? confirmedUserId = null,
        CancellationToken cancellationToken = default,
        bool actorConfirmed = false) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthLogin,
                AuditOutcome.Failure,
                Resource: confirmedUserId is Guid userId ? Account(userId) : null,
                Details: Details(("reasonCode", reasonCode))),
            actorConfirmed && confirmedUserId is Guid id
                ? UserContext(id, correlationId)
                : AnonymousContext(correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> LogoutSucceededAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(AuditEventTypes.AuthLogout, AuditOutcome.Success),
            UserContext(userId, correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> MfaEnrollmentStartedAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthMfaEnrollmentStarted,
                AuditOutcome.Success,
                Resource: Account(userId)),
            UserContext(userId, correlationId),
            cancellationToken);

    /// <summary>
    /// IDN-06 (US-17): MFA was enabled. Written in the caller's transaction; the caller commits the change only
    /// when this returns <see cref="AuditWriteResult.IsRecorded"/>.
    /// </summary>
    public ValueTask<AuditWriteResult> MfaEnabledAsync(
        Guid userId,
        string correlationId,
        DbTransaction transaction,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthMfaEnabled,
                AuditOutcome.Success,
                Resource: Account(userId)),
            UserContext(userId, correlationId),
            transaction,
            cancellationToken);

    public ValueTask<AuditWriteResult> MfaEnableFailedAsync(
        Guid userId,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthMfaEnabled,
                AuditOutcome.Failure,
                Resource: Account(userId),
                Details: Details(("reasonCode", reasonCode))),
            UserContext(userId, correlationId),
            cancellationToken);

    /// <summary>
    /// IDN-06 (US-18): the second factor was verified and the session is being promoted. Written in the caller's
    /// transaction; the caller commits only when this returns <see cref="AuditWriteResult.IsRecorded"/>.
    /// </summary>
    public ValueTask<AuditWriteResult> MfaVerifiedAsync(
        Guid userId,
        string correlationId,
        DbTransaction transaction,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthMfaVerified,
                AuditOutcome.Success,
                Resource: Account(userId)),
            UserContext(userId, correlationId),
            transaction,
            cancellationToken);

    public ValueTask<AuditWriteResult> MfaVerifyFailedAsync(
        Guid userId,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthMfaVerified,
                AuditOutcome.Failure,
                Resource: Account(userId),
                Details: Details(("reasonCode", reasonCode))),
            UserContext(userId, correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> MfaVerifyDeniedByAttemptLimitAsync(
        Guid userId,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthMfaVerified,
                AuditOutcome.Denied,
                Resource: Account(userId),
                Details: Details(("reasonCode", "attempt_limit"))),
            UserContext(userId, correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> SessionRevokedAsync(
        Guid userId,
        Guid revokedHandle,
        string revocationCause,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthSessionRevoked,
                AuditOutcome.Success,
                Resource: Account(userId),
                Details: Details(
                    ("revocationCause", revocationCause),
                    ("revokedHandle", revokedHandle.ToString("D")))),
            UserContext(userId, correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> SessionRevokeFailedAsync(
        Guid userId,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AuthSessionRevoked,
                AuditOutcome.Failure,
                Resource: Account(userId),
                Details: Details(("reasonCode", reasonCode))),
            UserContext(userId, correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> AccountStatusChangedAsync(
        Guid actorId,
        Guid targetId,
        AccountStatus oldStatus,
        AccountStatus newStatus,
        string correlationId,
        Guid? eventId = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AccountStatusChanged,
                AuditOutcome.Success,
                Resource: Account(targetId),
                Details: Details(
                    ("oldStatus", Status(oldStatus)),
                    ("newStatus", Status(newStatus))),
                EventId: eventId),
            UserContext(actorId, correlationId),
            cancellationToken);

    public ValueTask<AuditWriteResult> AccountStatusRejectedAsync(
        Guid targetId,
        AuditOutcome outcome,
        string reasonCode,
        string correlationId,
        Guid? actorId = null,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new AuditEventDraft(
                AuditEventTypes.AccountStatusChanged,
                outcome,
                Resource: Account(targetId),
                Details: Details(("reasonCode", reasonCode))),
            actorId is Guid id
                ? UserContext(id, correlationId)
                : AnonymousContext(correlationId),
            cancellationToken);

    private ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        CancellationToken cancellationToken) =>
        GuardAsync(draft, context, () => writer.WriteAsync(draft, context, cancellationToken), cancellationToken);

    private ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft,
        AuditContext context,
        DbTransaction transaction,
        CancellationToken cancellationToken) =>
        GuardAsync(
            draft,
            context,
            () => transactionalWriter.WriteAsync(draft, context, transaction, cancellationToken),
            cancellationToken);

    private async ValueTask<AuditWriteResult> GuardAsync(
        AuditEventDraft draft,
        AuditContext context,
        Func<ValueTask<AuditWriteResult>> write,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await write();
            if (!result.IsRecorded)
            {
                logger.LogError(
                    "Audit write failed for {EventType} with status {Status}; correlation {CorrelationId}",
                    draft.EventType,
                    result.Status,
                    context.CorrelationId);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogError(
                "Audit writer failed unexpectedly for {EventType}; correlation {CorrelationId}",
                draft.EventType,
                context.CorrelationId);
            return AuditWriteResult.Unavailable();
        }
    }

    private static AuditContext UserContext(Guid userId, string correlationId) =>
        new(AuditActor.ForUser(userId), correlationId);

    private static AuditContext AnonymousContext(string correlationId) =>
        new(AuditActor.Anonymous, correlationId);

    private static AuditResourceRef Account(Guid accountId) =>
        new(AuditResourceTypes.Account, accountId.ToString());

    private static IReadOnlyDictionary<string, string> Details(
        params (string Key, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static string Status(AccountStatus status) => status switch
    {
        AccountStatus.Active => "active",
        AccountStatus.Blocked => "blocked",
        AccountStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(nameof(status), "Unsupported account status.")
    };
}
