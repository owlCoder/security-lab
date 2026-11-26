using NUnit.Framework;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Grants;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Grants;

[TestFixture]
[Property("RequirementId", "ACC-08-05")]
public sealed class ResultsSensitiveReadPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Action = AccessActions.ResultsSensitiveRead.Id;

    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid courseId = Guid.NewGuid();
    private readonly ResourceRef attempt = new("attempt", "attempt-9");

    private SubjectRef Admin => new(adminId.ToString("D"), AccountState.Active, SystemRole.Administrator);

    private ResourceDescriptor Resource(Guid? course) =>
        new("attempt", "attempt-9", course, Guid.NewGuid(), null, new Dictionary<string, AttributeValue>());

    private ElevatedGrant Grant(
        DateTimeOffset? expiresAt = null,
        Guid? scopeCourse = null,
        Guid? holder = null) =>
        new(
            Guid.NewGuid(),
            holder ?? adminId,
            Guid.NewGuid(),
            Action,
            new GrantScope(GrantScopeKind.Course, (scopeCourse ?? courseId).ToString("D")),
            "incident review",
            Now.AddHours(-1),
            expiresAt ?? Now.AddHours(1),
            false);

    private AccessRequest Request(string? action = null) =>
        AccessRequestBuilder.Default()
            .WithSubject(Admin)
            .WithAction(action ?? Action)
            .WithResource(attempt)
            .Build();

    private static ResultsSensitiveReadPolicy PolicyFor(EvaluationSnapshot snapshot) =>
        new(new FixedSnapshotLoader(snapshot), new ElevatedGrantValidator());

    private EvaluationSnapshot Snapshot(
        SubjectRef? subject = null,
        bool resourcePresent = true,
        params ElevatedGrant[] grants) =>
        EvaluationSnapshotBuilder.Default()
            .WithSubject(subject ?? Admin)
            .WithResource(resourcePresent ? Resource(courseId) : null)
            .WithGrants(grants)
            .WithNow(Now)
            .Build();

    [Test]
    public async Task AdministratorWithoutGrant_IsDenied()
    {
        var result = await PolicyFor(Snapshot()).EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.NotPermitted));
        Assert.That(result.Trace.Steps.Last().Id, Is.EqualTo("grant.holder"));
    }

    [Test]
    public async Task AdministratorWithValidGrant_IsPermitted()
    {
        var result = await PolicyFor(Snapshot(grants: Grant())).EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Permit));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.None));
        Assert.That(result.Trace.Steps.Select(step => step.Result), Is.All.EqualTo(TraceResult.Passed));
    }

    [Test]
    public async Task AdministratorWithExpiredGrant_IsDenied()
    {
        var result = await PolicyFor(Snapshot(grants: Grant(expiresAt: Now)))
            .EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    public async Task AdministratorWithGrantForAnotherCourse_IsDenied()
    {
        var result = await PolicyFor(Snapshot(grants: Grant(scopeCourse: Guid.NewGuid())))
            .EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    public async Task AdministratorWithGrantOfAnotherHolder_IsDenied()
    {
        var result = await PolicyFor(Snapshot(grants: Grant(holder: Guid.NewGuid())))
            .EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    public async Task NonAdministratorWithValidGrant_IsDenied()
    {
        var subject = Admin with { SystemRole = SystemRole.None };

        var result = await PolicyFor(Snapshot(subject, grants: Grant()))
            .EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.NotPermitted));
        Assert.That(result.Trace.Steps.Last().Id, Is.EqualTo("subject.administrator"));
    }

    [Test]
    public async Task MissingResource_IsDeniedAsMissingAttribute()
    {
        var result = await PolicyFor(Snapshot(resourcePresent: false, grants: Grant()))
            .EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public async Task ResourceWithoutCourse_IsDeniedAsMissingAttribute()
    {
        var snapshot = Snapshot(grants: Grant()) with { Resource = Resource(null) };

        var result = await PolicyFor(snapshot).EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public async Task ResourceWithEmptyCourse_IsDeniedAsMissingAttribute()
    {
        var snapshot = Snapshot(grants: Grant()) with { Resource = Resource(Guid.Empty) };

        var result = await PolicyFor(snapshot).EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [TestCase("not-a-guid")]
    [TestCase("00000000-0000-0000-0000-000000000000")]
    public async Task SubjectWithInvalidId_IsDeniedAsMissingAttribute(string subjectId)
    {
        var subject = Admin with { Id = subjectId };

        var result = await PolicyFor(Snapshot(subject, grants: Grant()))
            .EvaluateOutcomeAsync(Request(), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public async Task OtherAction_IsDeniedAsContextMismatch()
    {
        var result = await PolicyFor(Snapshot(grants: Grant()))
            .EvaluateOutcomeAsync(Request(AccessActions.AttemptRead.Id), default);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.ContextMismatch));
    }

    [Test]
    public async Task TraceContainsOnlyStepIdentifiers()
    {
        var result = await PolicyFor(Snapshot(grants: Grant())).EvaluateOutcomeAsync(Request(), default);

        Assert.That(
            result.Trace.Steps.Select(step => step.Id),
            Is.All.Matches(@"^[a-z0-9._:-]{1,64}$"));
    }

    [Test]
    public async Task BooleanEvaluation_MatchesOutcome()
    {
        var permitted = await PolicyFor(Snapshot(grants: Grant())).EvaluateAsync(Request(), default);
        var denied = await PolicyFor(Snapshot()).EvaluateAsync(Request(), default);

        Assert.That(permitted, Is.True);
        Assert.That(denied, Is.False);
    }

    [Test]
    public void PolicyIdentity_IsStable()
    {
        var policy = PolicyFor(Snapshot());

        Assert.That(policy.Id, Is.EqualTo("elevated-grant.results.sensitive.read"));
        Assert.That(policy.Version, Is.EqualTo("v1"));
    }

    [Test]
    public async Task Engine_AllowsAdministratorWithValidGrant()
    {
        var fixture = new EngineFixture().WithPolicy(Action, PolicyFor(Snapshot(grants: Grant())));

        var decision = await fixture.Engine().DecideAsync(Request());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(decision.PolicyId, Is.EqualTo("elevated-grant.results.sensitive.read"));
    }

    [Test]
    public async Task Engine_DeniesAdministratorWithoutGrant()
    {
        var fixture = new EngineFixture().WithPolicy(Action, PolicyFor(Snapshot()));

        var decision = await fixture.Engine().DecideAsync(Request());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task Engine_DeniesWithPolicyErrorWhenSnapshotLoadingFails()
    {
        var policy = new ResultsSensitiveReadPolicy(new ThrowingSnapshotLoader(), new ElevatedGrantValidator());
        var fixture = new EngineFixture().WithPolicy(Action, policy);

        var decision = await fixture.Engine().DecideAsync(Request());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    private sealed class FixedSnapshotLoader(EvaluationSnapshot snapshot) : IEvaluationSnapshotLoader
    {
        public ValueTask<EvaluationSnapshot> LoadAsync(AccessRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(snapshot);
    }

    private sealed class ThrowingSnapshotLoader : IEvaluationSnapshotLoader
    {
        public ValueTask<EvaluationSnapshot> LoadAsync(AccessRequest request, CancellationToken cancellationToken) =>
            throw new IOException("snapshot source down");
    }
}
