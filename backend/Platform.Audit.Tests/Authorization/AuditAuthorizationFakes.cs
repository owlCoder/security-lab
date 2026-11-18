using System.Collections.Concurrent;
using System.Collections.Frozen;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;

namespace Platform.Audit.Tests.Authorization;

// Only tests install this provider. Production retains the fail-closed fallback.
internal sealed class StubCourseAuditMemberships : IMembershipReader
{
    private readonly ConcurrentDictionary<(Guid AccountId, Guid CourseId), CourseMembership> memberships = new();
    private int calls;
    public int Calls => Volatile.Read(ref calls);
    public Func<Guid, Guid, CancellationToken, ValueTask<CourseMembership?>>? Handler { get; set; }

    public void Set(CourseMembership membership) =>
        memberships[(membership.AccountId, membership.CourseId)] = membership with
        {
            DelegatedActions = membership.DelegatedActions.ToFrozenSet(StringComparer.Ordinal)
        };

    public void Remove(Guid accountId, Guid courseId) => memberships.TryRemove((accountId, courseId), out _);
    public CourseMembership? Snapshot(Guid accountId, Guid courseId) =>
        memberships.TryGetValue((accountId, courseId), out var membership) ? membership : null;

    public ValueTask<CourseMembership?> GetAsync(Guid accountId, Guid courseId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref calls);
        cancellationToken.ThrowIfCancellationRequested();
        return Handler is null
            ? ValueTask.FromResult(Snapshot(accountId, courseId))
            : Handler(accountId, courseId, cancellationToken);
    }
}

internal sealed class AuditAuthorizationClock : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal static class AuditAuthorizationExamples
{
    public static readonly Guid TeacherId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid OtherAccountId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid CourseK = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    public static readonly Guid CourseL = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");

    public static AccessRequest Request(Guid? accountId = null, Guid? courseId = null, string correlationId = "audit-test") =>
        new(new SubjectRef((accountId ?? TeacherId).ToString("D"), AccountState.Active),
            "audit.events.read", new ResourceRef("course", (courseId ?? CourseK).ToString("D")), correlationId);

    public static CourseMembership Membership(CourseRole role = CourseRole.Teacher,
        Guid? accountId = null, Guid? courseId = null, params string[] delegatedActions) =>
        new(accountId ?? TeacherId, courseId ?? CourseK, role, MembershipStatus.Active,
            delegatedActions.ToFrozenSet(StringComparer.Ordinal));
}

internal sealed class RecordingAccessAuditWriter : IAccessAuditWriter
{
    private readonly ConcurrentQueue<(AccessRequest Request, AccessDecision Decision)> entries = new();
    public IReadOnlyList<(AccessRequest Request, AccessDecision Decision)> Entries => entries.ToArray();

    public ValueTask WriteAsync(AccessRequest request, AccessDecision decision, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        entries.Enqueue((request, decision));
        return ValueTask.CompletedTask;
    }
}
