namespace Platform.Api.Modules.Access;

public enum PortStatus { Ok, NotFound, Unavailable }
public sealed record PortResult<T>(PortStatus Status, T? Value) where T : class
{
    public static PortResult<T> Found(T value) => new(PortStatus.Ok, value);
    public static PortResult<T> Missing() => new(PortStatus.NotFound, null);
    public static PortResult<T> Offline() => new(PortStatus.Unavailable, null);
}
public enum CourseRole { Student, Teacher, Assistant }
public enum AttemptState { InProgress, Submitted, Completed }
public enum WriteStatus { Updated, Conflict, Denied, Unavailable }
public sealed record AccountSnapshot(Guid Id, bool IsActive);
public sealed record MembershipSnapshot(Guid AccountId, Guid CourseId, CourseRole Role, bool IsActive);
public sealed record AttemptSnapshot(Guid Id, Guid OwnerId, Guid CourseId, AttemptState State, int Version);
public sealed record AttemptView(Guid Id, string Answer, int Version);
public sealed record UpdateDraftRequest(string? Answer, int ExpectedVersion);
public sealed record OperationResult(int Status, object? Value = null);
public sealed record WriteResult(WriteStatus Status, AttemptView? Value = null);
public sealed record SecurityEvent(DateTimeOffset Timestamp, string CorrelationId, Guid? ActorId,
    string Action, Guid TargetId, Guid? CourseId, string Outcome, string Reason,
    string PolicyVersion, int? VersionBefore = null, int? VersionAfter = null);

// The account, membership and attempt modules own these records; consume snapshots, not their tables.
public interface IAccountReader
{
    // Existing simulators remain source-compatible. I/O adapters implement the async port.
    PortResult<AccountSnapshot> FindAccount(Guid id) => throw new NotSupportedException("Use FindAccountAsync for this adapter.");
    ValueTask<PortResult<AccountSnapshot>> FindAccountAsync(Guid id, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(FindAccount(id)); }
}
public interface IMembershipReader { PortResult<MembershipSnapshot> FindMembership(Guid actorId, Guid courseId); }
public interface IAttemptStore
{
    PortResult<AttemptSnapshot> FindAttempt(Guid id);
    PortResult<AttemptView> ReadAnswer(AttemptSnapshot authorizedSnapshot);
    WriteResult UpdateDraft(AttemptSnapshot authorizedSnapshot, int expectedVersion, string answer);
}
public interface ISecurityAudit
{
    void Write(SecurityEvent entry) => throw new NotSupportedException("Use WriteAsync for this adapter.");
    ValueTask WriteAsync(SecurityEvent entry, CancellationToken ct = default)
    { Write(entry); return ValueTask.CompletedTask; } // preserve the legacy producer record even if the client disconnects
}
