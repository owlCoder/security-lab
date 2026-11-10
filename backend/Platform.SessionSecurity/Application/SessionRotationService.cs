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
/// any token, hash or chain ID (AUD-01, AUD-03). The replacement is stored as pending activation
/// and activated only after the rotation was audited, so no failure, exception or cancellation
/// can leave an unaudited active session (AUD-10, fail-closed; see
/// <see cref="IRotateSessionUseCase"/>).
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
            current.AbsoluteExpiresAt,
            IsPendingActivation: true);

        var status = await store.RotateAsync(current.TokenHash, replacement, now, cancellationToken);
        if (status != SessionRotationStoreStatus.Rotated)
        {
            return await RejectAsync(valid, ToRejectionCode(status), now, cancellationToken);
        }

        return await AuditAndActivateAsync(valid, replacement, issued, now, cancellationToken);
    }

    // From here on the old session is revoked and the replacement is pending. Until ActivateAsync
    // succeeds no session of the chain is active, so every early exit is fail-closed by itself;
    // the chain revocation after a failure is cleanup only.
    private async ValueTask<SessionRotationResult> AuditAndActivateAsync(
        ValidatedRequest request,
        RotatableSession replacement,
        IssuedSessionToken issued,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string failureCode;
        try
        {
            if (!await TryWriteAuditAsync(request, SessionRotationCodes.Rotated, now, cancellationToken))
            {
                failureCode = SessionRotationCodes.AuditFailed;
            }
            else if (await store.ActivateAsync(replacement.TokenHash, now, cancellationToken))
            {
                return SessionRotationResult.Rotated(issued.Value);
            }
            else
            {
                // The chain was revoked or expired after the rotation: record that the rotation
                // did not complete, so the trail does not end with a success.
                failureCode = SessionRotationCodes.RotationConflict;
                await TryWriteAuditAsync(request, failureCode, now, cancellationToken);
            }
        }
        catch
        {
            // Cancellation or a store failure: the replacement was never activated. Clean up and
            // let the caller see the original exception.
            await TryRevokeChainAsync(replacement.ChainId, now);
            throw;
        }

        await TryRevokeChainAsync(replacement.ChainId, now);
        return SessionRotationResult.Rejected(failureCode);
    }

    // Cleanup only, independent of the caller's cancellation. Safety does not depend on it: the
    // replacement is still pending, so a failed revocation leaves no active session in the chain,
    // and the pending session ends at the absolute expiry.
    private async ValueTask TryRevokeChainAsync(Guid chainId, DateTimeOffset now)
    {
        try
        {
            await store.RevokeChainAsync(chainId, now, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Intentionally ignored, see the comment above. The rotation already reports a failure.
        }
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
        // A pending replacement was never handed out, so it is treated as unknown.
        if (current.IsPendingActivation)
        {
            return SessionRotationCodes.SessionNotFound;
        }

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
        await TryWriteAuditAsync(request, code, now, cancellationToken);
        return SessionRotationResult.Rejected(code);
    }

    // An audit adapter may report a failure by returning false or by throwing; both are treated
    // as a failed write. Cancellation is not a write failure and is propagated.
    private async ValueTask<bool> TryWriteAuditAsync(
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

        try
        {
            return await auditWriter.TryWriteAsync(entry, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
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
