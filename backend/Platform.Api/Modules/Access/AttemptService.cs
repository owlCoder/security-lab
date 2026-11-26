using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.Access;

public sealed class AttemptService(IAccountReader accounts, IAttemptStore store,
    IAccessDecision decisions, ISecurityAudit audit, TimeProvider clock, AttemptPolicy? boundary = null)
{
    public const int MaxAnswerLength = 4000;
    public async Task<OperationResult> Execute(Guid? actor, Guid id, UpdateDraftRequest? update,
        string correlation, CancellationToken ct = default, Func<CancellationToken, Task<bool>>? validateSession = null,
        string? authorizationAction = null)
    {
        var action = update is null ? AttemptPolicy.Read : AttemptPolicy.Update;
        Guid? course = null;
        int? before = null;
        OperationResult Finish(int status, string reason, object? value = null, int? after = null)
        {
            audit.Write(new(clock.GetUtcNow(), correlation, actor, action, id, course,
                status == 200 ? "ALLOW" : status == 409 ? "CONFLICT" : "DENY", reason, "acc04:v3", before, after));
            return new(status, value);
        }
        if (actor is null || actor == Guid.Empty) return Finish(401, "UNAUTHENTICATED");
        if (id == Guid.Empty || (update is not null && (string.IsNullOrWhiteSpace(update.Answer) ||
            update.Answer.Length > MaxAnswerLength || update.ExpectedVersion < 1)))
            return Finish(400, "INVALID_INPUT");
        try
        {
            var account = await accounts.FindAccountAsync(actor.Value, ct);
            if (account.Status != PortStatus.Ok || account.Value is not { IsActive: true } || account.Value.Id != actor)
                return Finish(404, account.Status == PortStatus.Unavailable ? "IDENTITY_UNAVAILABLE" : "ACCOUNT_INACTIVE");
            var snapshot = await store.FindAttemptAsync(id, ct);
            if (snapshot.Status != PortStatus.Ok || snapshot.Value is null || snapshot.Value.Id != id)
                return Finish(404, snapshot.Status == PortStatus.Unavailable ? "ATTEMPT_UNAVAILABLE" : "RESOURCE_NOT_FOUND");
            var a = snapshot.Value;
            course = a.CourseId;
            before = a.Version;
            var decision = await decisions.DecideAsync(new(new(actor.ToString()!, AccountState.Active), authorizationAction ?? action,
                new("attempt", id.ToString()), correlation), ct);
            if (decision.Outcome != AccessOutcome.Allow) return Finish(404, "ACCESS_DENIED");
            if (boundary is not null)
            {
                if (!await boundary.CheckSnapshotAsync(actor.Value, a, action, ct)) return Finish(404, "AUTHORIZATION_CHANGED");
                var current = await store.FindAttemptAsync(id, ct);
                if (current.Value != a)
                {
                    // Reveal a version conflict only when the SAME scope/state/owner is still authorized.
                    var versionOnly = update is not null && current.Status == PortStatus.Ok && current.Value is { } now &&
                        now.Id == a.Id && now.OwnerId == a.OwnerId && now.CourseId == a.CourseId && now.State == a.State &&
                        await boundary.CheckSnapshotAsync(actor.Value, now, action, ct);
                    return Finish(versionOnly ? 409 : 404, "AUTHORIZATION_SNAPSHOT_CHANGED");
                }
            }
            if (validateSession is not null && !await validateSession(ct)) return Finish(401, "SESSION_INVALIDATED");
            if (update is null)
            {
                var read = await store.ReadAnswerAsync(a, ct);
                if (boundary is not null && !await boundary.CheckSnapshotAsync(actor.Value, a, action, ct))
                    return Finish(404, "AUTHORIZATION_CHANGED");
                if (validateSession is not null && !await validateSession(ct)) return Finish(401, "SESSION_INVALIDATED");
                return read.Status == PortStatus.Ok && read.Value is not null && read.Value.Id == id && read.Value.Version == a.Version
                    ? Finish(200, "AUTHORIZED_READ", read.Value) : Finish(404, "SNAPSHOT_UNAVAILABLE");
            }
            // Only an already-authorized caller learns about stale versions.
            if (update.ExpectedVersion != a.Version) return Finish(409, "VERSION_CONFLICT");
            var result = await store.UpdateDraftAuthorizedAsync(a, update.ExpectedVersion, update.Answer!, actor.Value, correlation, ct);
            return result.Status switch
            {
                WriteStatus.Updated when result.Value is not null && result.Value.Id == id && result.Value.Version == a.Version + 1
                    => Finish(200, "OWNER_DRAFT_UPDATED", result.Value, result.Value.Version),
                WriteStatus.Conflict => Finish(409, "VERSION_CONFLICT"),
                _ => Finish(404, "DRAFT_STORE_UNAVAILABLE")
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is TimeoutException or IOException)
        { return Finish(404, "DEPENDENCY_UNAVAILABLE"); }
    }
}
