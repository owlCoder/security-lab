
using Microsoft.Extensions.Configuration;
using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Tests.Testing;

namespace Platform.Membership.Tests.Delegations;

// ACC-07-3: samo aktivni nastavnik tog kursa (ili administrator) upravlja delegacijama.
[TestFixture]
public sealed class DelegationAuthorizationTests
{
    private static readonly Guid Course = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherCourse = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Teacher = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherTeacher = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Assistant = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Student = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Admin = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private InMemoryMembershipStore members = null!;

    [SetUp]
    public void Seed()
    {
        members = new InMemoryMembershipStore();
        members.Seed(Course, Teacher, MemberRole.Teacher);
        members.Seed(OtherCourse, OtherTeacher, MemberRole.Teacher);
        members.Seed(Course, Assistant, MemberRole.Assistant);
        members.Seed(Course, Student, MemberRole.Student);
    }

    [TestCase(DelegationActions.Grant)]
    [TestCase(DelegationActions.Revoke)]
    [TestCase(DelegationActions.Read)]
    public async Task Teacher_of_the_course_is_allowed(string action)
    {
        Assert.That(
            await Decide(Access(), Teacher, action, Course),
            Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task Administrator_is_allowed()
    {
        var outcome = await Decide(Access(), Admin, DelegationActions.Grant, Course);

        Assert.That(outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task Assistant_cannot_grant_to_anyone_including_self()
    {
        var outcome = await Decide(Access(), Assistant, DelegationActions.Grant, Course);

        Assert.That(outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Student_is_denied()
    {
        var outcome = await Decide(Access(), Student, DelegationActions.Read, Course);

        Assert.That(outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Teacher_of_another_course_is_denied()
    {
        var outcome = await Decide(Access(), OtherTeacher, DelegationActions.Grant, Course);

        Assert.That(outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Teacher_who_left_the_course_is_denied_on_the_next_request()
    {
        var access = Access();

        members.All.Single(m => m.AccountId == Teacher && m.CourseId == Course)
            .Leave(Admin, DateTimeOffset.UtcNow);

        Assert.That(
            await Decide(access, Teacher, DelegationActions.Grant, Course),
            Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Blocked_teacher_is_denied()
    {
        var decision = await Access().DecideAsync(new AccessRequest(
            new SubjectRef(Teacher.ToString("D"), AccountState.Blocked),
            DelegationActions.Grant,
            new ResourceRef(DelegationActions.CourseResourceType, Course.ToString("D")),
            "corr"));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Without_registered_policy_everyone_is_denied_ACC05()
    {
        var access = new DefaultDenyAccessDecision(
            new InMemoryActionCatalog(),
            new InMemoryAccessPolicyRegistry(),
            new NoAuditWriter(),
            new AccessDecisionOptions());

        Assert.That(
            await Decide(access, Teacher, DelegationActions.Grant, Course),
            Is.EqualTo(AccessOutcome.Deny));
    }

    private IAccessDecision Access()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:AdminAccountIds:0"] = Admin.ToString()
            })
            .Build();

        var policy = new ManageCourseDelegationsPolicy(configuration, members);
        var catalog = new InMemoryActionCatalog();
        var registry = new InMemoryAccessPolicyRegistry();

        foreach (var (action, privileged) in new[]
        {
            (DelegationActions.Grant, true),
            (DelegationActions.Revoke, true),
            (DelegationActions.Read, false)
        })
        {
            catalog.Register(new ActionDefinition(
                action,
                ActionScope.Course,
                DelegationActions.CourseResourceType,
                privileged));

            registry.Register(action, policy);
        }

        return new DefaultDenyAccessDecision(
            catalog,
            registry,
            new NoAuditWriter(),
            new AccessDecisionOptions());
    }

    private static async Task<AccessOutcome> Decide(
        IAccessDecision access,
        Guid caller,
        string action,
        Guid course)
    {
        var decision = await access.DecideAsync(new AccessRequest(
            new SubjectRef(caller.ToString("D"), AccountState.Active),
            action,
            new ResourceRef(DelegationActions.CourseResourceType, course.ToString("D")),
            "corr"));

        return decision.Outcome;
    }
}
