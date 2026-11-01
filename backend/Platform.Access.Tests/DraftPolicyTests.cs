using NUnit.Framework;
using Platform.Api.Modules.Access;
using Platform.Api.Modules.Access.Demo;

namespace Platform.Access.Tests;

public sealed class DraftPolicyTests
{
    private readonly AccessPolicy policy = new();
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static AccountSnapshot Actor => new(DemoData.Ana, true);
    private static MembershipSnapshot Member => new(DemoData.Ana, DemoData.Course, CourseRole.Student, true);
    private static AttemptSnapshot Draft => new(DemoData.DraftAttempt, DemoData.Ana, DemoData.Course,
        AttemptState.InProgress, 1, "open");
    private static ContextPolicy Window => new("open", "window:v1");
    private AccessVerdict Check(AccountSnapshot? actor = null, MembershipSnapshot? member = null,
        AttemptSnapshot? draft = null, ContextPolicy? window = null) =>
        policy.Evaluate(actor ?? Actor, member ?? Member, draft ?? Draft,
            AccessPolicy.UpdateDraftAnswer, window ?? Window, Now);

    [Test] public void OwnerCanUpdateOwnDraft() => Assert.That(Check().Allowed, Is.True);
    [Test] public void StudentCannotUpdateForeignDraft() => Assert.That(
        Check(draft: Draft with { OwnerId = DemoData.Marko }).Allowed, Is.False);
    [TestCase(CourseRole.Teacher)] [TestCase(CourseRole.Assistant)]
    public void ReviewPermissionDoesNotGrantDraftWrite(CourseRole role) => Assert.That(
        Check(member: Member with { Role = role, CanReview = true },
            draft: Draft with { OwnerId = DemoData.Marko }).Allowed, Is.False);
    [TestCase(AttemptState.Submitted)] [TestCase(AttemptState.Completed)]
    public void SubmittedAndCompletedAnswersCannotBeEdited(AttemptState state) => Assert.That(
        Check(draft: Draft with { State = state }).Allowed, Is.False);
    [TestCase(CourseRole.Teacher)] [TestCase(CourseRole.Assistant)]
    public void DraftWriteRequiresStudentMembership(CourseRole role) => Assert.That(
        Check(member: Member with { Role = role }).Allowed, Is.False);
    [Test] public void InactiveOwnerCannotUpdate() => Assert.That(Check(actor: Actor with { IsActive = false }).Allowed, Is.False);
    [Test] public void InactiveMembershipCannotUpdate() => Assert.That(Check(member: Member with { IsActive = false }).Allowed, Is.False);
    [Test] public void ForeignCourseMembershipCannotUpdate() => Assert.That(
        Check(member: Member with { CourseId = DemoData.OtherCourse }).Allowed, Is.False);
    [Test] public void AnotherAccountsMembershipCannotUpdate() => Assert.That(
        Check(member: Member with { AccountId = DemoData.Marko }).Allowed, Is.False);
    [Test] public void ExpiredContextDeniesDraftOwner() => Assert.That(
        Check(window: Window with { NotAfter = Now }).Allowed, Is.False);
    [Test] public void FutureContextDeniesDraftOwner() => Assert.That(
        Check(window: Window with { NotBefore = Now.AddSeconds(1) }).Allowed, Is.False);
    [Test] public void MissingContextCannotAuthorizeUpdate() => Assert.That(
        policy.Evaluate(Actor, Member, Draft, AccessPolicy.UpdateDraftAnswer, null, Now).Allowed, Is.False);
    [Test] public void ValidContextCannotGrantForeignDraftWrite() => Assert.That(
        Check(draft: Draft with { OwnerId = DemoData.Marko },
            window: Window with { NotBefore = Now.AddMinutes(-1), NotAfter = Now.AddMinutes(1) }).Allowed, Is.False);
}
