using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Audit.Application.CourseView;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Audit.Tests.Authorization;
using static Platform.Audit.Tests.Authorization.AuditAuthorizationExamples;

namespace Platform.Audit.Tests.CourseView;

/// <summary>
/// S-14 application-layer check against the real Access engine and the real S-08 policy, with
/// synthetic memberships. Covers the S-14 rights matrix (T07/T08 at the service boundary).
/// </summary>
[TestFixture]
public sealed class CourseAuditReadAuthorizerTests
{
    private const string Correlation = "s14-authz";
    private static readonly Guid AdminId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private StubCourseAuditMemberships memberships = null!;
    private ServiceProvider provider = null!;
    private CourseAuditReadAuthorizer authorizer = null!;

    [SetUp]
    public void SetUp()
    {
        memberships = new StubCourseAuditMemberships();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMembershipReader>(memberships);
        services.AddScoped<ITrustedAuditWriter, ValidatingTrustedAuditWriter>();
        services.AddAccessControl();
        services.AddAudit();
        services.AddCourseAuditView();
        provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        authorizer = provider.GetRequiredService<CourseAuditReadAuthorizer>();
    }

    [TearDown]
    public void TearDown() => provider.Dispose();

    private static SubjectRef Account(Guid id, AccountState state = AccountState.Active) => new(id.ToString("D"), state);

    private ValueTask<CourseAuditReadAuthorization> Authorize(SubjectRef? subject, Guid course) =>
        authorizer.AuthorizeAsync(subject, course, Correlation);

    [Test]
    public async Task Teacher_InCourseK_IsAllowed_WithTheAccountAsViewer()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher));

        var result = await Authorize(Account(TeacherId), CourseK);

        Assert.That(result, Is.EqualTo(CourseAuditReadAuthorization.Allowed(TeacherId)));
    }

    [Test]
    public async Task TeacherInK_RequestingL_IsForbidden()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher));

        var result = await Authorize(Account(TeacherId), CourseL);

        Assert.That(result, Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
    }

    [Test]
    public async Task TeacherInK_StudentInL_CannotReadL()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher));
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Student, courseId: CourseL));

        var own = await Authorize(Account(TeacherId), CourseK);
        var other = await Authorize(Account(TeacherId), CourseL);

        Assert.Multiple(() =>
        {
            Assert.That(own.IsAllowed, Is.True);
            Assert.That(other, Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
        });
    }

    [Test]
    public async Task Student_IsForbidden_EvenWithTheActionInTheSnapshot()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Student, delegatedActions: ["audit.events.read"]));

        Assert.That(await Authorize(Account(TeacherId), CourseK), Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
    }

    [TestCase(new string[0], false, TestName = "Assistant_WithoutDelegation_IsForbidden")]
    [TestCase(new[] { "attempt.review" }, false, TestName = "Assistant_WithDelegationForAnotherAction_IsForbidden")]
    [TestCase(new[] { "AUDIT.EVENTS.READ" }, false, TestName = "Assistant_WithDifferentlyCasedAction_IsForbidden")]
    [TestCase(new[] { "audit.events.read" }, true, TestName = "Assistant_WithAuditReadDelegation_IsAllowed")]
    public async Task Assistant_NeedsTheExactDelegatedAction(string[] delegated, bool allowed)
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant, delegatedActions: delegated));

        var result = await Authorize(Account(TeacherId), CourseK);

        Assert.That(result.IsAllowed, Is.EqualTo(allowed));
    }

    [Test]
    public async Task RevokedMembershipOrDelegation_AppliesFromTheNextCall()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant, delegatedActions: ["audit.events.read"]));
        var before = await Authorize(Account(TeacherId), CourseK);

        // Delegation revoked or expired: the provider stops returning the action.
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Assistant));
        var afterRevocation = await Authorize(Account(TeacherId), CourseK);

        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher) with { Status = MembershipStatus.Left });
        var afterLeaving = await Authorize(Account(TeacherId), CourseK);

        Assert.Multiple(() =>
        {
            Assert.That(before.IsAllowed, Is.True);
            Assert.That(afterRevocation, Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
            Assert.That(afterLeaving, Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
            Assert.That(memberships.Calls, Is.EqualTo(3), "Membership is read on every call, never cached.");
        });
    }

    [Test]
    public async Task Administrator_WithoutTeacherMembershipInK_IsForbidden()
    {
        // Access has no global administrator right for course audit (D4); only course membership counts.
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher, accountId: AdminId, courseId: CourseL));

        Assert.That(await Authorize(Account(AdminId), CourseK), Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
    }

    [Test]
    public async Task BlockedAccount_IsNotAllowed_EvenAsTeacher()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher));

        var result = await Authorize(Account(TeacherId, AccountState.Blocked), CourseK);

        Assert.That(result.IsAllowed, Is.False);
    }

    [Test]
    public async Task Anonymous_IsUnauthenticated_WithoutASecondDecision()
    {
        var decision = new ScriptedAccessDecision();

        var result = await new CourseAuditReadAuthorizer(decision).AuthorizeAsync(null, CourseK, Correlation);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(CourseAuditReadAuthorization.Unauthenticated));
            Assert.That(decision.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task Decision_UsesTheCatalogActionTheRouteCourseAndTheRequestCorrelation()
    {
        var decision = new ScriptedAccessDecision();

        await new CourseAuditReadAuthorizer(decision).AuthorizeAsync(Account(TeacherId), CourseK, Correlation);

        var request = decision.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Action, Is.EqualTo("audit.events.read"));
            Assert.That(request.Resource, Is.EqualTo(new ResourceRef("course", CourseK.ToString("D"))));
            Assert.That(request.CorrelationId, Is.EqualTo(Correlation));
            Assert.That(request.Subject, Is.EqualTo(Account(TeacherId)));
        });
    }

    [Test]
    public async Task EmptyCourse_IsForbidden_WithoutADecision()
    {
        var decision = new ScriptedAccessDecision();

        var result = await new CourseAuditReadAuthorizer(decision).AuthorizeAsync(Account(TeacherId), Guid.Empty, Correlation);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
            Assert.That(decision.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task AllowForANonAccountSubject_IsStillForbidden()
    {
        // The viewer is recorded as a User; a demo subject such as "dev-user" is never turned into one.
        var decision = new ScriptedAccessDecision();

        var result = await new CourseAuditReadAuthorizer(decision)
            .AuthorizeAsync(new SubjectRef("dev-user", AccountState.Active), CourseK, Correlation);

        Assert.That(result, Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
    }

    [Test]
    public async Task DecisionThatThrows_IsForbidden()
    {
        var decision = new ScriptedAccessDecision
        {
            Handler = (_, _) => throw new InvalidOperationException("Host=db;Password=s3cret")
        };

        var result = await new CourseAuditReadAuthorizer(decision).AuthorizeAsync(Account(TeacherId), CourseK, Correlation);

        Assert.That(result, Is.EqualTo(CourseAuditReadAuthorization.Forbidden));
    }

    [Test]
    public async Task DenyWithUnauthenticatedReason_Is401()
    {
        var decision = new ScriptedAccessDecision
        {
            Handler = (request, _) => ValueTask.FromResult(
                AccessDecision.Deny(AccessDenialReason.Unauthenticated, request.CorrelationId))
        };

        var result = await new CourseAuditReadAuthorizer(decision).AuthorizeAsync(Account(TeacherId), CourseK, Correlation);

        Assert.That(result, Is.EqualTo(CourseAuditReadAuthorization.Unauthenticated));
    }

    [Test]
    public void CallerCancellation_Propagates()
    {
        var decision = new ScriptedAccessDecision
        {
            Handler = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return ValueTask.FromResult(AccessDecision.Allow("audit.course.read", Correlation));
            }
        };
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await new CourseAuditReadAuthorizer(decision).AuthorizeAsync(Account(TeacherId), CourseK, Correlation, cancelled.Token));
    }

    [Test]
    public async Task ParallelSubjects_GetTheirOwnResults()
    {
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher));
        memberships.Set(AuditAuthorizationExamples.Membership(CourseRole.Teacher, accountId: OtherAccountId, courseId: CourseL));

        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => i % 2 == 0
            ? Authorize(Account(TeacherId), CourseK).AsTask()
            : Authorize(Account(OtherAccountId), CourseK).AsTask()));

        Assert.Multiple(() =>
        {
            for (var i = 0; i < results.Length; i++)
            {
                Assert.That(results[i], Is.EqualTo(i % 2 == 0
                    ? CourseAuditReadAuthorization.Allowed(TeacherId)
                    : CourseAuditReadAuthorization.Forbidden));
            }
        });
    }
}
