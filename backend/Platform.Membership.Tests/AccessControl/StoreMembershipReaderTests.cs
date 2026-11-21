using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Infrastructure.Resources;
using Platform.Membership.Domain;
using Platform.Membership.Infrastructure.AccessControl;
using Platform.Membership.Infrastructure.Courses;
using Platform.Membership.Tests.Testing;

namespace Platform.Membership.Tests.AccessControl;

// ACC-02-5: provera pristupa Tima 3 zna ko je upisan u kurs.
[TestFixture]
public sealed class StoreMembershipReaderTests
{
    private static readonly Guid Student = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Outsider = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Teacher = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Course = DevelopmentCourses.SecurityLab;
    private static readonly ResourceRef Lab = new("laboratory", "lab-1");

    private InMemoryMembershipStore store = null!;
    private StoreMembershipReader reader = null!;
    private IAccessDecision access = null!;

    [SetUp]
    public void Wire()
    {
        store = new InMemoryMembershipStore();
        store.Seed(Course, Student, MemberRole.Student);
        store.Seed(Course, Teacher, MemberRole.Teacher);
        reader = new StoreMembershipReader(store);

        // Pravi delovi Tima 3: katalog akcija, politika po ulozi u kursu i podrazumevano odbijanje.
        var resources = new InMemoryResourceDescriptorResolver();
        resources.SetCourse(Lab, Course);
        var registry = new InMemoryAccessPolicyRegistry();
        foreach (var definition in CourseRolePolicies.All)
        {
            registry.Register(
                definition.Action.Id,
                new CourseRoleAccessPolicy(definition, reader, resources));
        }

        access = new DefaultDenyAccessDecision(
            new InMemoryActionCatalog(),
            registry,
            new NoAudit(),
            new AccessDecisionOptions());
    }

    [Test]
    public async Task Active_member_is_returned_with_the_same_role()
    {
        var membership = await reader.GetAsync(Teacher, Course);

        Assert.That(membership, Is.Not.Null);
        Assert.That(membership!.Role, Is.EqualTo(CourseRole.Teacher));
        Assert.That(membership.Status, Is.EqualTo(MembershipStatus.Active));
        Assert.That(membership.DelegatedActions, Is.Empty);
    }

    [Test]
    public async Task Not_enrolled_account_has_no_membership()
    {
        Assert.That(await reader.GetAsync(Outsider, Course), Is.Null);
    }

    [Test]
    public async Task Enrolled_student_can_open_a_lab_of_the_course()
    {
        var decision = await LabRead(Student);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task Student_who_is_not_enrolled_cannot_open_the_lab()
    {
        var decision = await LabRead(Outsider);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Student_who_left_loses_access_on_the_next_request()
    {
        var before = await LabRead(Student);
        store.All.Single(m => m.AccountId == Student).Leave(Teacher, DateTimeOffset.UtcNow);

        var after = await LabRead(Student);

        Assert.That(before.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(after.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    private ValueTask<AccessDecision> LabRead(Guid accountId) =>
        access.DecideAsync(new AccessRequest(
            new SubjectRef(accountId.ToString("D"), AccountState.Active),
            AccessActions.LabRead.Id,
            Lab,
            "corr"));

    // Tim 3 je obrisao NullAccessAuditWriter, pa test ima svoj prazan audit.
    private sealed class NoAudit : IAccessAuditWriter
    {
        public ValueTask WriteAsync(
            AccessRequest request,
            AccessDecision decision,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}