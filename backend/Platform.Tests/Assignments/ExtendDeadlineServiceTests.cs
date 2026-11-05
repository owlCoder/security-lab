using Platform.Assignments.Application;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;

namespace Platform.Tests.Assignments;

// ASN-06 TIM 15
[TestFixture]
public sealed class ExtendDeadlineServiceTests
{
    private const string Instructor = "teacher-1";
    private const string Student = "student-1";

    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 11, 9, 23, 59, 59, TimeSpan.Zero);

    [Test]
    public async Task Extend_WhenInstructorIsAuthorized_SavesIndividualDeadlineAndAudit()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.ExtendDeadlineAsync(
            Instructor, fixture.Assignment.Id, Student, Now.AddDays(2));

        var stored = await fixture.Store.GetAsync(fixture.Assignment.Id, Student);

        Assert.Multiple(() =>
        {
            Assert.That(result.DueAtUtc, Is.EqualTo(Now.AddDays(2)));
            Assert.That(result.PreviousDueAtUtc, Is.EqualTo(Due));
            Assert.That(result.ExtendedById, Is.EqualTo(Instructor));
            Assert.That(stored, Is.Not.Null);
            Assert.That(fixture.Store.SavedAudit, Has.Count.EqualTo(1));
            Assert.That(
                fixture.Store.SavedAudit[0].Outcome,
                Is.EqualTo(DeadlineExtensionAuditOutcome.Success));
        });
    }

    [Test]
    public async Task Extend_ForOneStudent_DoesNotChangeDeadlineOfOtherStudents()
    {
        var fixture = new Fixture();

        await fixture.Service.ExtendDeadlineAsync(
            Instructor, fixture.Assignment.Id, Student, Now.AddDays(2));

        var other = await fixture.Store.GetAsync(fixture.Assignment.Id, "student-2");

        Assert.That(other, Is.Null);
        Assert.That(fixture.Assignment.DueAtUtc, Is.EqualTo(Due));
    }

    [Test]
    public void Extend_WhenCallerIsNotAuthorized_ThrowsAndChangesNothing()
    {
        var fixture = new Fixture();

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                "student-1", fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Access denied."));
            Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Extend_WhenInstructorBelongsToAnotherCourse_Throws()
    {
        var fixture = new Fixture(instructors: ["teacher-of-other-course"]);

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.That(exception!.Message, Is.EqualTo("Access denied."));
        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    public void Extend_WhenAssignmentDoesNotExist_ReturnsSameErrorAsDenied()
    {
        var fixture = new Fixture();

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, Guid.NewGuid(), Student, Now.AddDays(2)));

        Assert.That(exception!.Message, Is.EqualTo("Access denied."));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("teacher-1\n")]
    [TestCase("teacher 1")]
    public void Extend_WhenActorIdIsInvalid_Throws(string actorId)
    {
        var fixture = new Fixture();

        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                actorId, fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    public void Extend_WhenDenied_WritesDeniedAuditRecord()
    {
        var fixture = new Fixture();

        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                "student-1", fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.That(fixture.Audit.Entries, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(
                fixture.Audit.Entries[0].Outcome,
                Is.EqualTo(DeadlineExtensionAuditOutcome.Denied));
            Assert.That(
                fixture.Audit.Entries[0].ReasonCode,
                Is.EqualTo(DeadlineExtensionReasonCodes.Forbidden));
        });
    }

    [Test]
    public void Extend_WhenAuditWriterFailsOnDenial_StillDenies()
    {
        var fixture = new Fixture();
        fixture.Audit.ThrowOnWrite = true;

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                "student-1", fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.That(exception!.Message, Is.EqualTo("Access denied."));
        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [TestCase("")]
    [TestCase("student 1")]
    [TestCase("student-1\n")]
    public void Extend_WhenStudentIdIsInvalid_Throws(string studentId)
    {
        var fixture = new Fixture();

        Assert.ThrowsAsync<ArgumentException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, studentId, Now.AddDays(2)));

        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    public void Extend_WhenStudentIsNotEnrolled_Throws()
    {
        var fixture = new Fixture(enrolledStudents: ["student-2"]);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.That(
            exception!.Message,
            Is.EqualTo("Student is not an active member of the course."));
        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    public void Extend_WhenAssignmentIsArchived_Throws()
    {
        var fixture = new Fixture();
        fixture.Assignment.Archive();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.That(exception!.Message, Is.EqualTo("Assignment is archived."));
        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    public void Extend_WhenNewDeadlineEqualsCurrentDeadline_Throws()
    {
        var fixture = new Fixture();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Due));

        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    public void Extend_WhenNewDeadlineIsEarlierThanCurrentDeadline_Throws()
    {
        var fixture = new Fixture();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Due.AddDays(-1)));

        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    public void Extend_WhenNewDeadlineIsAlreadyInThePast_Throws()
    {
        var fixture = new Fixture();

        // Later than the assignment deadline, but earlier than "now": the extension would be useless.
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Now.AddHours(-1)));

        Assert.That(fixture.Store.SaveCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Extend_WhenExtendedAgain_UsesCurrentIndividualDeadlineAsPrevious()
    {
        var fixture = new Fixture();

        await fixture.Service.ExtendDeadlineAsync(
            Instructor, fixture.Assignment.Id, Student, Now.AddDays(2));

        var second = await fixture.Service.ExtendDeadlineAsync(
            Instructor, fixture.Assignment.Id, Student, Now.AddDays(5));

        Assert.Multiple(() =>
        {
            Assert.That(second.PreviousDueAtUtc, Is.EqualTo(Now.AddDays(2)));
            Assert.That(second.DueAtUtc, Is.EqualTo(Now.AddDays(5)));
            Assert.That(fixture.Store.SavedAudit, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Extend_WhenSecondDeadlineIsNotLaterThanFirst_Throws()
    {
        var fixture = new Fixture();

        await fixture.Service.ExtendDeadlineAsync(
            Instructor, fixture.Assignment.Id, Student, Now.AddDays(5));

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.That(fixture.Store.SaveCount, Is.EqualTo(1));
    }

    [Test]
    public void Extend_WhenDeadlineChangedConcurrently_ThrowsAndKeepsOtherChange()
    {
        var fixture = new Fixture();
        var concurrent = new StudentDeadlineOverride(
            fixture.Assignment.Id, Student, Now.AddDays(9), Due, "teacher-2", Now);

        // Another instructor saves between this request's read and its save.
        fixture.Store.BeforeSave = () => fixture.Store.Seed(concurrent);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Now.AddDays(2)));

        var stored = fixture.Store.Peek(fixture.Assignment.Id, Student);
        Assert.That(stored!.DueAtUtc, Is.EqualTo(Now.AddDays(9)));
    }

    [Test]
    public void Extend_WhenStoreFails_PropagatesAndWritesFailureAudit()
    {
        var fixture = new Fixture();
        fixture.Store.FailOnSave = true;

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Store.Peek(fixture.Assignment.Id, Student), Is.Null);
            Assert.That(fixture.Store.SavedAudit, Is.Empty);
            Assert.That(fixture.Audit.Entries, Has.Count.EqualTo(1));
            Assert.That(
                fixture.Audit.Entries[0].Outcome,
                Is.EqualTo(DeadlineExtensionAuditOutcome.Failure));
            Assert.That(
                fixture.Audit.Entries[0].ReasonCode,
                Is.EqualTo(DeadlineExtensionReasonCodes.SaveFailed));
        });
    }

    [Test]
    public void Extend_WhenValidationFails_WritesFailureAuditWithoutFreeText()
    {
        var fixture = new Fixture(enrolledStudents: []);

        Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fixture.Service.ExtendDeadlineAsync(
                Instructor, fixture.Assignment.Id, Student, Now.AddDays(2)));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Audit.Entries, Has.Count.EqualTo(1));
            Assert.That(
                fixture.Audit.Entries[0].ReasonCode,
                Is.EqualTo(DeadlineExtensionReasonCodes.NotEnrolled));
        });
    }

    private sealed class Fixture
    {
        public Fixture(
            string[]? instructors = null,
            string[]? enrolledStudents = null)
        {
            Assignment = new Assignment(
                Guid.NewGuid(),
                "course-oib",
                "revision-1",
                Due.AddDays(-7),
                Due,
                maxAttempts: 2);

            Store = new FakeDeadlineExtensionStore();
            Audit = new FakeDeadlineExtensionAuditWriter();

            Service = new ExtendDeadlineService(
                new FakeAssignmentRepository(Assignment),
                new FakeAuthorization(instructors ?? [Instructor]),
                new FakeEnrollment(enrolledStudents ?? [Student, "student-2"]),
                Store,
                Audit,
                new FixedTimeProvider(Now));
        }

        public Assignment Assignment { get; }

        public FakeDeadlineExtensionStore Store { get; }

        public FakeDeadlineExtensionAuditWriter Audit { get; }

        public ExtendDeadlineService Service { get; }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeAssignmentRepository(Assignment assignment) : IAssignmentRepository
    {
        public ValueTask<Assignment?> GetByIdAsync(
            Guid assignmentId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<Assignment?>(
                assignmentId == assignment.Id ? assignment : null);

        public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
            IReadOnlyCollection<string> courseIds,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public ValueTask AddAsync(
            Assignment assignment,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public ValueTask UpdateAsync(
            Assignment assignment,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    // Deny by default: only the listed instructors are allowed.
    private sealed class FakeAuthorization(string[] instructors) : IDeadlineExtensionAuthorization
    {
        public ValueTask<bool> CanExtendAsync(
            string actorId,
            Guid assignmentId,
            string courseId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(instructors.Contains(actorId));
    }

    private sealed class FakeEnrollment(string[] students) : ICourseEnrollmentCheck
    {
        public ValueTask<bool> IsActiveStudentAsync(
            string studentId,
            string courseId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(students.Contains(studentId));
    }

    private sealed class FakeDeadlineExtensionAuditWriter : IDeadlineExtensionAuditWriter
    {
        public List<DeadlineExtensionAuditEntry> Entries { get; } = [];

        public bool ThrowOnWrite { get; set; }

        public ValueTask<bool> TryWriteAsync(
            DeadlineExtensionAuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("Audit storage is down.");
            }

            Entries.Add(entry);
            return ValueTask.FromResult(true);
        }
    }

    // Same contract as a real store: change and audit entry are saved together or not at all,
    // and a save over a deadline that changed in the meantime is rejected.
    private sealed class FakeDeadlineExtensionStore : IDeadlineExtensionStore
    {
        private readonly Dictionary<(Guid, string), StudentDeadlineOverride> _overrides = new();

        public List<DeadlineExtensionAuditEntry> SavedAudit { get; } = [];

        public int SaveCount { get; private set; }

        public bool FailOnSave { get; set; }

        public Action? BeforeSave { get; set; }

        public void Seed(StudentDeadlineOverride deadlineOverride)
            => _overrides[(deadlineOverride.AssignmentId, deadlineOverride.StudentId)] = deadlineOverride;

        public StudentDeadlineOverride? Peek(Guid assignmentId, string studentId)
            => _overrides.GetValueOrDefault((assignmentId, studentId));

        public ValueTask<StudentDeadlineOverride?> GetAsync(
            Guid assignmentId,
            string studentId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Peek(assignmentId, studentId));

        public ValueTask SaveAsync(
            StudentDeadlineOverride deadlineOverride,
            DeadlineExtensionAuditEntry auditEntry,
            CancellationToken cancellationToken = default)
        {
            BeforeSave?.Invoke();

            if (FailOnSave)
            {
                throw new InvalidOperationException("Storage is down.");
            }

            var key = (deadlineOverride.AssignmentId, deadlineOverride.StudentId);

            // The stored deadline must still be the one the caller read.
            if (_overrides.TryGetValue(key, out var stored)
                && stored.DueAtUtc != deadlineOverride.PreviousDueAtUtc)
            {
                throw new InvalidOperationException("Deadline was changed concurrently.");
            }

            _overrides[key] = deadlineOverride;
            SavedAudit.Add(auditEntry);
            SaveCount++;
            return ValueTask.CompletedTask;
        }
    }
}
