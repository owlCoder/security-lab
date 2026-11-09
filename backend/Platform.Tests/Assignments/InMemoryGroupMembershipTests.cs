using Platform.Assignments.Infrastructure;

namespace Platform.Tests.Assignments;

[TestFixture]
public sealed class InMemoryGroupMembershipTests
{
    private const string Course = "course-1";
    private const string Group = "group-a";
    private const string Student = "student-1";

    [Test]
    public async Task IsGroupMember_WhenStudentWasAddedToGroup_ReturnsTrue()
    {
        var membership = new InMemoryGroupMembership();
        membership.Add(Course, Group, Student);

        Assert.That(await membership.IsGroupMemberAsync(Student, Course, Group), Is.True);
    }

    [Test]
    public async Task IsGroupMember_ForAnotherGroupOnSameCourse_ReturnsFalse()
    {
        var membership = new InMemoryGroupMembership();
        membership.Add(Course, Group, Student);

        Assert.That(await membership.IsGroupMemberAsync(Student, Course, "group-b"), Is.False);
    }

    [Test]
    public async Task IsGroupMember_ForSameGroupOnAnotherCourse_ReturnsFalse()
    {
        var membership = new InMemoryGroupMembership();
        membership.Add(Course, Group, Student);

        Assert.That(await membership.IsGroupMemberAsync(Student, "course-2", Group), Is.False);
    }

    [Test]
    public async Task IsGroupMember_ForAnotherStudent_ReturnsFalse()
    {
        var membership = new InMemoryGroupMembership();
        membership.Add(Course, Group, Student);

        Assert.That(await membership.IsGroupMemberAsync("student-2", Course, Group), Is.False);
    }

    [TestCase(null, Course, Group)]
    [TestCase(Student, "", Group)]
    [TestCase(Student, Course, " ")]
    public async Task IsGroupMember_WithMissingValue_ReturnsFalse(
        string? studentId,
        string courseId,
        string groupId)
    {
        var membership = new InMemoryGroupMembership();

        Assert.That(
            await membership.IsGroupMemberAsync(studentId!, courseId, groupId),
            Is.False);
    }

    [TestCase("")]
    [TestCase("student 1")]
    [TestCase("student-1\n")]
    public void Add_WithInvalidStudentId_Throws(string studentId)
    {
        var membership = new InMemoryGroupMembership();

        Assert.Throws<ArgumentException>(() => membership.Add(Course, Group, studentId));
    }

    [TestCase("", Group)]
    [TestCase(Course, " ")]
    public void Add_WithMissingCourseOrGroup_Throws(string courseId, string groupId)
    {
        var membership = new InMemoryGroupMembership();

        Assert.Throws<ArgumentException>(() => membership.Add(courseId, groupId, Student));
    }
}