using NUnit.Framework;
using Microsoft.Extensions.DependencyInjection;
using Platform.Access.Contracts;
using Platform.Access.Composition;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Membership;
using Platform.Access.Infrastructure.Resources;
using Platform.Access.Infrastructure.Policies;

namespace Platform.Access.Tests;

[TestFixture]
public sealed class CourseRoleAccessPolicyTests
{
    private readonly Guid accountId = Guid.NewGuid();
    private readonly Guid courseId = Guid.NewGuid();
    private readonly ResourceRef laboratory = new("laboratory", "lab-42");
    private readonly ResourceRef assignment = new("assignment", "assignment-7");
    private readonly ResourceRef attempt = new("attempt", "attempt-9");
    private InMemoryMembershipReader memberships = null!;
    private InMemoryResourceDescriptorResolver resources = null!;

    [SetUp]
    public void SetUp()
    {
        memberships = new InMemoryMembershipReader();
        resources = new InMemoryResourceDescriptorResolver();
        resources.SetCourse(laboratory, courseId);
        resources.SetCourse(assignment, courseId);
        resources.SetCourse(attempt, courseId);
        resources.SetOwner(assignment, accountId);
        resources.SetOwner(attempt, accountId);
    }

    [TestCase(CourseRole.Student, "lab.read", true)]
    [TestCase(CourseRole.Student, "lab.draft.manage", false)]
    [TestCase(CourseRole.Student, "lab.revision.publish", false)]
    [TestCase(CourseRole.Student, "lab.criteria.hidden.read", false)]
    [TestCase(CourseRole.Student, "assignment.manage", false)]
    [TestCase(CourseRole.Student, "assignment.read.own", true)]
    [TestCase(CourseRole.Student, "attempt.create", true)]
    [TestCase(CourseRole.Student, "attempt.read", true)]
    [TestCase(CourseRole.Student, "attempt.submit", true)]
    [TestCase(CourseRole.Student, "attempt.read.course", false)]
    [TestCase(CourseRole.Student, "attempt.review", false)]
    [TestCase(CourseRole.Teacher, "lab.read", true)]
    [TestCase(CourseRole.Teacher, "lab.draft.manage", true)]
    [TestCase(CourseRole.Teacher, "lab.revision.publish", true)]
    [TestCase(CourseRole.Teacher, "lab.criteria.hidden.read", true)]
    [TestCase(CourseRole.Teacher, "assignment.manage", true)]
    [TestCase(CourseRole.Teacher, "assignment.read.own", false)]
    [TestCase(CourseRole.Teacher, "attempt.create", false)]
    [TestCase(CourseRole.Teacher, "attempt.read", false)]
    [TestCase(CourseRole.Teacher, "attempt.submit", false)]
    [TestCase(CourseRole.Teacher, "attempt.read.course", true)]
    [TestCase(CourseRole.Teacher, "attempt.review", true)]
    public async Task StudentAndTeacher_FollowCourseRoleMatrix(CourseRole role, string action, bool expected)
    {
        AddMembership(role);
        Assert.That(await Evaluate(action), Is.EqualTo(expected));
    }

    [Test]
    public async Task Assistant_IsDeniedWhenActionWasNotDelegated()
    {
        AddMembership(CourseRole.Assistant);
        Assert.That(await Evaluate("lab.read"), Is.False);
    }

    [TestCase("lab.read")]
    [TestCase("attempt.read.course")]
    [TestCase("attempt.review")]
    public async Task Assistant_IsAllowedOnlyForExplicitlyDelegatedActions(string action)
    {
        AddMembership(CourseRole.Assistant, action);
        Assert.That(await Evaluate(action), Is.True);
    }

    [Test]
    public async Task MembershipReader_SnapshotsDelegatedActionsOnSet()
    {
        var delegatedActions = new HashSet<string>(StringComparer.Ordinal) { "lab.read" };
        memberships.Set(new CourseMembership(
            accountId,
            courseId,
            CourseRole.Assistant,
            MembershipStatus.Active,
            delegatedActions));

        delegatedActions.Clear();
        delegatedActions.Add("attempt.read.course");

        Assert.That(await Evaluate("lab.read"), Is.True);
        Assert.That(await Evaluate("attempt.read.course"), Is.False);
    }

    [TestCase("lab.draft.manage")]
    [TestCase("lab.revision.publish")]
    [TestCase("lab.criteria.hidden.read")]
    [TestCase("assignment.manage")]
    [TestCase("assignment.read.own")]
    [TestCase("attempt.create")]
    [TestCase("attempt.read")]
    [TestCase("attempt.submit")]
    public async Task Assistant_CannotReceiveActionsThatAreNotDelegable(string action)
    {
        AddMembership(CourseRole.Assistant, action);
        Assert.That(await Evaluate(action), Is.False);
    }

    [Test]
    public async Task MissingMembership_IsDenied()
    {
        Assert.That(await Evaluate("lab.read"), Is.False);
    }

    [TestCase(MembershipStatus.Left)]
    [TestCase(MembershipStatus.Suspended)]
    [TestCase(MembershipStatus.Unknown)]
    public async Task NonActiveMembership_IsDenied(MembershipStatus status)
    {
        AddMembership(CourseRole.Teacher, status: status);
        Assert.That(await Evaluate("lab.draft.manage"), Is.False);
    }

    [Test]
    public async Task MembershipInAnotherCourse_DoesNotGrantAccess()
    {
        AddMembership(CourseRole.Teacher, membershipCourseId: Guid.NewGuid());
        Assert.That(await Evaluate("lab.draft.manage"), Is.False);
    }

    [Test]
    public async Task MembershipIsReadAgainForEveryDecision()
    {
        AddMembership(CourseRole.Teacher);
        var policy = CreatePolicy("lab.draft.manage");
        var request = CreateRequest("lab.draft.manage");
        Assert.That(await policy.EvaluateAsync(request), Is.True);

        AddMembership(CourseRole.Teacher, status: MembershipStatus.Suspended);
        Assert.That(await policy.EvaluateAsync(request), Is.False);
    }

    [Test]
    public async Task AttemptOwnership_IsRequiredForStudentReadAndSubmit()
    {
        AddMembership(CourseRole.Student);
        Assert.That(await Evaluate("attempt.read"), Is.True);
        Assert.That(await Evaluate("attempt.submit"), Is.True);

        resources.SetOwner(attempt, Guid.NewGuid());
        Assert.That(await Evaluate("attempt.read"), Is.False);
        Assert.That(await Evaluate("attempt.submit"), Is.False);
    }

    [Test]
    public async Task AssignmentOwnership_IsRequiredForStudentRead()
    {
        AddMembership(CourseRole.Student);
        Assert.That(await Evaluate("assignment.read.own"), Is.True);

        resources.SetOwner(assignment, Guid.NewGuid());
        Assert.That(await Evaluate("assignment.read.own"), Is.False);
    }

    [Test]
    public async Task MissingResourceCourseContext_IsDenied()
    {
        resources.Remove(laboratory);
        AddMembership(CourseRole.Teacher);
        Assert.That(await Evaluate("lab.draft.manage"), Is.False);
    }

    [Test]
    public async Task ResourceTypeMismatch_IsDenied()
    {
        AddMembership(CourseRole.Teacher);
        Assert.That(await Evaluate("lab.draft.manage", attempt), Is.False);
    }

    [Test]
    public async Task InvalidAccountIdentifier_IsDenied()
    {
        var request = new AccessRequest(
            new SubjectRef("not-a-guid", AccountState.Active),
            "lab.read",
            laboratory,
            Guid.NewGuid().ToString("N"));
        Assert.That(await CreatePolicy("lab.read").EvaluateAsync(request), Is.False);
    }

    [Test]
    public async Task AddAccessControl_UsesCourseRolePolicyForDecision()
    {
        AddMembership(CourseRole.Teacher);
        var services = new ServiceCollection();
        services.AddSingleton<IMembershipReader>(memberships);
        services.AddSingleton<IResourceDescriptorResolver>(resources);
        services.AddAccessControl();

        using var provider = services.BuildServiceProvider();
        var decision = provider.GetRequiredService<IAccessDecision>();
        var request = CreateRequest("lab.draft.manage");

        var result = await decision.DecideAsync(request);

        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task AddAccessControl_DefaultContextReaders_DenyCourseActions()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();

        using var provider = services.BuildServiceProvider();
        var decision = provider.GetRequiredService<IAccessDecision>();
        var request = CreateRequest("lab.read");

        var result = await decision.DecideAsync(request);

        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    private void AddMembership(
        CourseRole role,
        string? delegatedAction = null,
        MembershipStatus status = MembershipStatus.Active,
        Guid? membershipCourseId = null)
    {
        var delegated = new HashSet<string>(StringComparer.Ordinal);
        if (delegatedAction is not null) delegated.Add(delegatedAction);
        memberships.Set(new CourseMembership(accountId, membershipCourseId ?? courseId, role, status, delegated));
    }

    private async Task<bool> Evaluate(string action, ResourceRef? resource = null) =>
        await CreatePolicy(action).EvaluateAsync(CreateRequest(action, resource ?? ResourceForAction(action)));

    private CourseRoleAccessPolicy CreatePolicy(string action)
    {
        var definition = CourseRolePolicies.All.Single(x => x.Action.Id == action);
        return new CourseRoleAccessPolicy(definition, memberships, resources);
    }

    private ResourceRef ResourceForAction(string action)
    {
        var type = CourseRolePolicies.All.Single(x => x.Action.Id == action).Action.ResourceType;
        return type switch
        {
            "laboratory" => laboratory,
            "assignment" => assignment,
            "attempt" => attempt,
            _ => throw new InvalidOperationException($"No test resource for '{type}'.")
        };
    }

    private AccessRequest CreateRequest(string action, ResourceRef? resource = null) => new(
        new SubjectRef(accountId.ToString(), AccountState.Active),
        action,
        resource ?? ResourceForAction(action),
        Guid.NewGuid().ToString("N"));
}
