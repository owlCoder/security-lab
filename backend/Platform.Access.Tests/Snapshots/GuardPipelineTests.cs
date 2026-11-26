using Platform.Access.Application.Snapshots;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Guards;
using Platform.Access.Tests.Support;
using System.Text.Json;

namespace Platform.Access.Tests.Snapshots;

[TestFixture]
public sealed class GuardPipelineTests
{
    private static readonly string Action = AccessActions.AttemptRead.Id;
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Course = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherAccount = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    // Temporary pipeline wrapper until NT supplies ContextualAccessPolicy.
    private sealed class LoaderGuardPolicy(IEvaluationSnapshotLoader loader) : IOutcomeAccessPolicy
    {
        public string Id => "guard-pipeline-test";
        public string Version => "v1";

        public ValueTask<bool> EvaluateAsync(
            AccessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public async ValueTask<PolicyResult> EvaluateOutcomeAsync(
            AccessRequest request,
            CancellationToken cancellationToken)
        {
            var snapshot = await loader.LoadAsync(request, cancellationToken);
            return new OwnershipScopeGuard().Evaluate(snapshot, request.Action);
        }
    }

    private sealed class Harness
    {
        public required EngineFixture Fixture { get; init; }
        public required ManualTimeProvider Clock { get; init; }
        public required MutableMembership Membership { get; init; }

        public Task<AccessDecision> Decide(AccessRequest? request = null) =>
            Fixture.Engine().DecideAsync(request ?? Request()).AsTask();
    }

    private sealed class MutableMembership
    {
        public CourseRole Role { get; set; } = CourseRole.Student;
        public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    }

    private static Harness Build(
        Guid? ownerId = null,
        bool resourceExists = true,
        MutableMembership? membership = null)
    {
        var mutableMembership = membership ?? new MutableMembership();
        var clock = new ManualTimeProvider(Now);
        var reader = new FakeMembershipReader((accountId, courseId, _) =>
            ValueTask.FromResult<CourseMembership?>(
                new CourseMembership(
                    accountId,
                    courseId,
                    mutableMembership.Role,
                    mutableMembership.Status,
                    new HashSet<string>())));
        var resolver = new FakeResourceResolver("attempt", (resource, _) =>
            ValueTask.FromResult<ResourceDescriptor?>(resourceExists
                ? new ResourceDescriptor(
                    "attempt",
                    resource.Id,
                    Course,
                    ownerId ?? Account,
                    "InProgress",
                    new Dictionary<string, AttributeValue>())
                : null));
        var loader = new EvaluationSnapshotLoader(reader, [resolver], new FakeGrantReader(), clock);
        var fixture = new EngineFixture()
            .WithPolicy(Action, new LoaderGuardPolicy(loader));

        return new Harness { Fixture = fixture, Clock = clock, Membership = mutableMembership };
    }

    private static AccessRequest Request() =>
        AccessRequestBuilder.Default()
            .WithAction(Action)
            .WithSubject(new SubjectRef(Account.ToString("D"), AccountState.Active))
            .WithResource(new ResourceRef("attempt", "attempt-pipeline-test"))
            .Build();

    [Test]
    [Property("RequirementId", "ACC-08")]
    public async Task ACC08_GuardPipeline_MembershipRevokedBetweenDecisions_DenyNext()
    {
        var harness = Build();

        var first = await harness.Decide();
        harness.Membership.Status = MembershipStatus.Left;
        var second = await harness.Decide();

        Assert.That(first.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(second.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(second.DenialReason, Is.EqualTo(AccessDenialReason.NotPermitted));
    }

    [TestCase(null, true, AccessOutcome.Allow, AccessDenialReason.None)]
    [TestCase("33333333-3333-3333-3333-333333333333", true,
        AccessOutcome.Deny, AccessDenialReason.NotPermitted)]
    [TestCase(null, false, AccessOutcome.Deny, AccessDenialReason.MissingAttribute)]
    [Property("RequirementId", "ACC-08")]
    public async Task ACC08_GuardPipeline_OutcomeReason_IsMappedToAccessDecision(
        string? ownerId,
        bool resourceExists,
        AccessOutcome expectedOutcome,
        AccessDenialReason expectedReason)
    {
        var harness = Build(
            ownerId is null ? null : Guid.Parse(ownerId),
            resourceExists);

        var decision = await harness.Decide();

        Assert.That(decision.Outcome, Is.EqualTo(expectedOutcome));
        Assert.That(decision.DenialReason, Is.EqualTo(expectedReason));
    }

    [Test]
    [Property("RequirementId", "ACC-08")]
    public async Task ACC08_GuardPipeline_AccessDecisionDoesNotExposeTraceOrIds_Deny()
    {
        var decision = await Build(ownerId: OtherAccount).Decide();
        var json = JsonSerializer.Serialize(decision);
        var propertyTypes = typeof(AccessDecision).GetProperties()
            .Select(property => property.PropertyType)
            .ToArray();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(propertyTypes, Does.Not.Contain(typeof(DecisionTrace)));
        Assert.That(json, Does.Not.Contain(Account.ToString("D")));
        Assert.That(json, Does.Not.Contain("attempt-pipeline-test"));
        Assert.That(json, Does.Not.Contain(nameof(DecisionTrace)));
        Assert.That(json, Does.Not.Contain("TraceStep"));
    }
}
