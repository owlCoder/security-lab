
using Platform.Membership.Domain;

namespace Platform.Membership.Tests.Domain;

// ACC-02-2: pravila samog zapisa o članstvu.
[TestFixture]
public sealed class CourseMemberTests
{
    private static readonly Guid Course = Guid.NewGuid();
    private static readonly Guid Student = Guid.NewGuid();
    private static readonly Guid Teacher = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void Enroll_creates_active_membership_with_who_and_when()
    {
        var member = CourseMember.Enroll(Course, Student, MemberRole.Student, Teacher, Now);

        Assert.That(member.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(member.CourseId, Is.EqualTo(Course));
        Assert.That(member.AccountId, Is.EqualTo(Student));
        Assert.That(member.Role, Is.EqualTo(MemberRole.Student));
        Assert.That(member.Status, Is.EqualTo(MemberStatus.Active));
        Assert.That(member.EnrolledAt, Is.EqualTo(Now));
        Assert.That(member.EnrolledBy, Is.EqualTo(Teacher));
        Assert.That(member.LeftAt, Is.Null);
    }

    [Test]
    public void Enroll_stores_time_in_utc()
    {
        var local = new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.FromHours(1));

        var member = CourseMember.Enroll(Course, Student, MemberRole.Student, Teacher, local);

        Assert.That(member.EnrolledAt.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(member.EnrolledAt, Is.EqualTo(local));
    }

    [Test]
    public void Enroll_rejects_missing_course_account_or_actor()
    {
        Assert.Throws<ArgumentException>(() =>
            CourseMember.Enroll(Guid.Empty, Student, MemberRole.Student, Teacher, Now));
        Assert.Throws<ArgumentException>(() =>
            CourseMember.Enroll(Course, Guid.Empty, MemberRole.Student, Teacher, Now));
        Assert.Throws<ArgumentException>(() =>
            CourseMember.Enroll(Course, Student, MemberRole.Student, Guid.Empty, Now));
    }

    [Test]
    public void Enroll_rejects_unknown_role()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CourseMember.Enroll(Course, Student, (MemberRole)42, Teacher, Now));
    }

    [Test]
    public void Leave_keeps_the_record_and_marks_it_as_left()
    {
        var member = CourseMember.Enroll(Course, Student, MemberRole.Student, Teacher, Now);

        member.Leave(Teacher, Now.AddDays(1));

        Assert.That(member.Status, Is.EqualTo(MemberStatus.Left));
        Assert.That(member.LeftAt, Is.EqualTo(Now.AddDays(1)));
        Assert.That(member.LeftBy, Is.EqualTo(Teacher));
        Assert.That(member.EnrolledBy, Is.EqualTo(Teacher));
    }

    [Test]
    public void Leave_twice_is_rejected()
    {
        var member = CourseMember.Enroll(Course, Student, MemberRole.Student, Teacher, Now);
        member.Leave(Teacher, Now);

        Assert.Throws<InvalidOperationException>(() => member.Leave(Teacher, Now));
    }
}
