using System.Text.RegularExpressions;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Guards;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests;

[TestFixture]
public sealed class OwnershipScopeGuardTests
{
    private static readonly Guid SubjectId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CourseId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherCourseId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string ResourceId = "guard-test-resource";
    private static readonly Regex TraceIdPattern =
        new(@"^[a-z0-9._:-]{1,64}\z", RegexOptions.CultureInvariant);

    [TestCaseSource(nameof(ActionRelationCases))]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_ActionRelationMatrix_MatchesPolicyDefinition(
        string action,
        CourseRole role,
        bool delegated,
        PolicyOutcome expected)
    {
        var snapshot = Snapshot(role, action: action,
            delegatedAction: delegated ? action : null);

        Assert.That(Evaluate(snapshot, action).Outcome, Is.EqualTo(expected), action);
    }

    public static IEnumerable<TestCaseData> ActionRelationCases()
    {
        foreach (var definition in CourseRolePolicies.All)
        {
            foreach (var role in new[]
                     {
                         CourseRole.Student, CourseRole.Teacher, CourseRole.Assistant,
                         CourseRole.Unknown
                     })
            {
                var delegatedVariants = role == CourseRole.Assistant
                    ? new[] { false, true }
                    : new[] { false };
                foreach (var delegated in delegatedVariants)
                {
                    var expected = role switch
                    {
                        CourseRole.Student when definition.AllowedRoles.Contains(role) =>
                            PolicyOutcome.Permit,
                        CourseRole.Teacher when definition.AllowedRoles.Contains(role) =>
                            PolicyOutcome.Permit,
                        CourseRole.Assistant when delegated && definition.AllowAssistantDelegation =>
                            PolicyOutcome.Permit,
                        _ => PolicyOutcome.Deny
                    };
                    var resultName = expected == PolicyOutcome.Permit ? "Permit" : "Deny";
                    yield return new TestCaseData(definition.Action.Id, role, delegated, expected)
                        .SetName($"ACC08_ActionRelationMatrix_{definition.Action.Id.Replace('.', '_')}_{role}_{(delegated ? "Delegated" : "NoDelegation")}_{resultName}");
                }
            }
        }

        foreach (var role in new[]
                 {
                     CourseRole.Student, CourseRole.Teacher, CourseRole.Assistant,
                     CourseRole.Unknown
                 })
        {
            yield return new TestCaseData("results.sensitive.read", role, true, PolicyOutcome.Deny)
                .SetName($"ACC08_SensitiveReadMatrix_{role}_Deny");
            yield return new TestCaseData("results.sensitive.read", role, false, PolicyOutcome.Deny)
                .SetName($"ACC08_SensitiveReadMatrix_{role}_NoDelegation_Deny");
        }
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_LeftAndSuspendedMembership_Deny()
    {
        foreach (var status in new[] { MembershipStatus.Left, MembershipStatus.Suspended })
        {
            var result = Evaluate(Snapshot(CourseRole.Student, status: status), "lab.read");
            Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
            Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.NotPermitted));
        }
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_AssistantDelegatedAttemptReadCourse_Permits()
    {
        var snapshot = Snapshot(CourseRole.Assistant, action: "attempt.read.course",
            delegatedAction: "attempt.read.course");
        Assert.That(Evaluate(snapshot, "attempt.read.course").Outcome,
            Is.EqualTo(PolicyOutcome.Permit));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_AssistantWithoutAttemptReadCourseDelegation_Denies()
    {
        var snapshot = Snapshot(CourseRole.Assistant, action: "attempt.read.course");
        Assert.That(Evaluate(snapshot, "attempt.read.course").Outcome,
            Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_AssistantDelegationForNonDelegableLabDraftManage_Denies()
    {
        var snapshot = Snapshot(CourseRole.Assistant, action: "lab.draft.manage",
            delegatedAction: "lab.draft.manage");
        Assert.That(Evaluate(snapshot, "lab.draft.manage").Outcome,
            Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_OwnerWithoutCourseRole_DoesNotPermit()
    {
        var result = Evaluate(Snapshot(CourseRole.Unknown), "attempt.read");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_CourseRoleWithoutOwner_DoesNotPermitOwnershipAction()
    {
        var result = Evaluate(Snapshot(CourseRole.Student, ownerId: OtherUserId),
            "attempt.submit");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_OwnershipActionWithAnotherOwner_Denies()
    {
        var result = Evaluate(Snapshot(CourseRole.Student, action: "assignment.read.own",
            ownerId: OtherUserId), "assignment.read.own");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.NotPermitted));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_AttemptReadWithAssignmentResourceType_DeniesContextMismatch()
    {
        var result = Evaluate(Snapshot(CourseRole.Student, action: "attempt.read",
            resourceType: "assignment"), "attempt.read");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.ContextMismatch));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_MembershipAccountDifferentFromSubject_Denies()
    {
        var result = Evaluate(Snapshot(CourseRole.Student, membershipAccountId: OtherUserId),
            "lab.read");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_RoleFromDifferentCourse_DeniesContextMismatch()
    {
        var result = Evaluate(Snapshot(CourseRole.Teacher, membershipCourseId: OtherCourseId),
            "lab.read");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.ContextMismatch));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_EmptyResourceCourseId_DeniesMissingAttribute()
    {
        var result = Evaluate(Snapshot(CourseRole.Student, resourceCourseId: Guid.Empty),
            "lab.read");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [TestCase("{11111111-1111-1111-1111-111111111111}")]
    [TestCase("11111111111111111111111111111111")]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_NonCanonicalSubjectIdFormat_DeniesMissingAttribute(string subjectId)
    {
        var result = Evaluate(Snapshot(CourseRole.Student, subjectId: subjectId), "lab.read");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_MissingResourceAndUnknownAction_DenyMissingAttribute()
    {
        Assert.That(Evaluate(Snapshot(CourseRole.Student, includeResource: false), "lab.read").Reason,
            Is.EqualTo(AccessDenialReason.MissingAttribute));
        Assert.That(Evaluate(Snapshot(CourseRole.Student), "unknown.action").Reason,
            Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_SensitiveReadWithoutMembershipOrResource_Denies()
    {
        var noMembership = Snapshot(CourseRole.Teacher, action: "results.sensitive.read",
            includeMembership: false);
        Assert.That(Evaluate(noMembership, "results.sensitive.read").Outcome,
            Is.EqualTo(PolicyOutcome.Deny));

        var noResource = Snapshot(CourseRole.Teacher, action: "results.sensitive.read",
            includeResource: false);
        Assert.That(Evaluate(noResource, "results.sensitive.read").Outcome,
            Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_GrantHolderCurrentlyNeverPermits()
    {
        var snapshot = EvaluationSnapshotBuilder.Default()
            .WithSubject(new SubjectRef(SubjectId.ToString("D"), AccountState.Active))
            .WithResource(CreateResource("results.sensitive.read"))
            .WithGrants([
                new ElevatedGrant(Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    SubjectId, SubjectId, "results.sensitive.read",
                    new GrantScope(GrantScopeKind.Resource, ResourceId), "test",
                    DateTimeOffset.MinValue, DateTimeOffset.MaxValue, false)
            ])
            .Build();

        var result = Evaluate(snapshot, "results.sensitive.read");
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.NotPermitted));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_RoleRevocationInNextSnapshot_DeniesWithoutCache()
    {
        var guard = new OwnershipScopeGuard();
        var permitted = Snapshot(CourseRole.Teacher);
        var revoked = Snapshot(CourseRole.Unknown);

        Assert.That(Evaluate(guard, permitted, "lab.draft.manage").Outcome,
            Is.EqualTo(PolicyOutcome.Permit));
        Assert.That(Evaluate(guard, revoked, "lab.draft.manage").Outcome,
            Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public void ACC08_SeededOracleAndConditionRemoval_AreFailClosedAndDeterministic()
    {
        var random = new Random(8008);
        var guard = new OwnershipScopeGuard();
        var definitions = CourseRolePolicies.All;
        var roles = new[] { CourseRole.Student, CourseRole.Teacher, CourseRole.Assistant, CourseRole.Unknown };
        var statuses = new[] { MembershipStatus.Active, MembershipStatus.Left, MembershipStatus.Suspended };

        for (var i = 0; i < 250; i++)
        {
            var definition = definitions[random.Next(definitions.Count)];
            var role = roles[random.Next(roles.Length)];
            var status = statuses[random.Next(statuses.Length)];
            var resourceCourseId = random.Next(4) == 0 ? OtherCourseId : CourseId;
            var membershipCourseId = random.Next(8) == 0 ? Guid.Empty :
                random.Next(4) == 0 ? OtherCourseId : CourseId;
            var ownerId = random.Next(4) switch
            {
                0 => Guid.Empty,
                1 => OtherUserId,
                _ => SubjectId
            };
            var resourceType = random.Next(5) == 0
                ? WrongResourceType(definition.Action.Id)
                : definition.Action.ResourceType!;
            var delegated = random.Next(2) == 0;
            var accountId = random.Next(6) == 0 ? OtherUserId : SubjectId;
            var snapshot = Snapshot(role, definition.Action.Id, status, ownerId,
                delegated ? definition.Action.Id : null, resourceCourseId,
                membershipCourseId, accountId, resourceType);

            var roleAllowed =
                role == CourseRole.Student && definition.AllowedRoles.Contains(CourseRole.Student) ||
                role == CourseRole.Teacher && definition.AllowedRoles.Contains(CourseRole.Teacher) ||
                role == CourseRole.Assistant && delegated && definition.AllowAssistantDelegation;
            var expectedPermit = roleAllowed &&
                status == MembershipStatus.Active &&
                accountId == SubjectId &&
                membershipCourseId == resourceCourseId &&
                resourceCourseId != Guid.Empty &&
                membershipCourseId != Guid.Empty &&
                resourceType == definition.Action.ResourceType &&
                (!definition.RequiresOwnership || ownerId == SubjectId);

            var result = Evaluate(guard, snapshot, definition.Action.Id);
            Assert.That(result.Outcome,
                Is.EqualTo(expectedPermit ? PolicyOutcome.Permit : PolicyOutcome.Deny),
                $"iteration {i}, action {definition.Action.Id}");
            var repeat = Evaluate(guard, snapshot, definition.Action.Id);
            Assert.That(repeat.Outcome, Is.EqualTo(result.Outcome));
            Assert.That(repeat.Reason, Is.EqualTo(result.Reason));
            Assert.That(repeat.Trace.Steps, Is.EqualTo(result.Trace.Steps));
        }

        var valid = Snapshot(CourseRole.Student, "attempt.read");
        Assert.That(Evaluate(guard, valid, "attempt.read").Outcome, Is.EqualTo(PolicyOutcome.Permit));
        var deniedSnapshots = new[]
        {
            Snapshot(CourseRole.Unknown, "attempt.read"),
            Snapshot(CourseRole.Student, "attempt.read", status: MembershipStatus.Left),
            Snapshot(CourseRole.Student, "attempt.read", membershipCourseId: OtherCourseId),
            Snapshot(CourseRole.Student, "attempt.read", resourceCourseId: OtherCourseId,
                membershipCourseId: CourseId),
            Snapshot(CourseRole.Student, "attempt.read", ownerId: OtherUserId),
            Snapshot(CourseRole.Student, "attempt.read", resourceType: "assignment"),
            Snapshot(CourseRole.Student, "attempt.read", membershipAccountId: OtherUserId)
        };
        foreach (var denied in deniedSnapshots)
            Assert.That(Evaluate(guard, denied, "attempt.read").Outcome,
                Is.EqualTo(PolicyOutcome.Deny));
    }

    private static PolicyResult Evaluate(EvaluationSnapshot snapshot, string action) =>
        Evaluate(new OwnershipScopeGuard(), snapshot, action);

    private static PolicyResult Evaluate(
        OwnershipScopeGuard guard,
        EvaluationSnapshot snapshot,
        string action)
    {
        var result = guard.Evaluate(snapshot, action);
        AssertTraceSafe(result, snapshot);
        return result;
    }

    private static void AssertTraceSafe(PolicyResult result, EvaluationSnapshot snapshot)
    {
        Assert.That(result.Trace.Steps.Count, Is.LessThanOrEqualTo(32));
        foreach (var step in result.Trace.Steps)
        {
            Assert.That(TraceIdPattern.IsMatch(step.Id), Is.True, step.Id);
            Assert.That(step.Id, Does.Not.Contain(snapshot.Subject.Id));
            if (snapshot.Resource is not null)
                Assert.That(step.Id, Does.Not.Contain(snapshot.Resource.Id));
        }
    }

    private static EvaluationSnapshot Snapshot(
        CourseRole role,
        string? action = null,
        MembershipStatus status = MembershipStatus.Active,
        Guid? ownerId = null,
        string? delegatedAction = null,
        Guid? resourceCourseId = null,
        Guid? membershipCourseId = null,
        Guid? membershipAccountId = null,
        string? resourceType = null,
        string? subjectId = null,
        bool includeResource = true,
        bool includeMembership = true)
    {
        var delegated = delegatedAction is null
            ? new HashSet<string>()
            : new HashSet<string>(StringComparer.Ordinal) { delegatedAction };
        var course = resourceCourseId ?? CourseId;
        ResourceDescriptor? resource = includeResource
            ? CreateResource(action ?? "lab.read", course, ownerId, resourceType)
            : null;
        CourseMembership? membership = includeMembership
            ? new CourseMembership(
                membershipAccountId ?? SubjectId,
                membershipCourseId ?? course,
                role,
                status,
                delegated)
            : null;

        return EvaluationSnapshotBuilder.Default()
            .WithSubject(new SubjectRef(subjectId ?? SubjectId.ToString("D"), AccountState.Active))
            .WithMembership(membership)
            .WithResource(resource)
            .Build();
    }

    private static ResourceDescriptor CreateResource(
        string action,
        Guid? courseId = null,
        Guid? ownerId = null,
        string? resourceType = null)
    {
        var definition = CourseRolePolicies.All
            .FirstOrDefault(value => value.Action.Id == action)?.Action
            ?? AccessActions.All.FirstOrDefault(value => value.Id == action);

        return new ResourceDescriptor(
            resourceType ?? definition?.ResourceType ?? "laboratory",
            ResourceId,
            courseId ?? CourseId,
            ownerId ?? SubjectId,
            null,
            new Dictionary<string, AttributeValue>());
    }

    private static string WrongResourceType(string action) =>
        action.StartsWith("attempt.", StringComparison.Ordinal) ||
        action == "results.sensitive.read" ? "laboratory" : "attempt";

    private static readonly Guid OtherUserId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
}
