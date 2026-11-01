using NUnit.Framework;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Access.Demo;

namespace Platform.Access.Tests;

public sealed class AccessPolicyTests
{
    private readonly AccessPolicy policy = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static AccountSnapshot Ana => new(DemoData.Ana, true);
    private static MembershipSnapshot Member => new(DemoData.Ana, DemoData.Course, CourseRole.Student, true);
    private static AttemptSnapshot Resource => new(DemoData.AnaAttempt, DemoData.Ana,
        DemoData.Course, AttemptState.Submitted, 1, "open");
    private static ContextPolicy Context => new("open", "window:v1");
    private AccessVerdict Check(AccountSnapshot? account = null, MembershipSnapshot? member = null,
        AttemptSnapshot? resource = null, ContextPolicy? context = null, string action = AccessPolicy.ReadAttempt) =>
        policy.Evaluate(account ?? Ana, member ?? Member, resource ?? Resource,
            action, context ?? Context, Now);

    [Test] public void OwnerWithActiveMembershipCanRead() => Assert.That(Check().Allowed, Is.True);
    [Test] public void StudentCannotReadAnotherOwner() => Assert.That(
        Check(resource: Resource with { OwnerId = DemoData.Marko }).Allowed, Is.False);
    [Test] public void TeacherCanReviewSubmittedAttemptInOwnCourse() => Assert.That(
        Check(member: Member with { Role = CourseRole.Teacher },
            resource: Resource with { OwnerId = DemoData.Marko }).Allowed, Is.True);
    [Test] public void TeacherCannotReadAnotherCourse() => Assert.That(
        Check(member: Member with { Role = CourseRole.Teacher, CourseId = DemoData.OtherCourse }).Allowed, Is.False);
    [Test] public void TeacherCannotReadUnsubmittedAnswerOfAnotherOwner() => Assert.That(
        Check(member: Member with { Role = CourseRole.Teacher },
            resource: Resource with { OwnerId = DemoData.Marko, State = AttemptState.InProgress }).Allowed, Is.False);
    [TestCase(false, false)] [TestCase(true, true)]
    public void AssistantRequiresExplicitReviewDelegation(bool delegated, bool allowed) => Assert.That(
        Check(member: Member with { Role = CourseRole.Assistant, CanReview = delegated },
            resource: Resource with { OwnerId = DemoData.Marko }).Allowed, Is.EqualTo(allowed));
    [Test] public void BlockedOwnerIsDenied() => Assert.That(Check(account: Ana with { IsActive = false }).Allowed, Is.False);
    [Test] public void InactiveMembershipIsDenied() => Assert.That(Check(member: Member with { IsActive = false }).Allowed, Is.False);
    [Test] public void MembershipMustBelongToCaller() => Assert.That(Check(member: Member with { AccountId = DemoData.Marko }).Allowed, Is.False);
    [TestCase("UpdateAttempt")] [TestCase("DeleteAttempt")] [TestCase("")]
    public void UnknownActionDefaultsToDeny(string action) => Assert.That(Check(action: action).Allowed, Is.False);
    [Test] public void InvalidResourceSnapshotIsDenied() => Assert.That(Check(resource: Resource with { OwnerId = Guid.Empty }).Allowed, Is.False);
    [Test] public void UnknownCourseRoleIsDenied() => Assert.That(Check(member: Member with { Role = (CourseRole)999 }).Allowed, Is.False);
    [Test] public void MissingContextIsDenied() => Assert.That(
        policy.Evaluate(Ana, Member, Resource, AccessPolicy.ReadAttempt, null, Now).Allowed, Is.False);
    [Test] public void WrongContextIdentityIsDenied() => Assert.That(Check(context: Context with { Id = "other" }).Allowed, Is.False);
    [Test] public void UnversionedPolicyIsDenied() => Assert.That(Check(context: Context with { Version = "" }).Allowed, Is.False);
    [Test] public void ContextCannotGrantForeignResource() => Assert.That(
        Check(resource: Resource with { OwnerId = DemoData.Marko },
            context: Context with { NotBefore = Now.AddMinutes(-1), NotAfter = Now.AddMinutes(1) }).Allowed, Is.False);
    [Test] public void ExpiredContextDeniesEvenOwner() => Assert.That(
        Check(context: Context with { NotAfter = Now }).ReasonCode, Is.EqualTo("CONTEXT_EXPIRED"));
    [Test] public void FutureContextDeniesEvenTeacher() => Assert.That(
        Check(member: Member with { Role = CourseRole.Teacher },
            context: Context with { NotBefore = Now.AddSeconds(1) }).Allowed, Is.False);
    [Test] public void WindowStartIsInclusive() => Assert.That(Check(context: Context with { NotBefore = Now }).Allowed, Is.True);
    [Test] public void WindowEndIsExclusive() => Assert.That(Check(context: Context with { NotAfter = Now }).Allowed, Is.False);
    [Test] public void InvalidWindowFailsClosed() => Assert.That(
        Check(context: Context with { NotBefore = Now, NotAfter = Now }).Allowed, Is.False);
}
