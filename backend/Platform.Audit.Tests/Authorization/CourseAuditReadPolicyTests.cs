using NUnit.Framework;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Application.Authorization;
using static Platform.Audit.Tests.Authorization.AuditAuthorizationExamples;

namespace Platform.Audit.Tests.Authorization;

[TestFixture]
public sealed class CourseAuditReadPolicyTests
{
    private StubCourseAuditMemberships memberships = null!;
    private CourseAuditReadPolicy policy = null!;

    [SetUp]
    public void SetUp()
    {
        memberships = new StubCourseAuditMemberships();
        policy = new CourseAuditReadPolicy(memberships);
    }

    [Test]
    public async Task ActiveTeacher_SeesTheirCourse()
    {
        memberships.Set(AuditAuthorizationExamples.Membership());
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
    }

    [Test]
    public async Task TeacherInK_StudentInL_DoesNotInheritTeacherPermissionInL()
    {
        memberships.Set(AuditAuthorizationExamples.Membership());
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Student, courseId: CourseL));
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
        Assert.That(await policy.EvaluateAsync(Request(courseId: CourseL)), Is.False);
    }

    [Test]
    public async Task ActiveAssistant_WithCurrentExactActionDelegation_IsAllowed()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant, delegatedActions: ["audit.events.read"]));
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
    }

    [TestCase(CourseRole.Student)]
    [TestCase(CourseRole.Unknown)]
    [TestCase((CourseRole)999)]
    public async Task NonTeachingRole_IsDeniedEvenWithADelegatedAction(CourseRole role)
    {
        memberships.Set(AuditAuthorizationExamples.Membership(role, delegatedActions: ["audit.events.read"]));
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [TestCase("attempt.read")]
    [TestCase("Audit.Events.Read")]
    [TestCase("audit.events.read ")]
    [TestCase("audit.events.*")]
    public async Task UnrelatedOrMalformedDelegatedAction_DoesNotGrantAccess(string action)
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant, delegatedActions: [action]));
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public async Task CaseInsensitiveProviderSet_CannotWeakenExactActionMatching()
    {
        var membership = AuditAuthorizationExamples.Membership(CourseRole.Assistant) with
        {
            DelegatedActions = new HashSet<string>(["Audit.Events.Read"], StringComparer.OrdinalIgnoreCase)
        };
        memberships.Handler = (_, _, _) => ValueTask.FromResult<CourseMembership?>(membership);
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public async Task AssistantWithoutDelegatedAction_IsDenied()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant));
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public async Task MissingDelegatedActionSet_DoesNotGrantAssistantAccess()
    {
        memberships.Handler = (_, _, _) => ValueTask.FromResult<CourseMembership?>(
            AuditAuthorizationExamples.Membership(CourseRole.Assistant) with { DelegatedActions = null! });
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public async Task MatchingActionAmongUnrelatedActions_GrantsOnlyTheMembershipCourse()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant, delegatedActions: ["attempt.read", "audit.events.read"]));
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
        Assert.That(await policy.EvaluateAsync(Request(courseId: CourseL)), Is.False);
    }

    [TestCase("anonymous")]
    [TestCase("blocked-subject")]
    [TestCase("archived-subject")]
    [TestCase("unknown-subject-state")]
    [TestCase("invalid-subject-id")]
    [TestCase("empty-subject-id")]
    [TestCase("null-subject-id")]
    [TestCase("wrong-action")]
    [TestCase("action-case")]
    [TestCase("missing-resource")]
    [TestCase("wrong-resource-type")]
    [TestCase("resource-type-case")]
    [TestCase("invalid-course-id")]
    [TestCase("empty-course-id")]
    [TestCase("uppercase-course-id")]
    [TestCase("compact-course-id")]
    [TestCase("braced-course-id")]
    [TestCase("padded-course-id")]
    [TestCase("null-course-id")]
    public async Task InvalidRequest_IsDeniedBeforeProviderLookup(string scenario)
    {
        memberships.Set(AuditAuthorizationExamples.Membership());
        var request = Request();
        request = scenario switch
        {
            "anonymous" => request with { Subject = null },
            "blocked-subject" => request with { Subject = request.Subject! with { State = AccountState.Blocked } },
            "archived-subject" => request with { Subject = request.Subject! with { State = AccountState.Archived } },
            "unknown-subject-state" => request with { Subject = request.Subject! with { State = (AccountState)999 } },
            "invalid-subject-id" => request with { Subject = request.Subject! with { Id = "not-a-guid" } },
            "empty-subject-id" => request with { Subject = request.Subject! with { Id = Guid.Empty.ToString("D") } },
            "null-subject-id" => request with { Subject = request.Subject! with { Id = null! } },
            "wrong-action" => request with { Action = "audit.events.write" },
            "action-case" => request with { Action = "Audit.Events.Read" },
            "missing-resource" => request with { Resource = null },
            "wrong-resource-type" => request with { Resource = new ResourceRef("account", CourseK.ToString("D")) },
            "resource-type-case" => request with { Resource = new ResourceRef("Course", CourseK.ToString("D")) },
            "invalid-course-id" => request with { Resource = new ResourceRef("course", "not-a-guid") },
            "empty-course-id" => request with { Resource = new ResourceRef("course", Guid.Empty.ToString("D")) },
            "uppercase-course-id" => request with { Resource = new ResourceRef("course", CourseK.ToString("D").ToUpperInvariant()) },
            "compact-course-id" => request with { Resource = new ResourceRef("course", CourseK.ToString("N")) },
            "braced-course-id" => request with { Resource = new ResourceRef("course", CourseK.ToString("B")) },
            "padded-course-id" => request with { Resource = new ResourceRef("course", " " + CourseK.ToString("D") + " ") },
            "null-course-id" => request with { Resource = new ResourceRef("course", null!) },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Assert.That(await policy.EvaluateAsync(request), Is.False);
        Assert.That(memberships.Calls, Is.Zero);
    }

    [Test]
    public async Task NullRequest_IsDeniedWithoutProviderLookup()
    {
        Assert.That(await policy.EvaluateAsync(null!), Is.False);
        Assert.That(memberships.Calls, Is.Zero);
    }

    [Test]
    public async Task AccountWithoutCourseMembership_HasNoImplicitAdministratorPermission()
    {
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [TestCase(MembershipStatus.Left)]
    [TestCase(MembershipStatus.Suspended)]
    [TestCase((MembershipStatus)999)]
    public async Task NonActiveMembership_CannotGrantTeacherAccess(MembershipStatus status)
    {
        memberships.Set(AuditAuthorizationExamples.Membership() with { Status = status });
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [TestCase("other-account")]
    [TestCase("other-course")]
    public async Task ProviderResultForAnotherScope_CannotGrantTeacherAccess(string scenario)
    {
        var membership = scenario == "other-account"
            ? AuditAuthorizationExamples.Membership(accountId: OtherAccountId)
            : AuditAuthorizationExamples.Membership(courseId: CourseL);
        memberships.Handler = (_, _, _) => ValueTask.FromResult<CourseMembership?>(membership);
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public async Task NextRequestWithBlockedSubject_IsDeniedByTheSamePolicy()
    {
        memberships.Set(AuditAuthorizationExamples.Membership());
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
        var nextRequest = Request() with { Subject = new SubjectRef(TeacherId.ToString("D"), AccountState.Blocked) };
        Assert.That(await policy.EvaluateAsync(nextRequest), Is.False);
        Assert.That(memberships.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task MembershipRevokedBetweenRequests_IsDeniedWithoutRestart()
    {
        memberships.Set(AuditAuthorizationExamples.Membership());
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
        memberships.Set(AuditAuthorizationExamples.Membership() with { Status = MembershipStatus.Left });
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
        Assert.That(memberships.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task MembershipRemovedBetweenRequests_IsDeniedWithoutRestart()
    {
        memberships.Set(AuditAuthorizationExamples.Membership());
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
        memberships.Remove(TeacherId, CourseK);
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public async Task TeachingRoleRemovedBetweenRequests_IsDeniedWithoutRestart()
    {
        memberships.Set(AuditAuthorizationExamples.Membership());
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Student));
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public async Task DelegatedActionRevokedByProvider_IsDeniedOnNextEvaluation()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant, delegatedActions: ["audit.events.read"]));
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant));
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
        Assert.That(memberships.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task CurrentRightsProviderFiltersValidity_AndPolicyDoesNotCacheItsResult()
    {
        // Dates are the provider's responsibility; this models its effective-rights
        // contract rather than claiming the Audit policy independently expires grants.
        var clock = new AuditAuthorizationClock();
        var validFrom = clock.UtcNow.AddMinutes(1);
        var expiresAt = validFrom.AddMinutes(1);
        memberships.Handler = (accountId, courseId, _) => ValueTask.FromResult<CourseMembership?>(
            AuditAuthorizationExamples.Membership(CourseRole.Assistant, accountId, courseId,
                clock.GetUtcNow() >= validFrom && clock.GetUtcNow() < expiresAt ? ["audit.events.read"] : []));
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
        clock.UtcNow = validFrom;
        Assert.That(await policy.EvaluateAsync(Request()), Is.True);
        clock.UtcNow = expiresAt;
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
        Assert.That(memberships.Calls, Is.EqualTo(3));
    }

    [TestCase("unavailable")]
    [TestCase("timeout")]
    [TestCase("unrelated-cancellation")]
    public async Task ProviderFailure_DoesNotGrantAccess(string failure)
    {
        Exception exception = failure switch
        {
            "unavailable" => new InvalidOperationException("unavailable"),
            "timeout" => new TimeoutException(),
            "unrelated-cancellation" => new OperationCanceledException(),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
        memberships.Handler = (_, _, _) => ValueTask.FromException<CourseMembership?>(exception);
        Assert.That(await policy.EvaluateAsync(Request()), Is.False);
    }

    [Test]
    public void AlreadyCancelledCallerToken_IsPropagatedByPolicy()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await policy.EvaluateAsync(Request(), cancellation.Token));
        Assert.That(memberships.Calls, Is.Zero);
    }

    [Test]
    public void CallerCancellationDuringLookup_IsPropagatedByPolicy()
    {
        using var cancellation = new CancellationTokenSource();
        memberships.Handler = (_, _, token) =>
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<CourseMembership?>(token);
        };
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await policy.EvaluateAsync(Request(), cancellation.Token));
    }

    [Test]
    public async Task ConcurrentSubjects_DoNotShareAccountOrCourseState()
    {
        memberships.Set(AuditAuthorizationExamples.Membership());
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Student, OtherAccountId, CourseL));
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        memberships.Handler = async (accountId, courseId, token) =>
        {
            if (Interlocked.Increment(ref started) == 2)
                bothStarted.TrySetResult();
            await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            return memberships.Snapshot(accountId, courseId);
        };
        var teacher = policy.EvaluateAsync(Request(correlationId: "teacher-request")).AsTask();
        var student = policy.EvaluateAsync(Request(OtherAccountId, CourseL, "student-request")).AsTask();
        await Task.WhenAll(teacher, student);
        Assert.Multiple(() =>
        {
            Assert.That(teacher.Result, Is.True);
            Assert.That(student.Result, Is.False);
            Assert.That(memberships.Calls, Is.EqualTo(2));
        });
    }
}
