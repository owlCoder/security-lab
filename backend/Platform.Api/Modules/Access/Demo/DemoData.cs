using System.Collections.Concurrent;

namespace Platform.Api.Modules.Access.Demo;

/// <summary>Repeatable synthetic seed; never a production account/attempt store.</summary>
public sealed class DemoData : IAccountReader, IMembershipReader, IAttemptReader,
    IAttemptDraftWriter, IContextPolicyReader, IAuditWriter
{
    public static readonly Guid Ana = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Marko = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid Teacher = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static readonly Guid ForeignTeacher = Guid.Parse("10000000-0000-0000-0000-000000000004");
    public static readonly Guid Blocked = Guid.Parse("10000000-0000-0000-0000-000000000005");
    public static readonly Guid Assistant = Guid.Parse("10000000-0000-0000-0000-000000000006");
    public static readonly Guid Admin = Guid.Parse("10000000-0000-0000-0000-000000000007");
    public static readonly Guid Course = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid OtherCourse = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid AnaAttempt = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid MarkoAttempt = Guid.Parse("30000000-0000-0000-0000-000000000002");
    public static readonly Guid ClosedAttempt = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid DraftAttempt = Guid.Parse("30000000-0000-0000-0000-000000000004");
    public static readonly Guid CompletedAttempt = Guid.Parse("30000000-0000-0000-0000-000000000005");
    public static readonly Guid ClosedDraftAttempt = Guid.Parse("30000000-0000-0000-0000-000000000006");
    public static readonly Guid MissingAttempt = Guid.Parse("30000000-0000-0000-0000-000000000099");

    private readonly Dictionary<Guid, AccountSnapshot> accounts = new()
    {
        [Ana] = new(Ana, true), [Marko] = new(Marko, true), [Teacher] = new(Teacher, true),
        [ForeignTeacher] = new(ForeignTeacher, true), [Blocked] = new(Blocked, false),
        [Assistant] = new(Assistant, true), [Admin] = new(Admin, true)
    };
    private readonly Dictionary<(Guid, Guid), MembershipSnapshot> memberships = new()
    {
        [(Ana, Course)] = new(Ana, Course, CourseRole.Student, true),
        [(Marko, Course)] = new(Marko, Course, CourseRole.Student, true),
        [(Teacher, Course)] = new(Teacher, Course, CourseRole.Teacher, true),
        [(ForeignTeacher, OtherCourse)] = new(ForeignTeacher, OtherCourse, CourseRole.Teacher, true),
        [(Blocked, Course)] = new(Blocked, Course, CourseRole.Student, true),
        [(Assistant, Course)] = new(Assistant, Course, CourseRole.Assistant, true, false)
    };
    private readonly Dictionary<Guid, (AttemptSnapshot Snapshot, string Answer)> attempts = new()
    {
        [AnaAttempt] = (new(AnaAttempt, Ana, Course, AttemptState.Submitted, 1, "open"), "Sinteticki Anin odgovor."),
        [MarkoAttempt] = (new(MarkoAttempt, Marko, Course, AttemptState.Submitted, 1, "open"), "Sinteticki Markov odgovor."),
        [ClosedAttempt] = (new(ClosedAttempt, Ana, Course, AttemptState.Submitted, 1, "closed"), "Zatvoren testni odgovor."),
        [DraftAttempt] = (new(DraftAttempt, Ana, Course, AttemptState.InProgress, 1, "open"), "Nepredati testni odgovor."),
        [CompletedAttempt] = (new(CompletedAttempt, Ana, Course, AttemptState.Completed, 1, "open"), "Zavrseni testni odgovor."),
        [ClosedDraftAttempt] = (new(ClosedDraftAttempt, Ana, Course, AttemptState.InProgress, 1, "closed"), "Zatvoreni nacrt.")
    };
    private readonly Dictionary<string, ContextPolicy> contexts = new()
    {
        ["open"] = new("open", "acc-08:window-v1"),
        ["closed"] = new("closed", "acc-08:window-v1", null,
            new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero))
    };
    private readonly ConcurrentQueue<AccessAuditEvent> events = new();
    private readonly object attemptGate = new();
    public IReadOnlyList<AccessAuditEvent> Events => events.ToArray();
    public PortResult<AccountSnapshot> FindAccount(Guid id) => accounts.TryGetValue(id, out var a)
        ? PortResult<AccountSnapshot>.Found(a) : PortResult<AccountSnapshot>.Missing();
    public PortResult<MembershipSnapshot> FindMembership(Guid accountId, Guid courseId) =>
        memberships.TryGetValue((accountId, courseId), out var m)
            ? PortResult<MembershipSnapshot>.Found(m) : PortResult<MembershipSnapshot>.Missing();
    public PortResult<AttemptSnapshot> FindAttempt(Guid id)
    {
        lock (attemptGate)
            return attempts.TryGetValue(id, out var a) ? PortResult<AttemptSnapshot>.Found(a.Snapshot)
                : PortResult<AttemptSnapshot>.Missing();
    }
    public PortResult<AttemptView> ReadAnswer(AttemptSnapshot authorizedSnapshot)
    {
        lock (attemptGate)
            return attempts.TryGetValue(authorizedSnapshot.Id, out var a) && a.Snapshot == authorizedSnapshot
                ? PortResult<AttemptView>.Found(new(a.Snapshot.Id, a.Answer, a.Snapshot.Version))
                : PortResult<AttemptView>.Missing();
    }
    public DraftWriteResult TryUpdateDraft(AttemptSnapshot authorizedSnapshot, int expectedVersion, string answer)
    {
        lock (attemptGate)
        {
            if (!attempts.TryGetValue(authorizedSnapshot.Id, out var current))
                return new(DraftWriteStatus.NotFound);
            if (current.Snapshot != authorizedSnapshot || current.Snapshot.Version != expectedVersion ||
                current.Snapshot.State != AttemptState.InProgress)
                return new(DraftWriteStatus.Conflict);
            var updated = current.Snapshot with { Version = checked(current.Snapshot.Version + 1) };
            attempts[updated.Id] = (updated, answer);
            return new(DraftWriteStatus.Updated, new(updated.Id, answer, updated.Version));
        }
    }
    public PortResult<ContextPolicy> FindPolicy(string id) => contexts.TryGetValue(id, out var p)
        ? PortResult<ContextPolicy>.Found(p) : PortResult<ContextPolicy>.Missing();
    public void Write(AccessAuditEvent entry)
    {
        events.Enqueue(entry);
        while (events.Count > 256) events.TryDequeue(out _);
    }
}
