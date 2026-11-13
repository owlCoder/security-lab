using Platform.Access.Domain.Enums;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Infrastructure.Courses;
using Platform.Membership.Tests.Testing;
namespace Platform.Membership.Tests.Application;
// ACC-02-3: poslovna pravila upisa. Ko sme da upisuje proverava MembershipAuthorizationTests (ACC-02-4);
// ovde je politika "dozvoli sve", da bi se testirala samo pravila o kursu, nalogu i duplikatu.
[TestFixture]
public sealed class MembershipServiceTests
{
    private static readonly Guid Teacher = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Student = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid BlockedStudent = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private InMemoryMembershipStore store = null!;
    private MembershipService service = null!;
    [SetUp]
    public void CreateService()
    {
        store = new InMemoryMembershipStore();
        service = CreateService(new AllowAllPolicy());
    }
    [Test]
    public async Task Student_is_enrolled_as_active_student()
    {
        var result = await Enroll(DevelopmentCourses.SecurityLab, Student);
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.Enrolled));
        Assert.That(result.Member!.Role, Is.EqualTo(MemberRole.Student));
        Assert.That(result.Member.Status, Is.EqualTo(MemberStatus.Active));
        Assert.That(result.Member.EnrolledBy, Is.EqualTo(Teacher));
        Assert.That(result.Member.EnrolledAt, Is.EqualTo(Now));
        Assert.That(store.All, Has.Count.EqualTo(1));
    }
    [Test]
    public async Task Same_student_cannot_be_enrolled_twice()
    {
        await Enroll(DevelopmentCourses.SecurityLab, Student);
        var second = await Enroll(DevelopmentCourses.SecurityLab, Student);
        Assert.That(second.Outcome, Is.EqualTo(EnrollmentOutcome.AlreadyEnrolled));
        Assert.That(store.All, Has.Count.EqualTo(1));
    }
    [Test]
    public async Task Duplicate_rejected_by_database_during_a_race_is_reported_as_already_enrolled()
    {
        store.SimulateConcurrentInsert = true;
        var result = await Enroll(DevelopmentCourses.SecurityLab, Student);
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.AlreadyEnrolled));
        Assert.That(result.Member, Is.Null);
    }
    [Test]
    public async Task Same_student_can_be_in_two_different_courses()
    {
        await Enroll(DevelopmentCourses.SecurityLab, Student);
        var other = await Enroll(DevelopmentCourses.OtherCourse, Student);
        Assert.That(other.Outcome, Is.EqualTo(EnrollmentOutcome.Enrolled));
        Assert.That(store.All, Has.Count.EqualTo(2));
    }
    [Test]
    public async Task Archived_course_accepts_no_new_students()
    {
        var result = await Enroll(DevelopmentCourses.ArchivedCourse, Student);
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.CourseArchived));
        Assert.That(store.All, Is.Empty);
    }
    [Test]
    public async Task Unknown_course_is_rejected()
    {
        var result = await Enroll(Guid.NewGuid(), Student);
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.CourseNotFound));
        Assert.That(store.All, Is.Empty);
    }
    [Test]
    public async Task Unknown_account_is_rejected()
    {
        var result = await Enroll(DevelopmentCourses.SecurityLab, Guid.NewGuid());
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.AccountNotFound));
        Assert.That(store.All, Is.Empty);
    }
    [Test]
    public async Task Blocked_account_is_rejected()
    {
        var result = await Enroll(DevelopmentCourses.SecurityLab, BlockedStudent);
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.AccountNotActive));
        Assert.That(store.All, Is.Empty);
    }
    [Test]
    public async Task Missing_account_id_is_invalid_input()
    {
        var result = await Enroll(DevelopmentCourses.SecurityLab, Guid.Empty);
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.InvalidInput));
        Assert.That(store.All, Is.Empty);
    }
    [Test]
    public async Task Without_a_registered_policy_nobody_can_enroll()
    {
        // ACC-05: nova akcija bez politike ne daje pristup automatski.
        var closed = CreateService(enrollPolicy: null);
        var result = await closed.EnrollStudentAsync(
        TestAccess.Subject(Teacher), DevelopmentCourses.SecurityLab, Student, "corr");
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.Forbidden));
        Assert.That(store.All, Is.Empty);
    }
    [Test]
    public async Task Without_a_caller_the_result_is_unauthenticated()
    {
        var result = await service.EnrollStudentAsync(null, DevelopmentCourses.SecurityLab, Student, "corr");
        Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.Unauthenticated));
        Assert.That(store.All, Is.Empty);
    }
    private MembershipService CreateService(Platform.Access.Contracts.IAccessPolicy? enrollPolicy) =>
    new(
    TestAccess.WithEnrollPolicy(enrollPolicy),
    new DevelopmentCourseReader(),
    new FakeAccountStatusReader()
    .With(Student, AccountState.Active)
    .With(BlockedStudent, AccountState.Blocked),
    store,
    new FixedTimeProvider(Now));
    private ValueTask<EnrollmentResult> Enroll(Guid courseId, Guid studentId) =>
    service.EnrollStudentAsync(TestAccess.Subject(Teacher), courseId, studentId, "corr");
}