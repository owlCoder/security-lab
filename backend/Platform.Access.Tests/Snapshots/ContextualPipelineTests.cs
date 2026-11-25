using Platform.Access.Application.Rules;
using Platform.Access.Application.Snapshots;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Snapshots;

/// <summary>
/// Ceo tok koji je u planu opisan po delovima: loader, skup pravila i engine zajedno.
/// Politika ovde je samo test-wrapper (nije <c>ContextualAccessPolicy</c> iz NT dela) i skup pravila je ilustracija.
/// </summary>
[TestFixture]
[Property("RequirementId", "ACC-08")]
public class ContextualPipelineTests
{
    private static readonly string Action = AccessActions.AttemptRead.Id;
    private static readonly DateTimeOffset Deadline = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Account = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Course = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class LoaderRuleBackedPolicy(IEvaluationSnapshotLoader loader, RuleSet<EvaluationSnapshot> rules)
        : IOutcomeAccessPolicy
    {
        public string Id => "pipeline-test";
        public string Version => "v1";

        public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public async ValueTask<PolicyResult> EvaluateOutcomeAsync(AccessRequest request, CancellationToken cancellationToken)
        {
            var snapshot = await loader.LoadAsync(request, cancellationToken);
            return rules.Evaluate(snapshot).ToPolicyResult();
        }
    }

    private sealed class Source
    {
        public Dictionary<string, AttributeValue> Attributes { get; set; } = Open();
        public Func<ResourceDescriptor?>? Override { get; set; }

        public static Dictionary<string, AttributeValue> Open() => new()
        {
            ["Deadline"] = AttributeValue.Of(Deadline),
            ["AssignmentActive"] = AttributeValue.Of(true),
            ["CourseArchived"] = AttributeValue.Of(false)
        };
    }

    private static RuleSet<EvaluationSnapshot> Rules() => new(
        [
            new Rule<EvaluationSnapshot>("r-deadline", SnapshotConditions.NowBefore("Deadline")),
            new Rule<EvaluationSnapshot>("r-assignment", SnapshotConditions.AttrEquals("AssignmentActive", true)),
        ],
        [
            new Rule<EvaluationSnapshot>("p-archived", SnapshotConditions.AttrEquals("CourseArchived", true)),
        ]);

    private sealed class Harness
    {
        public required FakeResourceResolver Resolver { get; init; }
        public required ManualTimeProvider Clock { get; init; }
        public required EngineFixture Fixture { get; init; }
        public required Source Data { get; init; }

        public Task<AccessDecision> Decide(
            AccessRequest? request = null,
            TimeSpan? policyTimeout = null,
            CancellationToken token = default) =>
            Fixture.Engine(policyTimeout: policyTimeout)
                .DecideAsync(request ?? Request(), token).AsTask();
    }

    private static Harness Build(
        Func<ResourceRef, CancellationToken, ValueTask<ResourceDescriptor?>>? resolverOverride = null,
        DateTimeOffset? now = null)
    {
        var data = new Source();
        var clock = new ManualTimeProvider(now ?? Deadline.AddMinutes(-30));
        var resolver = new FakeResourceResolver("attempt", resolverOverride ?? ((resource, _) =>
            ValueTask.FromResult<ResourceDescriptor?>(
                data.Override?.Invoke() ??
                new ResourceDescriptor(resource.Type, resource.Id, Course, Account, "InProgress", data.Attributes))));
        var loader = new EvaluationSnapshotLoader(new FakeMembershipReader(), [resolver], new FakeGrantReader(), clock);
        var fixture = new EngineFixture().WithPolicy(Action, new LoaderRuleBackedPolicy(loader, Rules()));

        return new Harness { Resolver = resolver, Clock = clock, Fixture = fixture, Data = data };
    }

    private static AccessRequest Request(AccountState state = AccountState.Active) =>
        AccessRequestBuilder.Default()
            .WithAction(Action)
            .WithSubject(new SubjectRef(Account.ToString("D"), state))
            .WithResource(new ResourceRef("attempt", "att-1"))
            .Build();

    // ---------- Pozitivan i negativan ishod kroz ceo tok ----------

    [Test]
    public async Task ACC08_Pipeline_ConditionsMet_Allowed()
    {
        var decision = await Build().Decide();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(decision.PolicyId, Is.EqualTo("pipeline-test"));
    }

    [Test]
    public async Task ACC08_Pipeline_ProhibitionActive_DeniedAsContextMismatch()
    {
        var harness = Build();
        harness.Data.Attributes["CourseArchived"] = AttributeValue.Of(true);

        var decision = await harness.Decide();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.ContextMismatch));
    }

    // ---------- Vreme: samo sa servera, granica ±1 s ----------

    [TestCase(-1, AccessOutcome.Allow)]
    [TestCase(0, AccessOutcome.Deny)]
    [TestCase(1, AccessOutcome.Deny)]
    public async Task ACC08_Pipeline_DeadlineBoundary_UsesServerClock(int secondsFromDeadline, AccessOutcome expected)
    {
        var harness = Build(now: Deadline.AddSeconds(secondsFromDeadline));

        var decision = await harness.Decide();

        Assert.That(decision.Outcome, Is.EqualTo(expected));
        if (expected == AccessOutcome.Deny)
        {
            Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.ContextMismatch));
        }
    }

    [Test]
    public async Task ACC08_Pipeline_ClockMovesBetweenDecisions_NextDecisionSeesIt()
    {
        var harness = Build(now: Deadline.AddSeconds(-1));

        var before = await harness.Decide();
        harness.Clock.Advance(TimeSpan.FromSeconds(2));
        var after = await harness.Decide();

        Assert.That(before.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(after.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC08_Pipeline_SourceChangedBetweenDecisions_NextDecisionSeesIt()
    {
        var harness = Build();

        var first = await harness.Decide();
        harness.Data.Attributes = Source.Open();
        harness.Data.Attributes["AssignmentActive"] = AttributeValue.Of(false);
        var second = await harness.Decide();

        Assert.That(first.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(second.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    // ---------- Nedostajući podaci ----------

    [Test]
    public async Task ACC08_Pipeline_ResourceNotFound_DeniedAsMissingAttribute()
    {
        var harness = Build((_, _) => ValueTask.FromResult<ResourceDescriptor?>(null));

        var decision = await harness.Decide();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public async Task ACC08_Pipeline_MissingAttribute_DeniedAsMissingAttribute()
    {
        var harness = Build();
        harness.Data.Attributes.Remove("Deadline");

        var decision = await harness.Decide();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public async Task ACC08_Pipeline_MissingProhibitionAttribute_NeverAllowed()
    {
        var harness = Build();
        harness.Data.Attributes.Remove("CourseArchived");

        var decision = await harness.Decide();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    // ---------- Greške i spori izvori (T3, T15) ----------

    [Test]
    public async Task ACC08_Pipeline_ResolverThrows_DeniedAsPolicyError()
    {
        var harness = Build((_, _) => throw new InvalidOperationException("resolver down"));

        var decision = await harness.Decide();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC08_Pipeline_ResolverFaultsAsynchronously_DeniedAsPolicyError()
    {
        var harness = Build(async (_, _) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("resolver down");
        });

        var decision = await harness.Decide();

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC08_Pipeline_ResolverIgnoringCancellation_DeniedWithinPolicyTimeout()
    {
        var harness = Build((_, _) => new ValueTask<ResourceDescriptor?>(new TaskCompletionSource<ResourceDescriptor?>().Task));
        var started = DateTime.UtcNow;

        var decision = await harness.Decide(policyTimeout: TimeSpan.FromMilliseconds(150)).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task ACC08_Pipeline_CallerCancelsWhileLoading_DeniedNotThrown()
    {
        var harness = Build((_, _) => new ValueTask<ResourceDescriptor?>(new TaskCompletionSource<ResourceDescriptor?>().Task));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var decision = await harness.Decide(token: cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    // ---------- Engine ne učitava podatke za nalog koji ne sme ni da dođe do politike ----------

    [TestCase(AccountState.Blocked)]
    [TestCase(AccountState.Archived)]
    public async Task ACC08_Pipeline_NonActiveAccount_NoDataIsLoaded(AccountState state)
    {
        var harness = Build();

        var decision = await harness.Decide(Request(state));

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.AccountNotActive));
        Assert.That(harness.Resolver.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task ACC08_Pipeline_Anonymous_NoDataIsLoaded()
    {
        var harness = Build();
        var anonymous = AccessRequestBuilder.Default().WithAction(Action).Anonymous()
            .WithResource(new ResourceRef("attempt", "att-1")).Build();

        var decision = await harness.Decide(anonymous);

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.Unauthenticated));
        Assert.That(harness.Resolver.Calls, Is.EqualTo(0));
    }

    [Test]
    public async Task ACC08_Pipeline_ResolverForOtherResource_DeniedAsPolicyError()
    {
        var harness = Build();
        harness.Data.Override = () => new ResourceDescriptor("attempt", "someone-elses", Course, Account, "InProgress", Source.Open());

        var decision = await harness.Decide();

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }
}
