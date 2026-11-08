
using Platform.Membership.Domain;

namespace Platform.Membership.Tests.Domain;

// ACC-07-1: pravila samog zapisa o delegaciji.
[TestFixture]
public sealed class CourseDelegationTests
{
    private const string Action = "course.member.enroll";
    private static readonly Guid Course = Guid.NewGuid();
    private static readonly Guid Assistant = Guid.NewGuid();
    private static readonly Guid Teacher = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void Grant_records_who_what_and_until_when()
    {
        var delegation = CourseDelegation.Grant(Course, Assistant, Action, Teacher, Now, Now.AddDays(7));

        Assert.That(delegation.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(delegation.CourseId, Is.EqualTo(Course));
        Assert.That(delegation.GranteeId, Is.EqualTo(Assistant));
        Assert.That(delegation.Action, Is.EqualTo(Action));
        Assert.That(delegation.GrantedBy, Is.EqualTo(Teacher));
        Assert.That(delegation.GrantedAt, Is.EqualTo(Now));
        Assert.That(delegation.ExpiresAt, Is.EqualTo(Now.AddDays(7)));
        Assert.That(delegation.RevokedAt, Is.Null);
        Assert.That(delegation.StateAt(Now), Is.EqualTo(DelegationState.Active));
    }

    [Test]
    public void Times_are_stored_in_utc()
    {
        var local = new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.FromHours(1));

        var delegation = CourseDelegation.Grant(Course, Assistant, Action, Teacher, local, local.AddHours(1));

        Assert.That(delegation.GrantedAt.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(delegation.ExpiresAt.Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That(delegation.GrantedAt, Is.EqualTo(local));
    }

    [Test]
    public void Grant_rejects_missing_ids_and_delegating_to_oneself()
    {
        Assert.Throws<ArgumentException>(() =>
            CourseDelegation.Grant(Guid.Empty, Assistant, Action, Teacher, Now, Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() =>
            CourseDelegation.Grant(Course, Guid.Empty, Action, Teacher, Now, Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() =>
            CourseDelegation.Grant(Course, Assistant, Action, Guid.Empty, Now, Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() =>
            CourseDelegation.Grant(Course, Teacher, Action, Teacher, Now, Now.AddDays(1)));
    }

    [Test]
    public void Grant_rejects_missing_action()
    {
        Assert.Throws<ArgumentException>(() =>
            CourseDelegation.Grant(Course, Assistant, " ", Teacher, Now, Now.AddDays(1)));
        Assert.Throws<ArgumentException>(() =>
            CourseDelegation.Grant(Course, Assistant, new string('a', 65), Teacher, Now, Now.AddDays(1)));
    }

    [Test]
    public void Grant_rejects_expiry_that_is_not_in_the_future()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CourseDelegation.Grant(Course, Assistant, Action, Teacher, Now, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CourseDelegation.Grant(Course, Assistant, Action, Teacher, Now, Now.AddMinutes(-1)));
    }

    [Test]
    public void Delegation_expires_exactly_at_expires_at()
    {
        var delegation = CourseDelegation.Grant(Course, Assistant, Action, Teacher, Now, Now.AddHours(1));

        Assert.That(delegation.IsActiveAt(Now.AddMinutes(59)), Is.True);
        Assert.That(delegation.StateAt(Now.AddHours(1)), Is.EqualTo(DelegationState.Expired));
        Assert.That(delegation.IsActiveAt(Now.AddDays(1)), Is.False);
    }

    [Test]
    public void Revoke_keeps_the_record_with_who_and_when()
    {
        var delegation = CourseDelegation.Grant(Course, Assistant, Action, Teacher, Now, Now.AddDays(7));

        delegation.Revoke(Teacher, Now.AddDays(1));

        Assert.That(delegation.StateAt(Now.AddDays(1)), Is.EqualTo(DelegationState.Revoked));
        Assert.That(delegation.RevokedAt, Is.EqualTo(Now.AddDays(1)));
        Assert.That(delegation.RevokedBy, Is.EqualTo(Teacher));
        Assert.That(delegation.GrantedBy, Is.EqualTo(Teacher));
        // Opozvana ostaje opozvana i posle roka; istorija se ne menja.
        Assert.That(delegation.StateAt(Now.AddDays(30)), Is.EqualTo(DelegationState.Revoked));
    }

    [Test]
    public void Revoked_or_expired_delegation_cannot_be_revoked()
    {
        var revoked = CourseDelegation.Grant(Course, Assistant, Action, Teacher, Now, Now.AddDays(7));
        revoked.Revoke(Teacher, Now);
        var expired = CourseDelegation.Grant(Course, Assistant, Action, Teacher, Now, Now.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => revoked.Revoke(Teacher, Now));
        Assert.Throws<InvalidOperationException>(() => expired.Revoke(Teacher, Now.AddHours(2)));
    }
}
