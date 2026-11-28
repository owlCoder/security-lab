
using Platform.Audit.Contracts;
using Platform.Membership.Application;
using Platform.Membership.Domain;
using Platform.Membership.Tests.Testing;

namespace Platform.Membership.Tests.Delegations;

// ACC-07-5: opoziv i pregled delegacija.
[TestFixture]
public sealed class RevokeAndListDelegationTests
{
    private static readonly Guid Course = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherCourse = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Teacher = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Assistant = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private InMemoryDelegationStore delegations = null!;
    private CourseDelegation active = null!;

    [SetUp]
    public void Seed()
    {
        delegations = new InMemoryDelegationStore();
        active = CourseDelegation.Grant(Course, Assistant, "lab.read", Teacher, Now.AddHours(-1), Now.AddDays(1));
        delegations.Seed(active);
    }

    [Test]
    public async Task Teacher_revokes_an_active_delegation_and_it_is_audited()
    {
        var result = await Revoke(active.Id);

        Assert.That(result.Outcome, Is.EqualTo(DelegationOutcome.Revoked));
        Assert.That(active.StateAt(Now), Is.EqualTo(DelegationState.Revoked));
        Assert.That(active.RevokedBy, Is.EqualTo(Teacher));
        var audit = delegations.Audits.Single();
        Assert.That(audit.EventType, Is.EqualTo(AuditEventTypes.CourseDelegationRevoked));
        Assert.That(audit.Context.Actor, Is.EqualTo(AuditActor.ForUser(Teacher)));
    }

    [Test]
    public async Task Second_revoke_is_rejected()
    {
        await Revoke(active.Id);

        var second = await Revoke(active.Id);

        Assert.That(second.Outcome, Is.EqualTo(DelegationOutcome.NotActive));
    }

    [Test]
    public async Task Expired_delegation_cannot_be_revoked()
    {
        var expired = CourseDelegation.Grant(
            Course, Assistant, "attempt.review", Teacher, Now.AddDays(-2), Now.AddDays(-1));
        delegations.Seed(expired);

        var result = await Revoke(expired.Id);

        Assert.That(result.Outcome, Is.EqualTo(DelegationOutcome.NotActive));
    }

    [Test]
    public async Task Delegation_of_another_course_looks_like_a_missing_one()
    {
        var foreign = CourseDelegation.Grant(
            OtherCourse, Assistant, "lab.read", Teacher, Now.AddHours(-1), Now.AddDays(1));
        delegations.Seed(foreign);

        var otherCourse = await Revoke(foreign.Id);
        var missing = await Revoke(Guid.NewGuid());

        Assert.That(otherCourse.Outcome, Is.EqualTo(DelegationOutcome.NotFound));
        Assert.That(missing.Outcome, Is.EqualTo(DelegationOutcome.NotFound));
        Assert.That(foreign.RevokedAt, Is.Null);
    }

    [Test]
    public async Task Without_an_audit_record_nothing_is_revoked()
    {
        delegations.SimulateAuditFailure = true;

        var result = await Revoke(active.Id);

        Assert.That(result.Outcome, Is.EqualTo(DelegationOutcome.Unavailable));
        Assert.That(active.RevokedAt, Is.Null);
    }

    [Test]
    public async Task Without_policy_revoke_is_denied()
    {
        var service = new RevokeDelegationService(
            TestAccess.WithEnrollPolicy(null), delegations, new FixedTimeProvider(Now));

        var result = await service.RevokeAsync(TestAccess.Subject(Teacher), Course, active.Id, "corr");

        Assert.That(result.Outcome, Is.EqualTo(DelegationOutcome.Forbidden));
        Assert.That(active.RevokedAt, Is.Null);
    }

    [Test]
    public async Task List_returns_only_delegations_of_the_course_with_history()
    {
        var revoked = CourseDelegation.Grant(
            Course, Assistant, "attempt.review", Teacher, Now.AddDays(-3), Now.AddDays(3));
        revoked.Revoke(Teacher, Now.AddDays(-2));
        delegations.Seed(revoked);
        delegations.Seed(CourseDelegation.Grant(OtherCourse, Assistant, "lab.read", Teacher, Now, Now.AddDays(1)));
        var service = new ListDelegationsService(
            DelegationTestAccess.AllowAll(DelegationActions.Read), delegations);

        var result = await service.ListAsync(TestAccess.Subject(Teacher), Course, "corr");

        Assert.That(result.Outcome, Is.EqualTo(DelegationOutcome.Listed));
        Assert.That(result.Delegations!.Select(d => d.Id), Is.EquivalentTo(new[] { active.Id, revoked.Id }));
    }

    [Test]
    public async Task List_without_policy_is_denied()
    {
        var service = new ListDelegationsService(TestAccess.WithEnrollPolicy(null), delegations);

        var result = await service.ListAsync(TestAccess.Subject(Teacher), Course, "corr");

        Assert.That(result.Outcome, Is.EqualTo(DelegationOutcome.Forbidden));
        Assert.That(result.Delegations, Is.Null);
    }

    private ValueTask<DelegationResult> Revoke(Guid delegationId) =>
        new RevokeDelegationService(
                DelegationTestAccess.AllowAll(DelegationActions.Revoke),
                delegations,
                new FixedTimeProvider(Now))
            .RevokeAsync(TestAccess.Subject(Teacher), Course, delegationId, "corr");
}
