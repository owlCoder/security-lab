using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Api.Modules.Access;

/// <summary>Concrete ACC-04 policy, registered in the existing shared default-deny engine.</summary>
public sealed class AttemptPolicy(IAccountReader accounts, IMembershipReader memberships,
    IAttemptStore attempts) : IAccessPolicy
{
    public const string Read = "attempt.read";
    public const string Update = "attempt.draft.update";
    public string Id => "acc04.ownership";
    public string Version => "acc04:v3";

    public async ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Subject is null || !Guid.TryParse(request.Subject.Id, out var actor) ||
            request.Resource?.Type != "attempt" || !Guid.TryParse(request.Resource.Id, out var id) ||
            request.Action is not (Read or Update)) return false;
        var a = await attempts.FindAttemptAsync(id, cancellationToken);
        return a is { Status: PortStatus.Ok, Value: not null } && a.Value.Id == id &&
            await CheckSnapshotAsync(actor, a.Value, request.Action, cancellationToken);
    }

    public async ValueTask<bool> CheckSnapshotAsync(Guid actor, AttemptSnapshot a, string action, CancellationToken ct = default)
    {
        if (actor == Guid.Empty || a.Id == Guid.Empty || a.OwnerId == Guid.Empty || a.CourseId == Guid.Empty ||
            a.Version < 1 || !Enum.IsDefined(a.State) || action is not (Read or Update)) return false;
        var account = await accounts.FindAccountAsync(actor, ct);
        if (account is not { Status: PortStatus.Ok, Value.IsActive: true } || account.Value.Id != actor) return false;
        var member = await memberships.FindMembershipAsync(actor, a.CourseId, ct);
        if (member is not { Status: PortStatus.Ok, Value.IsActive: true } || member.Value.AccountId != actor ||
            member.Value.CourseId != a.CourseId || !Enum.IsDefined(member.Value.Role)) return false;
        var role = member.Value.Role;
        return action == Update ? a.OwnerId == actor && role == CourseRole.Student && a.State == AttemptState.InProgress
            : a.OwnerId == actor || (role == CourseRole.Teacher && a.State != AttemptState.InProgress);
    }

    // Internal reasons are used by tests/diagnostics; never returned to an unauthorized HTTP caller.
    public string Resolve(AccessRequest request)
    {
        if (request.Subject is null || !Guid.TryParse(request.Subject.Id, out var actor)) return "NO_IDENTITY";
        if (request.Resource?.Type != "attempt" || !Guid.TryParse(request.Resource.Id, out var id)) return "BAD_RESOURCE";
        if (request.Action != Read && request.Action != Update) return "UNKNOWN_ACTION";
        var account = accounts.FindAccount(actor);
        if (account.Status != PortStatus.Ok || account.Value is not { IsActive: true } || account.Value.Id != actor)
            return account.Status == PortStatus.Unavailable ? "IDENTITY_UNAVAILABLE" : "ACCOUNT_INACTIVE";
        var resource = attempts.FindAttempt(id);
        if (resource.Status != PortStatus.Ok || resource.Value is null)
            return resource.Status == PortStatus.Unavailable ? "ATTEMPT_UNAVAILABLE" : "RESOURCE_NOT_FOUND";
        var a = resource.Value;
        if (a.Id != id || a.OwnerId == Guid.Empty || a.CourseId == Guid.Empty || a.Version < 1 || !Enum.IsDefined(a.State))
            return "INVALID_SNAPSHOT";
        var member = memberships.FindMembership(actor, a.CourseId);
        if (member.Status != PortStatus.Ok || member.Value is null)
            return member.Status == PortStatus.Unavailable ? "MEMBERSHIP_UNAVAILABLE" : "NO_MEMBERSHIP";
        var m = member.Value;
        if (!m.IsActive || m.AccountId != actor || m.CourseId != a.CourseId || !Enum.IsDefined(m.Role)) return "SCOPE_MISMATCH";
        if (request.Action == Update)
            return a.OwnerId == actor && m.Role == CourseRole.Student && a.State == AttemptState.InProgress
                ? "ALLOW" : "NOT_EDITABLE_OWNER_DRAFT";
        return a.OwnerId == actor || (m.Role == CourseRole.Teacher && a.State != AttemptState.InProgress)
            ? "ALLOW" : "NOT_OWNER_OR_COURSE_TEACHER";
    }
}
