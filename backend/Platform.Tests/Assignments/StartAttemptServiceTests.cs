
using NUnit.Framework;
using Platform.Assignments.Application;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;
using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Infrastructure;

namespace Platform.Tests.Assignments;

[TestFixture]
public class StartAttemptServiceTests
{
    [Test]
    public void StartAttempt_WhenLimitReached_Throws()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            2,
            scope: AssignmentScope.Course);

        var repository = new FakeAssignmentRepository(assignment);
        var attemptStore = new FakeAttemptStore(2);

        var service = new StartAttemptService(
            repository, attemptStore,
            new FakeStartAttemptAuthorization(true),
            new InMemoryGroupMembership());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartAttemptAsync(
                assignment.Id,
                "student-1",
                Guid.NewGuid()));

        Assert.That(exception!.Message,
            Is.EqualTo("Maximum number of attempts reached."));
    }

    [Test]
    public async Task StartAttempt_WhenLimitNotReached_Succeeds()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            3,
            scope: AssignmentScope.Course);

        var repository = new FakeAssignmentRepository(assignment);
        var attemptStore = new FakeAttemptStore(1);

        var service = new StartAttemptService(
            repository, attemptStore,
            new FakeStartAttemptAuthorization(true),
            new InMemoryGroupMembership());

        var attemptId = await service.StartAttemptAsync(
            assignment.Id,
            "student-1",
            Guid.NewGuid());

        Assert.That(attemptId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void StartAttempt_WhenAssignmentNotFound_Throws()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            3,
            scope: AssignmentScope.Course);

        var repository = new FakeAssignmentRepository(assignment);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            repository, attemptStore,
            new FakeStartAttemptAuthorization(true),
            new InMemoryGroupMembership());

        var exception = Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await service.StartAttemptAsync(
                Guid.NewGuid(),
                "student-1",
                Guid.NewGuid()));

        Assert.That(exception!.Message,
            Is.EqualTo("Assignment not found."));
    }

    [Test]
    public void StartAttempt_WhenDeadlineExpired_Throws()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            3,
            scope: AssignmentScope.Course);

        var repository = new FakeAssignmentRepository(assignment);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            repository, attemptStore,
            new FakeStartAttemptAuthorization(true),
            new InMemoryGroupMembership());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartAttemptAsync(
                assignment.Id,
                "student-1",
                Guid.NewGuid()));

        Assert.That(exception!.Message,
            Is.EqualTo("Assignment is not available."));
    }

    [Test]
    public void StartAttempt_WhenAssignmentArchived_Throws()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            3,
            scope: AssignmentScope.Course);

        assignment.Archive();

        var repository = new FakeAssignmentRepository(assignment);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            repository, attemptStore,
            new FakeStartAttemptAuthorization(true),
            new InMemoryGroupMembership());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.StartAttemptAsync(
                assignment.Id,
                "student-1",
                Guid.NewGuid()));

        Assert.That(exception!.Message,
            Is.EqualTo("Assignment is not available."));
    }

    [Test]
    public async Task StartAttempt_WhenRequestRepeated_ReturnsSameAttempt()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            1,
            scope: AssignmentScope.Course);

        var repository = new FakeAssignmentRepository(assignment);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            repository, attemptStore,
            new FakeStartAttemptAuthorization(true),
            new InMemoryGroupMembership());

        var requestId = Guid.NewGuid();

        var firstAttempt = await service.StartAttemptAsync(
            assignment.Id, "student-1", requestId);

        var secondAttempt = await service.StartAttemptAsync(
            assignment.Id, "student-1", requestId);

        Assert.That(secondAttempt, Is.EqualTo(firstAttempt));
        Assert.That(attemptStore.AttemptCount, Is.EqualTo(1));
    }

    [Test]
    public void StartAttempt_WhenAccessDenied_Throws()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            3,
            scope: AssignmentScope.Course);

        var repository = new FakeAssignmentRepository(assignment);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            repository, attemptStore,
            new FakeStartAttemptAuthorization(false),
            new InMemoryGroupMembership());

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(
                assignment.Id,
                "student-1",
                Guid.NewGuid()));

        Assert.That(exception!.Message,
            Is.EqualTo("Access denied."));

        Assert.That(attemptStore.AttemptCount, Is.EqualTo(0));
    }

    // ASN-05: group assignment

    private const string GroupA = "group-a";
    private const string GroupB = "group-b";
    private const string Student = "student-1";

    private static Assignment CreateGroupAssignment(string groupId) =>
        new(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            3,
            scope: AssignmentScope.Group,
            groupId: groupId);

    [Test]
    public async Task StartAttempt_GroupAssignment_WhenStudentIsGroupMember_Succeeds()
    {
        var assignment = CreateGroupAssignment(GroupA);
        var membership = new InMemoryGroupMembership();
        membership.Add("course-oib", GroupA, Student);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            new FakeAssignmentRepository(assignment),
            attemptStore,
            new FakeStartAttemptAuthorization(true),
            membership);

        var attemptId = await service.StartAttemptAsync(
            assignment.Id, Student, Guid.NewGuid());

        Assert.That(attemptId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(attemptStore.AttemptCount, Is.EqualTo(1));
    }

    [Test]
    public void StartAttempt_GroupAssignment_WhenStudentIsInAnotherGroup_IsDeniedWithoutAttempt()
    {
        var assignment = CreateGroupAssignment(GroupA);
        var membership = new InMemoryGroupMembership();
        membership.Add("course-oib", GroupB, Student);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            new FakeAssignmentRepository(assignment),
            attemptStore,
            new FakeStartAttemptAuthorization(true),
            membership);

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(
                assignment.Id, Student, Guid.NewGuid()));

        Assert.That(exception!.Message, Is.EqualTo("Access denied."));
        Assert.That(attemptStore.AttemptCount, Is.EqualTo(0));
    }

    [Test]
    public void StartAttempt_GroupAssignment_WhenStudentIsInNoGroup_IsDenied()
    {
        var assignment = CreateGroupAssignment(GroupA);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            new FakeAssignmentRepository(assignment),
            attemptStore,
            new FakeStartAttemptAuthorization(true),
            new InMemoryGroupMembership());

        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(
                assignment.Id, Student, Guid.NewGuid()));

        Assert.That(attemptStore.AttemptCount, Is.EqualTo(0));
    }

    [Test]
    public void StartAttempt_GroupAssignment_WhenGroupMatchesOnAnotherCourse_IsDenied()
    {
        var assignment = CreateGroupAssignment(GroupA);
        var membership = new InMemoryGroupMembership();
        membership.Add("course-other", GroupA, Student);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            new FakeAssignmentRepository(assignment),
            attemptStore,
            new FakeStartAttemptAuthorization(true),
            membership);

        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(
                assignment.Id, Student, Guid.NewGuid()));

        Assert.That(attemptStore.AttemptCount, Is.EqualTo(0));
    }

    [Test]
    public void StartAttempt_GroupAssignment_WhenCourseAuthorizationFails_IsDeniedEvenForGroupMember()
    {
        var assignment = CreateGroupAssignment(GroupA);
        var membership = new InMemoryGroupMembership();
        membership.Add("course-oib", GroupA, Student);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            new FakeAssignmentRepository(assignment),
            attemptStore,
            new FakeStartAttemptAuthorization(false),
            membership);

        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(
                assignment.Id, Student, Guid.NewGuid()));

        Assert.That(attemptStore.AttemptCount, Is.EqualTo(0));
    }

    [Test]
    public void StartAttempt_GroupAssignment_WhenMembershipCheckFails_IsDenied()
    {
        var assignment = CreateGroupAssignment(GroupA);
        var attemptStore = new FakeAttemptStore(0);

        var service = new StartAttemptService(
            new FakeAssignmentRepository(assignment),
            attemptStore,
            new FakeStartAttemptAuthorization(true),
            new ThrowingGroupMembership());

        var exception = Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(
                assignment.Id, Student, Guid.NewGuid()));

        Assert.That(exception!.Message, Is.EqualTo("Access denied."));
        Assert.That(attemptStore.AttemptCount, Is.EqualTo(0));
    }

    [Test]
    public void StartAttempt_GroupAssignment_WhenNotMemberAndExpired_DeniesBeforeRevealingAvailability()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            3,
            scope: AssignmentScope.Group,
            groupId: GroupA);

        var service = new StartAttemptService(
            new FakeAssignmentRepository(assignment),
            new FakeAttemptStore(0),
            new FakeStartAttemptAuthorization(true),
            new InMemoryGroupMembership());

        Assert.ThrowsAsync<UnauthorizedAccessException>(
            async () => await service.StartAttemptAsync(
                assignment.Id, Student, Guid.NewGuid()));
    }

    [Test]
    public async Task StartAttempt_CourseAssignment_DoesNotRequireGroupMembership()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            3,
            scope: AssignmentScope.Course);

        var service = new StartAttemptService(
            new FakeAssignmentRepository(assignment),
            new FakeAttemptStore(0),
            new FakeStartAttemptAuthorization(true),
            new ThrowingGroupMembership());

        var attemptId = await service.StartAttemptAsync(
            assignment.Id, Student, Guid.NewGuid());

        Assert.That(attemptId, Is.Not.EqualTo(Guid.Empty));
    }

    private sealed class ThrowingGroupMembership : IGroupMembership
    {
        public ValueTask<bool> IsGroupMemberAsync(
            string studentId,
            string courseId,
            string groupId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Membership store unavailable.");
    }

    private sealed class FakeAssignmentRepository(
        Assignment assignment) : IAssignmentRepository
    {
        public ValueTask<Assignment?> GetByIdAsync(
            Guid assignmentId,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<Assignment?>(
                assignmentId == assignment.Id ? assignment : null);
        }

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

    private sealed class FakeAttemptStore(
        int existingAttempts) : IAttemptStore
    {
        private int _attemptCount = existingAttempts;

        private readonly Dictionary<(Guid, string, Guid), Guid>
            _requests = new();

        public int AttemptCount => _attemptCount;

        public ValueTask<Guid?> TryStartAsync(
            Guid assignmentId,
            string studentId,
            string publishedLabRevisionId,
            int maxAttempts,
            Guid requestId,
            CancellationToken cancellationToken = default)
        {
            var key = (assignmentId, studentId, requestId);

            if (_requests.TryGetValue(key, out var existingId))
            {
                return ValueTask.FromResult<Guid?>(existingId);
            }

            if (_attemptCount >= maxAttempts)
            {
                return ValueTask.FromResult<Guid?>(null);
            }

            var attemptId = Guid.NewGuid();

            _requests.Add(key, attemptId);
            _attemptCount++;

            return ValueTask.FromResult<Guid?>(attemptId);
        }
    }

    private sealed class FakeStartAttemptAuthorization(bool allow)
        : IStartAttemptAuthorization
    {
        public ValueTask<bool> CanStartAsync(
            string studentId,
            Guid assignmentId,
            string courseId,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(allow);
        }
    }
}
