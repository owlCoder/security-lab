using Platform.Access.Domain.Constants;
using Platform.Audit.Contracts;
using Platform.Membership.Application;
using Platform.Membership.Domain;

namespace Platform.Membership.Tests.Delegations;

// ACC-07-1: lista radnji koje smeju da se delegiraju, audit događaji i zapis o isteku.
[TestFixture]
public sealed class DelegationFoundationTests
{
    private static readonly Guid Course = Guid.NewGuid();
    private static readonly Guid Assistant = Guid.NewGuid();
    private static readonly Guid Teacher = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    [TestCase("lab.read")]
    [TestCase("attempt.read.course")]
    [TestCase("attempt.review")]
    [TestCase("audit.events.read")]
    public void Assistant_actions_supported_by_Access_and_Audit_can_be_delegated(string action)
    {
        Assert.That(DelegationActions.IsDelegable(action), Is.True);
    }

    [TestCase("course.member.enroll")]
    [TestCase("course.delegation.grant")]
    [TestCase("lab.draft.manage")]
    [TestCase("")]
    [TestCase(null)]
    public void Other_actions_cannot_be_delegated(string? action)
    {
        Assert.That(DelegationActions.IsDelegable(action), Is.False);
    }

    [Test]
    public void Delegable_actions_exist_in_the_Access_catalog()
    {
        var known = AccessActions.All.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        Assert.That(DelegationActions.Delegable, Is.SubsetOf(known));
    }

    [Test]
    public void Audit_event_names_the_course_the_assistant_and_the_action()
    {
        var delegation = Grant(Now.AddDays(7));

        var draft = DelegationAuditEvents.Granted(delegation);

        Assert.That(draft.EventType, Is.EqualTo(AuditEventTypes.CourseDelegationGranted));
        Assert.That(draft.Outcome, Is.EqualTo(AuditOutcome.Success));
        Assert.That(draft.CourseId, Is.EqualTo(Course));
        var assistant = new AuditResourceRef(AuditResourceTypes.Account, Assistant.ToString("D"));
        Assert.That(draft.Resource, Is.EqualTo(assistant));
        Assert.That(draft.Details!["action"], Is.EqualTo("lab.read"));
        Assert.That(draft.Details["delegationId"], Is.EqualTo(delegation.Id.ToString("D")));
    }

    [Test]
    public void Expiry_can_be_recorded_once_and_only_after_expiry()
    {
        var delegation = Grant(Now.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => delegation.MarkExpiryRecorded(Now));

        delegation.MarkExpiryRecorded(Now.AddHours(2));

        Assert.That(delegation.ExpiryRecordedAt, Is.EqualTo(Now.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => delegation.MarkExpiryRecorded(Now.AddHours(3)));
    }

    [Test]
    public void Revoked_delegation_has_no_expiry_record()
    {
        var delegation = Grant(Now.AddHours(1));
        delegation.Revoke(Teacher, Now);

        Assert.Throws<InvalidOperationException>(() => delegation.MarkExpiryRecorded(Now.AddHours(2)));
    }

    private static CourseDelegation Grant(DateTimeOffset expiresAt) =>
        CourseDelegation.Grant(Course, Assistant, "lab.read", Teacher, Now, expiresAt);
}
