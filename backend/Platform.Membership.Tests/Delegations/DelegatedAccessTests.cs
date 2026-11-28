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
using Platform.Membership.Tests.Testing;

namespace Platform.Membership.Tests.Delegations;

// ACC-07-7 (kriterijum specifikacije): asistent sa delegacijom ima pristup dok delegacija vazi,
// a po isteku ili opozivu pristup prestaje vec na sledecem zahtevu. Prava pravila Tima 3.
[TestFixture]
public sealed class DelegatedAccessTests
{
    private static readonly Guid Course = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherCourse = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Teacher = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Assistant = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Student = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Granted = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Expires = Granted.AddDays(7);
    private static readonly ResourceRef Lab = new("laboratory", "lab-1");
    private static readonly ResourceRef OtherLab = new("laboratory", "lab-2");
    private static readonly ResourceRef Attempt = new("attempt", "attempt-1");

    private InMemoryMembershipStore members = null!;
    private InMemoryDelegationStore delegations = null!;
    private MutableTimeProvider clock = null!;
    private IAccessDecision access = null!;

    [SetUp]
    public void Wire()
    {
        members = new InMemoryMembershipStore();
        members.Seed(Course, Teacher, MemberRole.Teacher);
        members.Seed(Course, Assistant, MemberRole.Assistant);
        members.Seed(OtherCourse, Assistant, MemberRole.Assistant);
        members.Seed(Course, Student, MemberRole.Student);
        delegations = new InMemoryDelegationStore();
        clock = new MutableTimeProvider(Granted);

        var reader = new StoreMembershipReader(members, delegations, clock);
        var resources = new InMemoryResourceDescriptorResolver();
        resources.SetCourse(Lab, Course);
        resources.SetCourse(OtherLab, OtherCourse);
        resources.SetCourse(Attempt, Course);
        var registry = new InMemoryAccessPolicyRegistry();
        foreach (var definition in CourseRolePolicies.All)
        {
            registry.Register(definition.Action.Id, new CourseRoleAccessPolicy(definition, reader, resources));
        }

        access = new DefaultDenyAccessDecision(
            new InMemoryActionCatalog(), registry, new NoAuditWriter(), new AccessDecisionOptions());
    }

    [Test]
    public async Task Assistant_without_delegation_has_no_access()
    {
        Assert.That(await LabRead(Assistant, Lab), Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Assistant_with_delegation_has_access_until_expiry_then_loses_it()
    {
        delegations.Seed(Delegation("lab.read"));

        clock.Now = Granted.AddMinutes(1);
        var during = await LabRead(Assistant, Lab);
        clock.Now = Expires.AddSeconds(-1);
        var lastSecond = await LabRead(Assistant, Lab);
        clock.Now = Expires;
        var atExpiry = await LabRead(Assistant, Lab);

        Assert.That(during, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(lastSecond, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(atExpiry, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Revoked_delegation_stops_access_on_the_next_request()
    {
        var delegation = Delegation("lab.read");
        delegations.Seed(delegation);
        clock.Now = Granted.AddHours(1);
        var before = await LabRead(Assistant, Lab);

        delegation.Revoke(Teacher, clock.Now);
        var after = await LabRead(Assistant, Lab);

        Assert.That(before, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(after, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Delegation_works_only_in_its_own_course()
    {
        delegations.Seed(Delegation("lab.read"));
        clock.Now = Granted.AddHours(1);

        Assert.That(await LabRead(Assistant, OtherLab), Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Delegation_of_one_action_does_not_give_another()
    {
        delegations.Seed(Delegation("lab.read"));
        clock.Now = Granted.AddHours(1);

        var review = await Decide(Assistant, AccessActions.AttemptReview.Id, Attempt);

        Assert.That(review, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Delegated_review_lets_the_assistant_review_attempts_of_the_course()
    {
        delegations.Seed(Delegation("attempt.review"));
        clock.Now = Granted.AddHours(1);

        var review = await Decide(Assistant, AccessActions.AttemptReview.Id, Attempt);

        Assert.That(review, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task Only_assistants_receive_delegated_actions()
    {
        delegations.Seed(CourseDelegation.Grant(Course, Student, "attempt.review", Teacher, Granted, Expires));
        clock.Now = Granted.AddHours(1);
        var reader = new StoreMembershipReader(members, delegations, clock);

        var membership = await reader.GetAsync(Student, Course);

        Assert.That(membership!.DelegatedActions, Is.Empty);
    }

    [Test]
    public async Task Assistant_who_left_the_course_loses_delegated_access()
    {
        delegations.Seed(Delegation("lab.read"));
        clock.Now = Granted.AddHours(1);
        members.All.Single(m => m.AccountId == Assistant && m.CourseId == Course).Leave(Teacher, clock.Now);

        Assert.That(await LabRead(Assistant, Lab), Is.EqualTo(AccessOutcome.Deny));
    }

    private static CourseDelegation Delegation(string action) =>
        CourseDelegation.Grant(Course, Assistant, action, Teacher, Granted, Expires);

    private Task<AccessOutcome> LabRead(Guid accountId, ResourceRef lab) =>
        Decide(accountId, AccessActions.LabRead.Id, lab);

    private async Task<AccessOutcome> Decide(Guid accountId, string action, ResourceRef resource)
    {
        var decision = await access.DecideAsync(new AccessRequest(
            new SubjectRef(accountId.ToString("D"), AccountState.Active), action, resource, "corr"));

        return decision.Outcome;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
