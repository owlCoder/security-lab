using System.Diagnostics.CodeAnalysis;
using Platform.SessionSecurity.Contracts;
using Platform.SessionSecurity.Domain;

// IDN-11 TIM 09
namespace Platform.SessionSecurity.Application;

/// <summary>
/// IDN-11: replaces the current session with a new one after a login or a privilege change and
/// revokes the old one, so the previous session token is rejected (session fixation protection).
/// The new session keeps the chain and the absolute expiry of the old one, so a rotation never
/// extends the session lifetime. Every rotation attempt with a valid request is audited without
/// any token, hash or chain ID (AUD-01, AUD-03). When the audit of a successful rotation fails,
/// the whole chain is revoked and the rotation is reported as failed (AUD-10, fail-closed).
/// </summary>
public sealed class SessionRotationService(
    ISessionRotationStore store,
    ISessionTokenGenerator tokenGenerator,
    ISessionSecurityAuditWriter auditWriter,
    TimeProvider timeProvider) : IRotateSessionUseCase
{
    public async ValueTask<SessionRotationResult> RotateAsync(
        RotateSessionRequest? request,
        CancellationToken cancellationToken = default)
    {
        // Without a valid request there is no reliable actor or correlation ID to audit.
        if (!TryValidate(request, out var valid))
        {
            return SessionRotationResult.Rejected(SessionRotationCodes.InvalidRequest);
        }

        var now = timeProvider.GetUtcNow();
        var current = await store.FindAsync(
            tokenGenerator.ComputeHash(valid.CurrentToken), cancellationToken);
        if (current is null)
        {
            return await RejectAsync(valid, SessionRotationCodes.SessionNotFound, now, cancellationToken);
        }

        var rejectionCode = FindRejectionCode(current, valid, now);
        if (rejectionCode is not null)
        {
            return await RejectAsync(valid, rejectionCode, now, cancellationToken);
        }

        var issued = tokenGenerator.Generate();
        var replacement = new RotatableSession(
            current.ChainId,
            valid.SubjectId,
            issued.Hash,
            valid.NewPrivilegeLevel,
            now,
            current.AbsoluteExpiresAt);

        var status = await store.RotateAsync(current.TokenHash, replacement, now, cancellationToken);
        if (status != SessionRotationStoreStatus.Rotated)
        {
            return await RejectAsync(valid, ToRejectionCode(status), now, cancellationToken);
        }

        if (!await WriteAuditAsync(valid, SessionRotationCodes.Rotated, now, cancellationToken))
        {
            // AUD-10: the new token never reaches the client and the old one stays revoked.
            // The compensation must finish even when the request is cancelled.
            await store.RevokeChainAsync(current.ChainId, now, CancellationToken.None);
            return SessionRotationResult.Rejected(SessionRotationCodes.AuditFailed);
        }

        return SessionRotationResult.Rotated(issued.Value);
    }

    // The store checks the old session again inside its atomic operation, because it can be
    // rotated by a concurrent request or reach its absolute expiry after FindAsync.
    private static string ToRejectionCode(SessionRotationStoreStatus status) => status switch
    {
        SessionRotationStoreStatus.NotFound => SessionRotationCodes.SessionNotFound,
        SessionRotationStoreStatus.Expired => SessionRotationCodes.SessionExpired,
        _ => SessionRotationCodes.RotationConflict,
    };

    private static bool TryValidate(
        RotateSessionRequest? request,
        [NotNullWhen(true)] out ValidatedRequest? valid)
    {
        valid = null;
        if (request is null
            || string.IsNullOrWhiteSpace(request.CurrentToken)
            || string.IsNullOrWhiteSpace(request.SubjectId)
            || string.IsNullOrWhiteSpace(request.CorrelationId)
            || !Enum.IsDefined(request.Trigger)
            || !Enum.IsDefined(request.NewPrivilegeLevel)
            || request.NewPrivilegeLevel == SessionPrivilegeLevel.PreAuthentication)
        {
            return false;
        }

        valid = new ValidatedRequest(
            request.CurrentToken,
            request.SubjectId,
            request.Trigger,
            request.NewPrivilegeLevel,
            request.CorrelationId);
        return true;
    }

    private static string? FindRejectionCode(
        RotatableSession current,
        ValidatedRequest request,
        DateTimeOffset now)
    {
        if (current.IsRevoked)
        {
            return SessionRotationCodes.SessionRevoked;
        }

        if (current.IsExpiredAt(now))
        {
            return SessionRotationCodes.SessionExpired;
        }

        return BelongsToSubject(current, request) ? null : SessionRotationCodes.SubjectMismatch;
    }

    // A login may start from a pre-authentication session (no subject yet). Every other trigger
    // needs a session that already belongs to the same subject.
    private static bool BelongsToSubject(RotatableSession current, ValidatedRequest request)
    {
        if (current.SubjectId is null)
        {
            return request.Trigger == RotationTrigger.Login;
        }

        return string.Equals(current.SubjectId, request.SubjectId, StringComparison.Ordinal);
    }

    private async ValueTask<SessionRotationResult> RejectAsync(
        ValidatedRequest request,
        string code,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The result is a rejection whether or not this audit write succeeds.
        await WriteAuditAsync(request, code, now, cancellationToken);
        return SessionRotationResult.Rejected(code);
    }

    private ValueTask<bool> WriteAuditAsync(
        ValidatedRequest request,
        string outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var entry = new SessionSecurityAuditEntry(
            SessionSecurityAuditEvents.SessionRotated,
            request.SubjectId,
            request.Trigger,
            outcome,
            now,
            request.CorrelationId);

        return auditWriter.TryWriteAsync(entry, cancellationToken);
    }

    private sealed record ValidatedRequest(
        string CurrentToken,
        string SubjectId,
        RotationTrigger Trigger,
        SessionPrivilegeLevel NewPrivilegeLevel,
        string CorrelationId)
    {
        // AUD-03: never print the session token.
        public override string ToString() =>
            $"ValidatedRequest {{ Trigger = {Trigger}, NewPrivilegeLevel = {NewPrivilegeLevel} }}";
    }
}
