using Microsoft.Extensions.Configuration;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Tests.Testing;
using Platform.Membership.Infrastructure.Courses;
using Platform.Membership.Contracts;

namespace Platform.Membership.Tests.Application;

[TestFixture]
public sealed class MembershipAuthorizationTests
{
    private static readonly Guid CourseA = Guid.NewGuid();
    private static readonly Guid CourseB = Guid.NewGuid();
    private static readonly Guid Teacher = Guid.NewGuid();
    private static readonly Guid Student = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private InMemoryMembershipStore store = null!;
    private EnrollCourseMemberPolicy policy = null!;

    [SetUp]
    public void SetUp()
    {
        store = new InMemoryMembershipStore();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:AdminAccountIds:0"] = Admin.ToString()
            })
            .Build();

        policy = new EnrollCourseMemberPolicy(configuration, store);
    }


private MembershipService CreateMembershipService()
{
    return new MembershipService(
        TestAccess.WithEnrollPolicy(policy),
        new DevelopmentCourseReader(),
        new FakeAccountStatusReader()
            .With(Student, AccountState.Active),
        store,
        new FixedTimeProvider(DateTimeOffset.UtcNow));
}

[Test]
public async Task Student_cannot_enroll_another_student_through_service()
{
    store.Seed(CourseA, Student, MemberRole.Student);

    var service = CreateMembershipService();

    var result = await service.EnrollStudentAsync(
        TestAccess.Subject(Student),
        CourseA,
        Guid.NewGuid(),
        "test-correlation");

    Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.Forbidden));
    Assert.That(store.All, Has.Count.EqualTo(1));
}

[Test]
public async Task Teacher_of_another_course_cannot_enroll_through_service()
{
    store.Seed(CourseB, Teacher, MemberRole.Teacher);

    var service = CreateMembershipService();

    var result = await service.EnrollStudentAsync(
        TestAccess.Subject(Teacher),
        CourseA,
        Student,
        "test-correlation");

    Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.Forbidden));
    Assert.That(store.All, Has.Count.EqualTo(1));
}

[Test]
public async Task Unauthenticated_caller_cannot_enroll_through_service()
{
    var service = CreateMembershipService();

    var result = await service.EnrollStudentAsync(
        null,
        CourseA,
        Student,
        "test-correlation");

    Assert.That(result.Outcome, Is.EqualTo(EnrollmentOutcome.Unauthenticated));
    Assert.That(store.All, Is.Empty);
}


    private static AccessRequest Request(
        Guid? caller,
        Guid courseId,
        AccountState state = AccountState.Active,
        string action = MembershipActions.EnrollMember)
    {
        return new AccessRequest(
            caller is null
                ? null
                : new SubjectRef(caller.Value.ToString(), state),
            action,
            new ResourceRef(
                MembershipActions.CourseResourceType,
                courseId.ToString()),
            "test-correlation");
    }

    [Test]
    public async Task Teacher_of_own_course_is_allowed()
    {
        store.Seed(CourseA, Teacher, MemberRole.Teacher);

        var allowed = await policy.EvaluateAsync(
            Request(Teacher, CourseA));

        Assert.That(allowed, Is.True);
    }

    [Test]
    public async Task Administrator_is_allowed()
    {
        var allowed = await policy.EvaluateAsync(
            Request(Admin, CourseA));

        Assert.That(allowed, Is.True);
    }

    [Test]
    public async Task Student_cannot_enroll_anyone()
    {
        store.Seed(CourseA, Student, MemberRole.Student);

        var allowed = await policy.EvaluateAsync(
            Request(Student, CourseA));

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task Teacher_of_another_course_is_denied()
    {
        store.Seed(CourseB, Teacher, MemberRole.Teacher);

        var allowed = await policy.EvaluateAsync(
            Request(Teacher, CourseA));

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task Teacher_who_left_course_is_denied()
    {
        store.Seed(CourseA, Teacher, MemberRole.Teacher);

        var membership = store.All.Single();
        membership.Leave(Teacher, DateTimeOffset.UtcNow);

        var allowed = await policy.EvaluateAsync(
            Request(Teacher, CourseA));

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task Unauthenticated_user_is_denied()
    {
        var allowed = await policy.EvaluateAsync(
            Request(null, CourseA));

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task Blocked_administrator_is_denied()
    {
        var allowed = await policy.EvaluateAsync(
            Request(Admin, CourseA, AccountState.Blocked));

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task Wrong_action_is_denied()
    {
        store.Seed(CourseA, Teacher, MemberRole.Teacher);

        var allowed = await policy.EvaluateAsync(
            Request(Teacher, CourseA, action: "course.delete"));

        Assert.That(allowed, Is.False);
    }

    [Test]
    public async Task Invalid_resource_is_denied()
    {
        var request = new AccessRequest(
            new SubjectRef(Admin.ToString(), AccountState.Active),
            MembershipActions.EnrollMember,
            new ResourceRef("laboratory", CourseA.ToString()),
            "test-correlation");

        var allowed = await policy.EvaluateAsync(request);

        Assert.That(allowed, Is.False);
    }
}
