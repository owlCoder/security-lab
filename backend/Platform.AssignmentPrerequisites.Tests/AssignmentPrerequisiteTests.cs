using Platform.AssignmentPrerequisites.Domain;
using static Platform.AssignmentPrerequisites.Tests.Support.TestData;

namespace Platform.AssignmentPrerequisites.Tests;

[TestFixture]
public class AssignmentPrerequisiteTests
{
    [Test]
    public void ValidPrerequisite_KeepsAllValues()
    {
        var prerequisite = new AssignmentPrerequisite(
            Course, AssignmentB, AssignmentA, CompletionRequirement.PassedOrPartial, Teacher, Now);

        Assert.Multiple(() =>
        {
            Assert.That(prerequisite.CourseId, Is.EqualTo(Course));
            Assert.That(prerequisite.AssignmentId, Is.EqualTo(AssignmentB));
            Assert.That(prerequisite.RequiredAssignmentId, Is.EqualTo(AssignmentA));
            Assert.That(prerequisite.CompletionRequirement, Is.EqualTo(CompletionRequirement.PassedOrPartial));
            Assert.That(prerequisite.CreatedById, Is.EqualTo(Teacher));
            Assert.That(prerequisite.CreatedAtUtc, Is.EqualTo(Now));
        });
    }

    [Test]
    public void AssignmentCannotRequireItself()
    {
        Assert.Throws<ArgumentException>(() => Requires(AssignmentA, AssignmentA));
    }

    [Test]
    public void EmptyAssignmentIds_AreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => Requires(Guid.Empty, AssignmentA));
            Assert.Throws<ArgumentException>(() => Requires(AssignmentA, Guid.Empty));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("course oib")]
    [TestCase("course-oib'; DROP TABLE x;--")]
    [TestCase("course-oib\n")]
    public void InvalidCourseId_IsRejected(string? courseId)
    {
        Assert.Throws<ArgumentException>(() => new AssignmentPrerequisite(
            courseId!, AssignmentB, AssignmentA, CompletionRequirement.Passed, Teacher, Now));
    }

    [Test]
    public void TooLongCourseId_IsRejected()
    {
        string courseId = new('c', Identifiers.MaxLength + 1);

        Assert.Throws<ArgumentException>(() => Requires(AssignmentB, AssignmentA, course: courseId));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("teacher@example.com")]
    public void InvalidCreatorId_IsRejected(string? creatorId)
    {
        Assert.Throws<ArgumentException>(() => new AssignmentPrerequisite(
            Course, AssignmentB, AssignmentA, CompletionRequirement.Passed, creatorId!, Now));
    }

    [Test]
    public void UnknownCompletionRequirement_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AssignmentPrerequisite(
            Course, AssignmentB, AssignmentA, (CompletionRequirement)99, Teacher, Now));
    }

    [Test]
    public void CreationTime_IsStoredInUtc()
    {
        var local = new DateTimeOffset(2026, 11, 8, 13, 0, 0, TimeSpan.FromHours(1));

        var prerequisite = new AssignmentPrerequisite(
            Course, AssignmentB, AssignmentA, CompletionRequirement.Passed, Teacher, local);

        Assert.That(prerequisite.CreatedAtUtc.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(prerequisite.CreatedAtUtc, Is.EqualTo(local));
    }
}
