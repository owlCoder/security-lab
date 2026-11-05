using Platform.Assignments.Domain.Models;

namespace Platform.Tests.Assignments;

// ASN-06 TIM 15
[TestFixture]
public sealed class StudentDeadlineOverrideTests
{
    private static readonly DateTimeOffset Previous = new(2026, 11, 9, 23, 59, 59, TimeSpan.Zero);

    [Test]
    public void Create_WithValidValues_KeepsPreviousDeadlineAndActor()
    {
        var deadlineOverride = new StudentDeadlineOverride(
            Guid.NewGuid(),
            "student-1",
            Previous.AddDays(2),
            Previous,
            "teacher-1",
            Previous.AddDays(-1));

        Assert.Multiple(() =>
        {
            Assert.That(deadlineOverride.DueAtUtc, Is.EqualTo(Previous.AddDays(2)));
            Assert.That(deadlineOverride.PreviousDueAtUtc, Is.EqualTo(Previous));
            Assert.That(deadlineOverride.ExtendedById, Is.EqualTo("teacher-1"));
        });
    }

    [Test]
    public void Create_WhenDeadlineIsNotLaterThanPrevious_Throws()
    {
        Assert.That(
            () => new StudentDeadlineOverride(
                Guid.NewGuid(), "student-1", Previous, Previous, "teacher-1", Previous),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Create_WithEmptyAssignmentId_Throws()
    {
        Assert.That(
            () => new StudentDeadlineOverride(
                Guid.Empty, "student-1", Previous.AddDays(1), Previous, "teacher-1", Previous),
            Throws.TypeOf<ArgumentException>());
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("student 1")]
    [TestCase("student-1\n")]
    [TestCase("stud'; DROP TABLE x;--")]
    public void Create_WithInvalidStudentId_Throws(string studentId)
    {
        Assert.That(
            () => new StudentDeadlineOverride(
                Guid.NewGuid(), studentId, Previous.AddDays(1), Previous, "teacher-1", Previous),
            Throws.TypeOf<ArgumentException>());
    }
}
