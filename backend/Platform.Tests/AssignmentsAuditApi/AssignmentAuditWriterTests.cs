using Microsoft.Extensions.Logging;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Audit.Contracts;

namespace Platform.Tests.AssignmentsAuditApi;

// ASN-04 / ASN-06 TIM 15 (celina 5): akter, radnja, vreme, ishod, korelacioni ID, bez tajni.
[TestFixture]
public sealed class AssignmentAuditWriterTests
{
    private static readonly Guid TeacherId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AssignmentId = Guid.Parse("a5500000-0000-4000-8000-000000000002");
    private const string CorrelationId = "corr-asn-0001";

    private RecordingTrustedAuditWriter _platform = null!;
    private CapturingLogger<AssignmentAuditWriter> _logger = null!;
    private AssignmentAuditWriter _writer = null!;

    [SetUp]
    public void SetUp()
    {
        _platform = new RecordingTrustedAuditWriter();
        _logger = new CapturingLogger<AssignmentAuditWriter>();
        _writer = CreateWriter(_platform, _logger);
    }

    [Test]
    public async Task Write_UsesActorAndCorrelationFromTrustedContext()
    {
        _platform.Behavior = _ => AuditWriteResult.Recorded(Guid.NewGuid());

        var recorded = await _writer.WriteAsync(Draft(AssignmentAuditOutcome.Failure, "not_enrolled"));

        Assert.That(recorded, Is.True);
        Assert.That(_platform.Calls, Has.Count.EqualTo(1));
        var (draft, context) = _platform.Calls[0];
        Assert.Multiple(() =>
        {
            Assert.That(context.Actor, Is.EqualTo(AuditActor.ForUser(TeacherId)));
            Assert.That(context.CorrelationId, Is.EqualTo(CorrelationId));
            Assert.That(draft.EventType, Is.EqualTo(AssignmentAuditEventTypes.DeadlineExtended));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(draft.Details!["reasonCode"], Is.EqualTo("not_enrolled"));
        });
    }

    [Test]
    public async Task Write_LogsActorActionTimeOutcomeAndCorrelation()
    {
        await _writer.WriteAsync(Draft(AssignmentAuditOutcome.Denied, "forbidden"));

        var record = _logger.Entries.Single(e => e.EventId.Id == AssignmentAuditWriter.RecordEvent.Id).Message;

        Assert.Multiple(() =>
        {
            Assert.That(record, Does.Contain(AssignmentAuditActions.ExtendDeadline));
            Assert.That(record, Does.Contain("Denied"));
            Assert.That(record, Does.Contain(TeacherId.ToString()));
            Assert.That(record, Does.Contain("2026-11-10T09:00:00"));
            Assert.That(record, Does.Contain(CorrelationId));
        });
    }

    [Test]
    public async Task Write_DeniedWhileTypeNotInCatalog_FallsBackToAccessDenied()
    {
        _platform.Behavior = RecordingTrustedAuditWriter.CatalogWithoutAssignmentTypes;

        var recorded = await _writer.WriteAsync(Draft(AssignmentAuditOutcome.Denied, "forbidden"));

        Assert.That(recorded, Is.True);
        Assert.That(_platform.Calls, Has.Count.EqualTo(2));
        var (fallback, context) = _platform.Calls[1];
        Assert.Multiple(() =>
        {
            Assert.That(_platform.Calls[0].Draft.EventType, Is.EqualTo(AssignmentAuditEventTypes.DeadlineExtended));
            Assert.That(fallback.EventType, Is.EqualTo(AuditEventTypes.AccessDenied));
            Assert.That(fallback.Details!["action"], Is.EqualTo(AssignmentAuditActions.ExtendDeadline));
            Assert.That(context.CorrelationId, Is.EqualTo(CorrelationId));
        });
    }

    [Test]
    public async Task Write_FailureWhileTypeNotInCatalog_IsNotRecorded_AndRaisesAlarm()
    {
        _platform.Behavior = RecordingTrustedAuditWriter.CatalogWithoutAssignmentTypes;

        var recorded = await _writer.WriteAsync(Draft(AssignmentAuditOutcome.Failure, "invalid_deadline"));

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.False);
            Assert.That(_platform.Calls, Has.Count.EqualTo(1));
            Assert.That(_logger.Entries.Any(e => e.EventId.Id == AssignmentAuditWriter.AlarmEvent.Id
                && e.Level == LogLevel.Warning
                && e.Message.Contains("UnknownEventType")), Is.True);
        });
    }

    [Test]
    public async Task Write_WhenAuditStorageUnavailable_ReturnsFalseAndRaisesAlarm()
    {
        _platform.Behavior = _ => AuditWriteResult.Unavailable();

        var recorded = await _writer.WriteAsync(Draft(AssignmentAuditOutcome.Failure, "save_failed"));

        Assert.That(recorded, Is.False);
        Assert.That(_logger.Entries.Any(e => e.EventId.Id == AssignmentAuditWriter.AlarmEvent.Id), Is.True);
    }

    [Test]
    public async Task Write_WhenAuditWriterThrows_ReturnsFalseWithoutLeakingExceptionMessage()
    {
        _platform.ThrowOnWrite = new InvalidOperationException("Host=db;Password=s3cr3t");

        var recorded = await _writer.WriteAsync(Draft(AssignmentAuditOutcome.Denied, "forbidden"));

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.False);
            Assert.That(_logger.AllText, Does.Not.Contain("s3cr3t"));
            Assert.That(_logger.AllText, Does.Contain(nameof(InvalidOperationException)));
        });
    }

    [Test]
    public void Write_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _writer.WriteAsync(Draft(AssignmentAuditOutcome.Denied, "forbidden"), cts.Token));
    }

    [Test]
    public async Task Write_UnknownAction_IsRejectedBeforeAudit()
    {
        var recorded = await _writer.WriteAsync(
            new AssignmentAuditDraft("assignment.grade.change", AssignmentAuditOutcome.Success, AssignmentId));

        Assert.That(recorded, Is.False);
        Assert.That(_platform.Calls, Is.Empty);
    }

    [Test]
    public async Task Write_NeverCopiesInjectedValuesIntoLogOrDraft()
    {
        const string injected = "x\nSecurityLab.Session=tok3n";

        await _writer.WriteAsync(new AssignmentAuditDraft(
            AssignmentAuditActions.ExtendDeadline,
            AssignmentAuditOutcome.Failure,
            AssignmentId,
            CourseId: injected,
            StudentId: injected,
            ReasonCode: injected));

        var draft = _platform.Calls.Single().Draft;
        Assert.Multiple(() =>
        {
            Assert.That(_logger.AllText, Does.Not.Contain("tok3n"));
            Assert.That(draft.Resource, Is.Null);
            Assert.That(draft.CourseId, Is.Null);
            Assert.That(draft.Details!.Values, Has.None.Contains("tok3n"));
        });
    }

    [Test]
    public async Task Write_WithoutSession_UsesAnonymousActor()
    {
        var writer = CreateWriter(_platform, _logger, AuditActor.Anonymous);

        await writer.WriteAsync(Draft(AssignmentAuditOutcome.Denied, "forbidden"));

        Assert.That(_platform.Calls[0].Context.Actor, Is.EqualTo(AuditActor.Anonymous));
    }

    private static AssignmentAuditWriter CreateWriter(
        RecordingTrustedAuditWriter platform,
        CapturingLogger<AssignmentAuditWriter> logger,
        AuditActor? actor = null) =>
        new(
            platform,
            new FixedAuditContextProvider(new AuditContext(actor ?? AuditActor.ForUser(TeacherId), CorrelationId)),
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero)),
            logger);

    private static AssignmentAuditDraft Draft(AssignmentAuditOutcome outcome, string? reasonCode) =>
        new(
            AssignmentAuditActions.ExtendDeadline,
            outcome,
            AssignmentId,
            "course-oib",
            "22222222-2222-2222-2222-222222222222",
            reasonCode,
            new DateTimeOffset(2026, 11, 9, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 20, 23, 59, 0, TimeSpan.Zero));
}
