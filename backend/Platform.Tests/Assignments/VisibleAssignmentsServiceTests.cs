// ASN-05 TIM 13
using Platform.Assignments.Application;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Domain.Models;
using Platform.Assignments.Infrastructure;

namespace Platform.Tests.Assignments;

[TestFixture]
public sealed class VisibleAssignmentsServiceTests
{
    private const string CourseOib = "course-oib";
    private const string CourseOther = "course-other";
    private const string GroupA = "group-a";
    private const string GroupB = "group-b";

    private const string StudentA = "student-a";       // enrolled, member of group-a
    private const string StudentB = "student-b";       // enrolled, member of group-b
    private const string NoGroup = "student-no-group"; // enrolled, in no group
    private const string Outsider = "student-outside"; // not enrolled anywhere

    private static readonly DateTimeOffset Due = new(2026, 12, 1, 23, 59, 59, TimeSpan.Zero);

    [Test]
    public async Task GetVisible_StudentSeesCourseAssignmentOfOwnCourse()
    {
        var courseAssignment = CourseAssignment(CourseOib);
        var fixture = new Fixture(courseAssignment);

        var visible = await fixture.Service.GetVisibleForStudentAsync(NoGroup, [CourseOib]);

        Assert.That(visible, Is.EqualTo(new[] { courseAssignment }));
    }

    [Test]
    public async Task GetVisible_GroupMemberSeesGroupAssignment()
    {
        var groupAssignment = GroupAssignment(GroupA);
        var fixture = new Fixture(groupAssignment);

        var visible = await fixture.Service.GetVisibleForStudentAsync(StudentA, [CourseOib]);

        Assert.That(visible, Is.EqualTo(new[] { groupAssignment }));
    }

    [Test]
    public async Task GetVisible_StudentOfAnotherGroup_SeesCourseAssignmentButNotGroupAssignment()
    {
        var groupAssignment = GroupAssignment(GroupA);
        var courseAssignment = CourseAssignment(CourseOib);
        var fixture = new Fixture(groupAssignment, courseAssignment);

        var visible = await fixture.Service.GetVisibleForStudentAsync(StudentB, [CourseOib]);

        Assert.That(visible, Is.EqualTo(new[] { courseAssignment }));
    }

    [Test]
    public async Task GetVisible_StudentWithoutGroup_DoesNotSeeGroupAssignment()
    {
        var fixture = new Fixture(GroupAssignment(GroupA));

        var visible = await fixture.Service.GetVisibleForStudentAsync(NoGroup, [CourseOib]);

        Assert.That(visible, Is.Empty);
    }

    [Test]
    public async Task GetVisible_StudentNotEnrolled_SeesNothing()
    {
        var fixture = new Fixture(CourseAssignment(CourseOib), GroupAssignment(GroupA));

        var visible = await fixture.Service.GetVisibleForStudentAsync(Outsider, [CourseOib]);

        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.Empty);
            // Both are hidden because of enrollment, before group membership matters.
            Assert.That(fixture.Log.Entries.Select(e => e.Code),
                Is.All.EqualTo(AssignmentGroupCodes.NotEnrolled));
        });
    }

    [Test]
    public async Task GetVisible_MixedList_ReturnsExactlyAllowedAssignmentsInStoredOrder()
    {
        var courseOib = CourseAssignment(CourseOib);
        var groupA = GroupAssignment(GroupA);
        var groupB = GroupAssignment(GroupB);
        var courseOther = CourseAssignment(CourseOther); // StudentA is not enrolled in this course
        var fixture = new Fixture(courseOib, groupA, groupB, courseOther);

        var visible = await fixture.Service.GetVisibleForStudentAsync(StudentA, [CourseOib, CourseOther]);

        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.EqualTo(new[] { courseOib, groupA }));
            Assert.That(fixture.Log.Entries.Select(e => (e.AssignmentId, e.Code)), Is.EquivalentTo(new[]
            {
                (groupB.Id, AssignmentGroupCodes.NotGroupMember),
                (courseOther.Id, AssignmentGroupCodes.NotEnrolled),
            }));
            // Enrollment is asked once per course, not once per assignment.
            Assert.That(fixture.Enrollment.Calls, Is.EqualTo(2));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("student a")]
    [TestCase("student-a\n")]
    public async Task GetVisible_WithInvalidStudentId_ReturnsEmptyWithoutReadingAssignments(string studentId)
    {
        var fixture = new Fixture(CourseAssignment(CourseOib));

        var visible = await fixture.Service.GetVisibleForStudentAsync(studentId, [CourseOib]);

        Assert.That(visible, Is.Empty);
        Assert.That(fixture.Repository.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task GetVisible_WhenMembershipCheckFails_HidesOnlyThatAssignment()
    {
        var groupAssignment = GroupAssignment(GroupA);
        var courseAssignment = CourseAssignment(CourseOib);
        var fixture = new Fixture(new ThrowingGroupMembership(), groupAssignment, courseAssignment);

        var visible = await fixture.Service.GetVisibleForStudentAsync(StudentA, [CourseOib]);

        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.EqualTo(new[] { courseAssignment }));
            Assert.That(fixture.Log.Entries.Single().Code, Is.EqualTo(VisibleAssignmentsService.CheckFailedCode));
        });
    }

    [Test]
    public async Task GetVisible_HiddenAssignment_IsOnlyInServerLog()
    {
        var groupAssignment = GroupAssignment(GroupA);
        var fixture = new Fixture(groupAssignment);

        var visible = await fixture.Service.GetVisibleForStudentAsync(StudentB, [CourseOib]);

        Assert.Multiple(() =>
        {
            Assert.That(visible, Is.Empty);
            Assert.That(fixture.Log.Entries, Is.EqualTo(new[]
            {
                new HiddenEntry(StudentB, groupAssignment.Id, AssignmentGroupCodes.NotGroupMember),
            }));
        });
    }

    private static Assignment CourseAssignment(string courseId) =>
        new(Guid.NewGuid(), courseId, "revision-1", null, Due, 1, AssignmentScope.Course);

    private static Assignment GroupAssignment(string groupId) =>
        new(Guid.NewGuid(), CourseOib, "revision-1", null, Due, 1, AssignmentScope.Group, groupId: groupId);

    private sealed class Fixture
    {
        public Fixture(params Assignment[] assignments)
            : this(DefaultGroups(), assignments)
        {
        }

        public Fixture(IGroupMembership groups, params Assignment[] assignments)
        {
            Repository = new FakeAssignmentRepository(assignments);
            Enrollment = new FakeEnrollment((StudentA, CourseOib), (StudentB, CourseOib), (NoGroup, CourseOib));
            Service = new VisibleAssignmentsService(Repository, Enrollment, groups, Log);
        }

        public FakeAssignmentRepository Repository { get; }
        public FakeEnrollment Enrollment { get; }
        public RecordingVisibilityLog Log { get; } = new();
        public VisibleAssignmentsService Service { get; }

        private static InMemoryGroupMembership DefaultGroups()
        {
            var groups = new InMemoryGroupMembership();
            groups.Add(CourseOib, GroupA, StudentA);
            groups.Add(CourseOib, GroupB, StudentB);
            return groups;
        }
    }

    private sealed class FakeAssignmentRepository(Assignment[] assignments) : IAssignmentRepository
    {
        public int Calls { get; private set; }

        public ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
            IReadOnlyCollection<string> courseIds,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            IReadOnlyList<Assignment> result = assignments.Where(a => courseIds.Contains(a.CourseId)).ToList();
            return ValueTask.FromResult(result);
        }

        public ValueTask<Assignment?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask AddAsync(Assignment assignment, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask UpdateAsync(Assignment assignment, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeEnrollment(params (string StudentId, string CourseId)[] enrolled) : ICourseEnrollmentCheck
    {
        public int Calls { get; private set; }

        public ValueTask<bool> IsActiveStudentAsync(
            string studentId,
            string courseId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(enrolled.Contains((studentId, courseId)));
        }
    }

    private sealed class ThrowingGroupMembership : IGroupMembership
    {
        public ValueTask<bool> IsGroupMemberAsync(
            string studentId,
            string courseId,
            string groupId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("membership store unavailable");
    }

    private sealed record HiddenEntry(string StudentId, Guid AssignmentId, string Code);

    private sealed class RecordingVisibilityLog : IAssignmentVisibilityLog
    {
        public List<HiddenEntry> Entries { get; } = new();

        public ValueTask RecordHiddenAsync(
            string studentId,
            Guid assignmentId,
            string reasonCode,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(new HiddenEntry(studentId, assignmentId, reasonCode));
            return ValueTask.CompletedTask;
        }
    }
}
