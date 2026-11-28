using System.Collections.Concurrent;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;

namespace Platform.Identity.Tests.Helpers;

/// <summary>
/// In-memory fake of IMfaResetService with the same check order as the contract.
/// Session revocation is injected, so the fake does not depend on ISessionManager's shape.
/// </summary>
public sealed class FakeMfaResetService : IMfaResetService
{
    private sealed class Account
    {
        public bool IsAdministrator;
        public bool MfaEnabled;
    }

    private readonly ConcurrentDictionary<Guid, Account> _accounts = new();
    private readonly Func<Guid, CancellationToken, Task<int>> _revokeAllSessions;

    /// <param name="revokeAllSessions">Revokes all sessions of a user and returns how many were revoked.</param>
    public FakeMfaResetService(Func<Guid, CancellationToken, Task<int>> revokeAllSessions)
        => _revokeAllSessions = revokeAllSessions;

    public void AddUser(Guid id, bool isAdministrator, bool mfaEnabled) =>
        _accounts[id] = new Account { IsAdministrator = isAdministrator, MfaEnabled = mfaEnabled };

    public bool IsMfaEnabled(Guid id) => _accounts.TryGetValue(id, out var a) && a.MfaEnabled;

    public async Task<MfaResetResult> ResetAsync(Guid actorUserId, Guid targetUserId, string reason, CancellationToken ct = default)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("actorUserId is required", nameof(actorUserId));
        if (targetUserId == Guid.Empty) throw new ArgumentException("targetUserId is required", nameof(targetUserId));

        // 1. reason
        if (!MfaResetReasons.IsValid(reason))
            return new MfaResetResult(MfaResetOutcome.InvalidReason);

        // 2. authorization BEFORE any lookup of the target
        if (!_accounts.TryGetValue(actorUserId, out var actor) || !actor.IsAdministrator)
            return new MfaResetResult(MfaResetOutcome.NotAuthorized);

        // 3. no self reset (another administrator is allowed)
        if (actorUserId == targetUserId)
            return new MfaResetResult(MfaResetOutcome.SelfResetNotAllowed);

        // 4. target exists
        if (!_accounts.TryGetValue(targetUserId, out var target))
            return new MfaResetResult(MfaResetOutcome.TargetNotFound);

        // 5. remove MFA (no error when there was none) and revoke all sessions
        var wasEnabled = target.MfaEnabled;
        target.MfaEnabled = false;
        var revoked = await _revokeAllSessions(targetUserId, ct);
        return new MfaResetResult(MfaResetOutcome.Success, revoked, wasEnabled);
    }
}
