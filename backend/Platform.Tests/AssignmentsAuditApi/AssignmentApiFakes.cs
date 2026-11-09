using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Tests.AssignmentsAuditApi;

// ASN-04 / ASN-06 TIM 15 (celina 5)
// Test dvojnici za portove drugih članova (1: skladišta, 4: autorizacija) i za Audit modul (tim 12).
// Nisu produkcioni kod; služe da se API i audit adapter provere kroz pravi tok use-case-ova.

internal sealed class InMemoryAssignmentRepository : IAssignmentRepository
{
    private readonly ConcurrentDictionary<Guid, Assignment> _items = new();

    public void Add(Assignment assignment) => _items[assignment.Id] = assignment;

    public ValueTask<Assignment?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<Assignment?>(_items.TryGetValue(assignmentId, out var assignment) ? assignment : null);

    public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
        IReadOnlyCollection<string> courseIds, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<Assignment>>(
            _items.Values.Where(a => courseIds.Contains(a.CourseId)).ToList());

    public ValueTask AddAsync(Assignment assignment, CancellationToken cancellationToken = default)
    {
        Add(assignment);
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(Assignment assignment, CancellationToken cancellationToken = default)
    {
        Add(assignment);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Atomski limit u memoriji; isti requestId vraća isti pokušaj (idempotentnost).</summary>
internal sealed class CountingAttemptStore : IAttemptStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, string), List<(Guid RequestId, Guid AttemptId)>> _attempts = new();

    public bool ThrowOnStart { get; set; }

    public int Count(Guid assignmentId, string studentId)
    {
        lock (_gate)
        {
            return _attempts.TryGetValue((assignmentId, studentId), out var list) ? list.Count : 0;
        }
    }

    public ValueTask<Guid?> TryStartAsync(
        Guid assignmentId, string studentId, string publishedLabRevisionId, int maxAttempts, Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (ThrowOnStart)
        {
            throw new TimeoutException("db host=10.0.0.5 password=hunter2");
        }

        lock (_gate)
        {
            if (!_attempts.TryGetValue((assignmentId, studentId), out var list))
            {
                list = [];
                _attempts[(assignmentId, studentId)] = list;
            }

            var existing = list.FirstOrDefault(a => a.RequestId == requestId);
            if (existing.AttemptId != Guid.Empty)
            {
                return ValueTask.FromResult<Guid?>(existing.AttemptId);
            }

            if (list.Count >= maxAttempts)
            {
                return ValueTask.FromResult<Guid?>(null);
            }

            var attemptId = Guid.NewGuid();
            list.Add((requestId, attemptId));
            return ValueTask.FromResult<Guid?>(attemptId);
        }
    }
}

internal sealed class AllowListStartAuthorization : IStartAttemptAuthorization
{
    private readonly ConcurrentDictionary<(string, string), bool> _allowed = new();

    public void Allow(string studentId, string courseId) => _allowed[(studentId, courseId)] = true;

    public ValueTask<bool> CanStartAsync(
        string studentId, Guid assignmentId, string courseId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_allowed.ContainsKey((studentId, courseId)));
}

internal sealed class AllowListDeadlineAuthorization : IDeadlineExtensionAuthorization
{
    private readonly ConcurrentDictionary<(string, string), bool> _allowed = new();

    public void Allow(string actorId, string courseId) => _allowed[(actorId, courseId)] = true;

    public ValueTask<bool> CanExtendAsync(
        string actorId, Guid assignmentId, string courseId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_allowed.ContainsKey((actorId, courseId)));
}

internal sealed class AllowListEnrollment : ICourseEnrollmentCheck
{
    private readonly ConcurrentDictionary<(string, string), bool> _members = new();

    public void Enroll(string studentId, string courseId) => _members[(studentId, courseId)] = true;

    public ValueTask<bool> IsActiveStudentAsync(
        string studentId, string courseId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_members.ContainsKey((studentId, courseId)));
}

/// <summary>Čuva ugovor: rok i audit zapis zajedno, zastarela izmena se odbija.</summary>
internal sealed class InMemoryDeadlineExtensionStore : IDeadlineExtensionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, string), StudentDeadlineOverride> _items = new();

    public List<DeadlineExtensionAuditEntry> SavedAudit { get; } = [];

    public int SaveCount { get; private set; }

    public ValueTask<StudentDeadlineOverride?> GetAsync(
        Guid assignmentId, string studentId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return ValueTask.FromResult<StudentDeadlineOverride?>(_items.TryGetValue((assignmentId, studentId), out var value) ? value : null);
        }
    }

    public ValueTask SaveAsync(
        StudentDeadlineOverride deadlineOverride, DeadlineExtensionAuditEntry auditEntry,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var key = (deadlineOverride.AssignmentId, deadlineOverride.StudentId);
            if (_items.TryGetValue(key, out var current) && current.DueAtUtc != deadlineOverride.PreviousDueAtUtc)
            {
                throw new InvalidOperationException("Concurrent change.");
            }

            _items[key] = deadlineOverride;
            SavedAudit.Add(auditEntry);
            SaveCount++;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Dvojnik Audit modula. Podrazumevano se ponaša kao pravi katalog: nepoznat tip ili ishod se
/// odbija (UnknownEventType / OutcomeNotAllowed), poznat se „čuva”. Pamti svaki poziv sa kontekstom.
/// </summary>
internal sealed class RecordingTrustedAuditWriter : ITrustedAuditWriter
{
    private readonly object _gate = new();
    private readonly List<(AuditEventDraft Draft, AuditContext Context)> _calls = [];

    public Func<AuditEventDraft, AuditWriteResult>? Behavior { get; set; }

    public Exception? ThrowOnWrite { get; set; }

    public IReadOnlyList<(AuditEventDraft Draft, AuditContext Context)> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _calls.Clear();
        }
    }

    public ValueTask<AuditWriteResult> WriteAsync(
        AuditEventDraft draft, AuditContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _calls.Add((draft, context));
        }

        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        return ValueTask.FromResult(Behavior?.Invoke(draft) ?? CatalogGate(draft));
    }

    public static AuditWriteResult CatalogGate(AuditEventDraft draft)
    {
        if (!AuditEventCatalog.TryGet(draft.EventType, out var schema))
        {
            return AuditWriteResult.Rejected(AuditRejectionCode.UnknownEventType);
        }

        return schema.ActorsByOutcome.ContainsKey(draft.Outcome)
            ? AuditWriteResult.Recorded(Guid.NewGuid())
            : AuditWriteResult.Rejected(AuditRejectionCode.OutcomeNotAllowed);
    }

    /// <summary>Kao CatalogGate, ali assignment.* je uvek nepoznat (stanje pre izmene kataloga).</summary>
    public static AuditWriteResult CatalogWithoutAssignmentTypes(AuditEventDraft draft) =>
        draft.EventType.StartsWith("assignment.", StringComparison.Ordinal)
            ? AuditWriteResult.Rejected(AuditRejectionCode.UnknownEventType)
            : CatalogGate(draft);
}

internal sealed class FixedAuditContextProvider(AuditContext context) : IAssignmentAuditContextProvider
{
    public AuditContext GetContext() => context;
}

internal sealed class RecordingAssignmentAuditWriter : IAssignmentAuditWriter
{
    private readonly object _gate = new();
    private readonly List<AssignmentAuditDraft> _drafts = [];

    public bool Result { get; set; } = true;

    public Exception? ThrowOnWrite { get; set; }

    public IReadOnlyList<AssignmentAuditDraft> Drafts
    {
        get
        {
            lock (_gate)
            {
                return _drafts.ToList();
            }
        }
    }

    public ValueTask<bool> WriteAsync(AssignmentAuditDraft draft, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _drafts.Add(draft);
        }

        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        return ValueTask.FromResult(Result);
    }
}

/// <summary>Hvata formatirane log poruke da bi testovi proverili da u log ne ulaze tajne (AUD-03).</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly object _gate = new();
    private readonly List<(LogLevel Level, EventId EventId, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, EventId EventId, string Message)> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public string AllText => string.Join("\n", Entries.Select(e => e.Message));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Izuzetak se namerno dodaje, da bi test uhvatio i poruku izuzetka ako bi ikad bila logovana.
        var message = formatter(state, exception) + (exception is null ? string.Empty : " | " + exception);

        lock (_gate)
        {
            _entries.Add((logLevel, eventId, message));
        }
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
