using Platform.Audit.Application;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;

namespace Platform.Audit.Tests.Reading;

internal sealed class FakeQueryStore : IAuditEventQueryStore
{
    public List<AuditEvent> Rows { get; } = [];
    public Exception? Throws { get; set; }
    public int Calls { get; private set; }
    public Guid? LastCourseId { get; private set; }
    public AuditEventFilter? LastFilter { get; private set; }
    public int LastTake { get; private set; }

    public Task<IReadOnlyList<AuditEvent>> QueryCourseAsync(
        Guid courseId, AuditEventFilter filter, int take, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastCourseId = courseId;
        LastFilter = filter;
        LastTake = take;
        if (Throws is not null)
            throw Throws;

        // Behaves like the real store: only the requested course, newest first, at most `take`.
        IReadOnlyList<AuditEvent> result = Rows
            .Where(row => row.CourseId == courseId)
            .OrderByDescending(row => row.OccurredAt).ThenByDescending(row => row.Id)
            .Take(take).ToList();
        return Task.FromResult(result);
    }
}

internal sealed class FakeTrustedWriter : ITrustedAuditWriter
{
    public List<(AuditEventDraft Draft, AuditContext Context)> Written { get; } = [];
    public AuditWriteResult Result { get; set; } = AuditWriteResult.Recorded(Guid.NewGuid());
    public Exception? Throws { get; set; }

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft, AuditContext context, CancellationToken cancellationToken = default)
    {
        Written.Add((draft, context));
        if (Throws is not null)
            throw Throws;
        return ValueTask.FromResult(Result);
    }
}

internal static class ReadingExamples
{
    public static readonly Guid OtherCourseId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public static AuditEvent Event(Guid courseId, DateTimeOffset at, Guid? id = null) =>
        AuditEvent.Create(
            id ?? Guid.NewGuid(),
            at,
            new AuditEventDraft(
                AuditEventTypes.CourseMemberRoleChanged,
                AuditOutcome.Success,
                courseId,
                new AuditResourceRef(AuditResourceTypes.Account, AuditDraftExamples.TargetAccountId.ToString()),
                new Dictionary<string, string> { ["oldRole"] = "student", ["newRole"] = "assistant" }),
            AuditDraftExamples.UserContext);
}
