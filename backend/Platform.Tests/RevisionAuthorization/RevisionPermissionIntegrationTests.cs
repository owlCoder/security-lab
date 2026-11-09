using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Security.RevisionAuthorization;
using IAccessDecision = Platform.Access.Contracts.IAccessDecision;

namespace Platform.Tests.RevisionAuthorization;

/// <summary>Exercises the real registration, Access engine and membership adapter together.</summary>
public sealed class RevisionPermissionIntegrationTests
{
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();
    private static readonly Guid Revision = Guid.NewGuid();
    private static readonly Guid Course = Guid.NewGuid();
    private InMemoryMembershipReader memberships = null!;
    private ServiceProvider services = null!;

    [SetUp]
    public void SetUp()
    {
        memberships = new InMemoryMembershipReader();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddAccessControl();
        collection.AddSingleton<IMembershipReader>(memberships);
        collection.AddSingleton<IRevisionResourceReader>(new Resources());
        collection.AddSingleton<IRevisionAuthorReader>(new Authors());
        collection.AddSingleton<IAdminDirectory>(new Admins());
        collection.AddRevisionAuthorization();
        services = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
        services.RegisterRevisionPolicies();
    }

    [TearDown]
    public void TearDown() => services.Dispose();

    [TestCase(RevisionActions.Create)]
    [TestCase(RevisionActions.Read)]
    [TestCase(RevisionActions.Mine)]
    [TestCase(RevisionActions.PendingReviews)]
    [TestCase(RevisionActions.Submit)]
    [TestCase(RevisionActions.Approve)]
    [TestCase(RevisionActions.Return)]
    public async Task TeacherOfRevisionCourse_CanPerformRegisteredAction(string action)
    {
        var actor = action is RevisionActions.Submit or RevisionActions.Mine ? Author : Reviewer;
        SetMembership(actor, CourseRole.Teacher);
        Assert.That((await Decide(actor, action)).Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task AuthorCannotApprove_EvenWithTeacherMembership()
    {
        SetMembership(Author, CourseRole.Teacher);
        Assert.That((await Decide(Author, RevisionActions.Approve)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [TestCase(RevisionActions.Submit)]
    [TestCase(RevisionActions.Mine)]
    public async Task AnotherTeacherCannotActAsAuthor(string action)
    {
        SetMembership(Reviewer, CourseRole.Teacher);
        Assert.That((await Decide(Reviewer, action)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [TestCase(CourseRole.Student)]
    [TestCase(CourseRole.Assistant)]
    [TestCase(CourseRole.Unknown)]
    public async Task RoleWithoutExplicitRights_CannotApprove(CourseRole role)
    {
        SetMembership(Reviewer, role);
        Assert.That((await Decide(Reviewer, RevisionActions.Approve)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task AssistantDelegation_AppliesOnlyToNamedAction()
    {
        SetMembership(Reviewer, CourseRole.Assistant, delegated: [RevisionActions.Read]);
        Assert.That((await Decide(Reviewer, RevisionActions.Read)).Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That((await Decide(Reviewer, RevisionActions.Return)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task OtherCourseMembership_DoesNotGrantAccess()
    {
        memberships.Set(new CourseMembership(Reviewer, Guid.NewGuid(), CourseRole.Teacher,
            MembershipStatus.Active, new HashSet<string>()));
        Assert.That((await Decide(Reviewer, RevisionActions.Read)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [TestCase(MembershipStatus.Left)]
    [TestCase(MembershipStatus.Suspended)]
    public async Task InactiveMembership_DeniesAccess(MembershipStatus status)
    {
        SetMembership(Reviewer, CourseRole.Teacher, status);
        Assert.That((await Decide(Reviewer, RevisionActions.Read)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task RevokedMembership_TakesEffectOnNextDecision()
    {
        SetMembership(Reviewer, CourseRole.Teacher);
        Assert.That((await Decide(Reviewer, RevisionActions.Approve)).Outcome, Is.EqualTo(AccessOutcome.Allow));
        memberships.Remove(Reviewer, Course);
        Assert.That((await Decide(Reviewer, RevisionActions.Approve)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Administrator_CanUnlock_ButCannotApproveWithoutCourseRights()
    {
        Assert.That((await Decide(Reviewer, RevisionActions.UnlockAccount)).Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That((await Decide(Reviewer, RevisionActions.Approve)).Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That((await Decide(Author, RevisionActions.UnlockAccount)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task UnlockWithUnexpectedResource_IsDenied()
    {
        var result = await services.GetRequiredService<IAccessDecision>().DecideAsync(
            new AccessRequest(new SubjectRef(Reviewer.ToString(), AccountState.Active),
                RevisionActions.UnlockAccount, new ResourceRef("account", Revision.ToString()), "test"));
        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [TestCase(AccountState.Blocked)]
    [TestCase(AccountState.Archived)]
    public async Task InactiveAccount_CannotUseOtherwiseValidPermission(AccountState state)
    {
        SetMembership(Reviewer, CourseRole.Teacher);
        Assert.That((await Decide(Reviewer, RevisionActions.Read, state)).Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    private ValueTask<AccessDecision> Decide(Guid actor, string action, AccountState state = AccountState.Active) =>
        services.GetRequiredService<IAccessDecision>().DecideAsync(new AccessRequest(
            new SubjectRef(actor.ToString(), state), action,
            action == RevisionActions.UnlockAccount ? null : new ResourceRef(action == RevisionActions.Create ? "course" : "revision",
                (action == RevisionActions.Create ? Course : Revision).ToString()), "test"));

    private void SetMembership(Guid actor, CourseRole role, MembershipStatus status = MembershipStatus.Active,
        string[]? delegated = null) => memberships.Set(new CourseMembership(actor, Course, role, status,
            new HashSet<string>(delegated ?? [], StringComparer.Ordinal)));

    private sealed class Resources : IRevisionResourceReader
    {
        public Task<RevisionResource?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(id == Revision ? new RevisionResource(Course, Author) : null);
    }

    private sealed class Authors : IRevisionAuthorReader
    {
        public Task<Guid?> FindAuthorIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(id == Revision ? Author : null);
    }

    private sealed class Admins : IAdminDirectory
    {
        public bool IsAdministrator(Guid id) => id == Reviewer;
    }
}
