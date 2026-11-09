using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Application;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;
using Platform.Audit.Contracts;

namespace Platform.Tests.AssignmentsAuditApi;

// ASN-06 TIM 15 (celina 5): adapter IDeadlineExtensionAuditWriter -> Audit modul.
[TestFixture]
public sealed class DeadlineExtensionAuditAdapterTests
{
    private const string Course = "c0c0c0c0-0000-4000-8000-000000000015";
    private const string Teacher = "11111111-1111-1111-1111-111111111111";
    private const string Student = "22222222-2222-2222-2222-222222222222";
    private const string CorrelationId = "corr-asn06-adapter";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 11, 9, 23, 59, 59, TimeSpan.Zero);

    [Test]
    public void ToDraft_MapsEveryFieldOfTheEntry()
    {
        var assignmentId = Guid.NewGuid();
        var entry = new DeadlineExtensionAuditEntry(
            DeadlineExtensionAuditOutcome.Failure, assignmentId, Course, Student, Due, Now.AddDays(1),
            DeadlineExtensionReasonCodes.NotEnrolled);

        var draft = DeadlineExtensionAuditAdapter.ToDraft(entry);

        Assert.That(draft, Is.EqualTo(new AssignmentAuditDraft(
            AssignmentAuditActions.ExtendDeadline, AssignmentAuditOutcome.Failure, assignmentId, Course, Student,
            DeadlineExtensionReasonCodes.NotEnrolled, Due, Now.AddDays(1))));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task TryWrite_ReturnsWriterResult(bool stored)
    {
        var writer = new RecordingAssignmentAuditWriter { Result = stored };
        var adapter = new DeadlineExtensionAuditAdapter(writer);

        var result = await adapter.TryWriteAsync(DeniedEntry());

        Assert.That(result, Is.EqualTo(stored));
        Assert.That(writer.Drafts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TryWrite_WhenWriterThrows_ReturnsFalse()
    {
        var adapter = new DeadlineExtensionAuditAdapter(
            new RecordingAssignmentAuditWriter { ThrowOnWrite = new InvalidOperationException("boom") });

        Assert.That(await adapter.TryWriteAsync(DeniedEntry()), Is.False);
    }

    [Test]
    public async Task TryWrite_NullEntry_ReturnsFalse()
    {
        var adapter = new DeadlineExtensionAuditAdapter(new RecordingAssignmentAuditWriter());

        Assert.That(await adapter.TryWriteAsync(null!), Is.False);
    }

    // Pravi ExtendDeadlineService (član 3) + naš adapter + naš writer: odbijen student ostavlja trag
    // sa akterom iz konteksta, čak i dok assignment.* nije u katalogu (rezerva access.denied).
    [Test]
    public void ExtendDeadline_StudentCaller_IsDeniedAndRecordedWithServerActor()
    {
        var fixture = new ServiceFixture(AuditActor.ForUser(Guid.Parse(Student)));

        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await fixture.Service.ExtendDeadlineAsync(Student, fixture.Assignment.Id, Student, Now.AddDays(2)));

        var calls = fixture.AuditSink.Calls;
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
            Assert.That(calls.Select(c => c.Draft.EventType),
                Is.EqualTo(new[] { AssignmentAuditEventTypes.DeadlineExtended, AuditEventTypes.AccessDenied }));
            Assert.That(calls[1].Context.Actor, Is.EqualTo(AuditActor.ForUser(Guid.Parse(Student))));
            Assert.That(calls[1].Context.CorrelationId, Is.EqualTo(CorrelationId));
            Assert.That(calls[1].Draft.CourseId, Is.EqualTo(Guid.Parse(Course)));
        });
    }

    [Test]
    public void ExtendDeadline_StudentNotEnrolled_WritesFailureWithReasonCode()
    {
        var fixture = new ServiceFixture(AuditActor.ForUser(Guid.Parse(Teacher)));

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.Service.ExtendDeadlineAsync(Teacher, fixture.Assignment.Id, "student-x", Now.AddDays(2)));

        var draft = fixture.AuditSink.Calls.Single().Draft;
        Assert.Multiple(() =>
        {
            Assert.That(draft.EventType, Is.EqualTo(AssignmentAuditEventTypes.DeadlineExtended));
            Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Failure));
            Assert.That(draft.Details!["reasonCode"], Is.EqualTo(DeadlineExtensionReasonCodes.NotEnrolled));
            Assert.That(draft.Resource!.Id, Is.EqualTo("student-x"));
        });
    }

    [Test]
    public async Task ExtendDeadline_Success_IsAuditedByStoreNotByAdapter()
    {
        var fixture = new ServiceFixture(AuditActor.ForUser(Guid.Parse(Teacher)));

        var result = await fixture.Service.ExtendDeadlineAsync(Teacher, fixture.Assignment.Id, Student, Now.AddDays(2));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExtendedById, Is.EqualTo(Teacher));
            Assert.That(fixture.Store.SavedAudit.Single().Outcome, Is.EqualTo(DeadlineExtensionAuditOutcome.Success));
            // Uspeh ide zajedno sa izmenom (član 1, SaveAsync); adapter ga ne upisuje drugi put.
            Assert.That(fixture.AuditSink.Calls, Is.Empty);
        });

        // Član 1 može isti nacrt da upiše u svojoj transakciji:
        var transactional = AssignmentAuditDraftMapper.ToPlatformDraft(
            DeadlineExtensionAuditAdapter.ToDraft(fixture.Store.SavedAudit.Single()));
        Assert.That(transactional.Details!["newDueAt"], Is.EqualTo(AssignmentAuditDraftMapper.FormatUtc(Now.AddDays(2))));
    }

    private static DeadlineExtensionAuditEntry DeniedEntry() =>
        new(DeadlineExtensionAuditOutcome.Denied, Guid.NewGuid(), Course, Student, null, null,
            DeadlineExtensionReasonCodes.Forbidden);

    private sealed class ServiceFixture
    {
        public ServiceFixture(AuditActor actor)
        {
            Assignment = new Assignment(Guid.NewGuid(), Course, "revision-1", null, Due, 3);
            var repository = new InMemoryAssignmentRepository();
            repository.Add(Assignment);

            var authorization = new AllowListDeadlineAuthorization();
            authorization.Allow(Teacher, Course);

            var enrollment = new AllowListEnrollment();
            enrollment.Enroll(Student, Course);

            AuditSink = new RecordingTrustedAuditWriter { Behavior = RecordingTrustedAuditWriter.CatalogWithoutAssignmentTypes };
            var writer = new AssignmentAuditWriter(
                AuditSink,
                new FixedAuditContextProvider(new AuditContext(actor, CorrelationId)),
                new FixedTimeProvider(Now),
                new CapturingLogger<AssignmentAuditWriter>());

            Service = new ExtendDeadlineService(
                repository, authorization, enrollment, Store, new DeadlineExtensionAuditAdapter(writer),
                new FixedTimeProvider(Now));
        }

        public Assignment Assignment { get; }

        public InMemoryDeadlineExtensionStore Store { get; } = new();

        public RecordingTrustedAuditWriter AuditSink { get; }

        public ExtendDeadlineService Service { get; }
    }
}
