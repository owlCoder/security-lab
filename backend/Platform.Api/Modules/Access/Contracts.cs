namespace Platform.Api.Modules.Access;

public enum CourseRole { Student, Teacher, Assistant }
public enum AttemptState { InProgress, Submitted, Completed }
public enum PortStatus { Ok, NotFound, Unavailable }

public sealed record PortResult<T>(PortStatus Status, T? Value) where T : class
{
    public static PortResult<T> Found(T value) => new(PortStatus.Ok, value);
    public static PortResult<T> Missing() => new(PortStatus.NotFound, null);
    public static PortResult<T> Offline() => new(PortStatus.Unavailable, null);
}

public sealed record AccountSnapshot(Guid Id, bool IsActive);
public sealed record MembershipSnapshot(Guid AccountId, Guid CourseId, CourseRole Role,
    bool IsActive, bool CanReview = false);
public sealed record AttemptSnapshot(Guid Id, Guid OwnerId, Guid CourseId,
    AttemptState State, int Version, string ContextPolicyId);
public sealed record ContextPolicy(string Id, string Version,
    DateTimeOffset? NotBefore = null, DateTimeOffset? NotAfter = null);
public sealed record AttemptView(Guid Id, string Answer, int Version);
public sealed record AccessVerdict(bool Allowed, string ReasonCode, string PolicyVersion);
public sealed record ReadResult(int StatusCode, AttemptView? Resource = null);
public sealed record AccessAuditEvent(DateTimeOffset Timestamp, Guid CorrelationId,
    Guid? ActorId, string Action, Guid TargetId, Guid? CourseId,
    string Outcome, string ReasonCode, string PolicyVersion);

// Provider modules own these snapshots. The consumer never reads their tables.
public interface IAccountReader { PortResult<AccountSnapshot> FindAccount(Guid id); }
public interface IMembershipReader
{
    PortResult<MembershipSnapshot> FindMembership(Guid accountId, Guid courseId);
}
public interface IAttemptReader
{
    PortResult<AttemptSnapshot> FindAttempt(Guid id);
    PortResult<AttemptView> ReadAnswer(AttemptSnapshot authorizedSnapshot);
}
public interface IContextPolicyReader { PortResult<ContextPolicy> FindPolicy(string id); }
public interface IIdentityContext { Guid? AccountId { get; } }
public interface IAuditWriter { void Write(AccessAuditEvent entry); }
public interface IAccessDecision
{
    AccessVerdict Evaluate(AccountSnapshot actor, MembershipSnapshot membership,
        AttemptSnapshot resource, string action, ContextPolicy? context, DateTimeOffset now);
}
