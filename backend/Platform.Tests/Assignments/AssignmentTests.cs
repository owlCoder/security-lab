using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Domain.Models;

namespace Platform.Tests.Assignments;

// KT1 TIM 15
[TestFixture]
public sealed class AssignmentTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Due = new(2026, 11, 9, 23, 59, 59, TimeSpan.Zero);

    [Test]
    public void Create_WithValidValues_CreatesActiveAssignment()
    {
        var assignment = CreateAssignment();

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Status, Is.EqualTo(AssignmentStatus.Active));
            Assert.That(assignment.MaxAttempts, Is.EqualTo(2));
            Assert.That(assignment.GetAvailability(Start), Is.EqualTo(AssignmentAvailability.Available));
        });
    }

    [Test]
    public void Create_WithZeroMaxAttempts_Throws()
    {
        Assert.That(
            () => new Assignment(
                Guid.NewGuid(),
                "course-oib",
                "revision-1",
                Start,
                Due,
                0,
                scope: AssignmentScope.Course),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Create_WhenAvailabilityBeginsAfterDueDate_Throws()
    {
        Assert.That(
            () => new Assignment(
                Guid.NewGuid(),
                "course-oib",
                "revision-1",
                Due.AddMinutes(1),
                Due,
                2,
                scope: AssignmentScope.Course),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void GetAvailability_AfterDueDate_ReturnsExpired()
    {
        var assignment = CreateAssignment();

        var availability = assignment.GetAvailability(Due.AddSeconds(1));

        Assert.That(availability, Is.EqualTo(AssignmentAvailability.Expired));
    }

    [Test]
    public void GetAvailability_AfterDueDate_WhenLateStartIsAllowed_ReturnsAvailable()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            Start,
            Due,
            2,
            allowStartAfterDeadline: true,
            scope: AssignmentScope.Course);

        var availability = assignment.GetAvailability(Due.AddSeconds(1));

        Assert.That(availability, Is.EqualTo(AssignmentAvailability.Available));
    }

    [Test]
    public void Create_WithGroupScope_PreservesScopeAndGroupId()
    {
        var assignment = new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            Start,
            Due,
            maxAttempts: 2,
            scope: AssignmentScope.Group,
            groupId: "group-a");

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Scope, Is.EqualTo(AssignmentScope.Group));
            Assert.That(assignment.GroupId, Is.EqualTo("group-a"));
        });
    }

    [Test]
    public void Create_WithCourseScope_HasNoGroup()
    {
        var assignment = CreateAssignment();

        Assert.Multiple(() =>
        {
            Assert.That(assignment.Scope, Is.EqualTo(AssignmentScope.Course));
            Assert.That(assignment.GroupId, Is.Null);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithGroupScopeAndMissingGroupId_Throws(string? groupId)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            Start,
            Due,
            maxAttempts: 2,
            scope: AssignmentScope.Group,
            groupId: groupId));

        Assert.That(exception!.ParamName, Is.EqualTo("groupId"));
    }

    [TestCase("group-a")]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithCourseScopeAndNonNullGroupId_Throws(string groupId)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            Start,
            Due,
            maxAttempts: 2,
            scope: AssignmentScope.Course,
            groupId: groupId));

        Assert.That(exception!.ParamName, Is.EqualTo("groupId"));
    }

    [TestCase(AssignmentScope.Unspecified)]
    [TestCase((AssignmentScope)99)]
    public void Create_WithInvalidScope_Throws(AssignmentScope scope)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Assignment(
            Guid.NewGuid(),
            "course-oib",
            "revision-1",
            Start,
            Due,
            maxAttempts: 2,
            scope: scope));

        Assert.That(exception!.ParamName, Is.EqualTo("scope"));
    }

    private static Assignment CreateAssignment() => new(
        Guid.NewGuid(),
        "course-oib",
        "revision-1",
        Start,
        Due,
        maxAttempts: 2,
        scope: AssignmentScope.Course);
}
