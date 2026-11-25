using Platform.Access.Application.Rules;
using Platform.Access.Contracts;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests;

/// <summary>Engine i politike sa bogatim ishodom (<see cref="IOutcomeAccessPolicy"/>), ACC-08.</summary>
[TestFixture]
[Property("RequirementId", "ACC-08")]
public class OutcomePolicyEngineTests
{
    private static readonly string LabRead = AccessActions.LabRead.Id;

    private static async Task<AccessDecision> Decide(FakeOutcomePolicy policy, AccessRequest? request = null)
    {
        var fx = new EngineFixture().WithPolicy(LabRead, policy);
        return await fx.Engine().DecideAsync(request ?? AccessRequestBuilder.Default().Build());
    }

    // ---------- Permit ----------

    [Test]
    public async Task ACC08_Outcome_Permit_AllowedWithPolicyIdentityAndCorrelation()
    {
        var policy = new FakeOutcomePolicy(
            (_, _) => ValueTask.FromResult(FakeOutcomePolicy.Result(PolicyOutcome.Permit, AccessDenialReason.None)),
            "p-ctx", "v3");

        var decision = await Decide(policy, AccessRequestBuilder.Default().WithCorrelationId("corr-7").Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(decision.PolicyId, Is.EqualTo("p-ctx"));
        Assert.That(decision.PolicyVersion, Is.EqualTo("v3"));
        Assert.That(decision.CorrelationId, Is.EqualTo("corr-7"));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.None));
    }

    [Test]
    public async Task ACC08_Outcome_UsesOutcomeMethodNotTheBoolMethod()
    {
        var policy = FakeOutcomePolicy.Returning(PolicyOutcome.Deny, AccessDenialReason.NotPermitted);

        var decision = await Decide(policy);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(policy.OutcomeCalls, Is.EqualTo(1));
        Assert.That(policy.BoolCalls, Is.EqualTo(0));
    }

    [TestCase(AccessDenialReason.NotPermitted)]
    [TestCase(AccessDenialReason.ContextMismatch)]
    [TestCase(AccessDenialReason.MissingAttribute)]
    [TestCase(AccessDenialReason.PolicyError)]
    [TestCase(AccessDenialReason.Unauthenticated)]
    [TestCase((AccessDenialReason)99)]
    public async Task ACC08_Outcome_PermitWithADenialReason_IsContradictory_DeniedAsPolicyError(AccessDenialReason reason)
    {
        var decision = await Decide(FakeOutcomePolicy.Returning(PolicyOutcome.Permit, reason));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    // ---------- Deny i Indeterminate ----------

    [TestCase(AccessDenialReason.NotPermitted)]
    [TestCase(AccessDenialReason.ContextMismatch)]
    [TestCase(AccessDenialReason.MissingAttribute)]
    [TestCase(AccessDenialReason.PolicyDenied)]
    [TestCase(AccessDenialReason.PolicyError)]
    public async Task ACC08_Outcome_DenyKeepsThePoliciesOwnReasonAndIdentity(AccessDenialReason reason)
    {
        var decision = await Decide(FakeOutcomePolicy.Returning(PolicyOutcome.Deny, reason));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(reason));
        Assert.That(decision.PolicyId, Is.EqualTo("fake-outcome-policy"));
        Assert.That(decision.PolicyVersion, Is.EqualTo("v1"));
    }

    [Test]
    public async Task ACC08_Outcome_Indeterminate_IsDeny_WithMissingAttributeByDefault()
    {
        var decision = await Decide(FakeOutcomePolicy.Returning(PolicyOutcome.Indeterminate, AccessDenialReason.None));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public async Task ACC08_Outcome_IndeterminateKeepsAnAllowedExplicitReason()
    {
        var decision = await Decide(FakeOutcomePolicy.Returning(PolicyOutcome.Indeterminate, AccessDenialReason.ContextMismatch));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.ContextMismatch));
    }

    [Test]
    public async Task ACC08_Outcome_DenyWithoutReason_IsNotPermitted()
    {
        var decision = await Decide(FakeOutcomePolicy.Returning(PolicyOutcome.Deny, AccessDenialReason.None));

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.NotPermitted));
    }

    [TestCase(AccessDenialReason.Unauthenticated)]
    [TestCase(AccessDenialReason.AccountNotActive)]
    [TestCase(AccessDenialReason.UnknownAction)]
    [TestCase(AccessDenialReason.NoPolicy)]
    [TestCase((AccessDenialReason)99)]
    public async Task ACC08_Outcome_PolicyCannotImpersonateReasonsOwnedByTheEngine(AccessDenialReason reason)
    {
        var decision = await Decide(FakeOutcomePolicy.Returning(PolicyOutcome.Deny, reason));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.NotPermitted));
    }

    [Test]
    public async Task ACC08_Outcome_UndefinedOutcomeValue_DeniedAsPolicyError()
    {
        var decision = await Decide(FakeOutcomePolicy.Returning((PolicyOutcome)99, AccessDenialReason.None));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC08_Outcome_NullResult_DeniedAsPolicyError()
    {
        var policy = new FakeOutcomePolicy((_, _) => ValueTask.FromResult<PolicyResult>(null!));

        var decision = await Decide(policy);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC08_Outcome_Exhaustive_AllowOnlyForPermitWithoutReason_AndDenyNeverCarriesEngineReasons()
    {
        var outcomes = new[] { PolicyOutcome.Permit, PolicyOutcome.Deny, PolicyOutcome.Indeterminate, (PolicyOutcome)99 };
        var reasons = Enum.GetValues<AccessDenialReason>().Append((AccessDenialReason)99).ToArray();
        var engineOwned = new[]
        {
            AccessDenialReason.None, AccessDenialReason.Unauthenticated, AccessDenialReason.AccountNotActive,
            AccessDenialReason.UnknownAction, AccessDenialReason.NoPolicy
        };

        foreach (var outcome in outcomes)
        {
            foreach (var reason in reasons)
            {
                var decision = await Decide(FakeOutcomePolicy.Returning(outcome, reason));
                var label = $"{outcome}/{reason}";

                Assert.That(decision.Outcome == AccessOutcome.Allow,
                    Is.EqualTo(outcome == PolicyOutcome.Permit && reason == AccessDenialReason.None), label);

                if (decision.Outcome == AccessOutcome.Deny)
                {
                    Assert.That(engineOwned.Contains(decision.DenialReason), Is.False, label);
                }
            }
        }
    }

    // ---------- Greške, timeout, otkazivanje ----------

    [Test]
    public async Task ACC08_Outcome_PolicyThrows_DeniedAsPolicyError()
    {
        var sync = new FakeOutcomePolicy((_, _) => throw new InvalidOperationException("boom"));
        var async = new FakeOutcomePolicy(async (_, _) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async boom");
        });
        var cancelled = new FakeOutcomePolicy((_, _) => throw new OperationCanceledException());

        foreach (var policy in new[] { sync, async, cancelled })
        {
            var decision = await Decide(policy);

            Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
            Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
        }
    }

    [Test]
    public async Task ACC08_Outcome_CooperativeSlowPolicy_TimesOutAsDeny()
    {
        var policy = new FakeOutcomePolicy(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return FakeOutcomePolicy.Result(PolicyOutcome.Permit, AccessDenialReason.None);
        });
        var fx = new EngineFixture().WithPolicy(LabRead, policy);

        var decision = await fx.Engine(policyTimeout: TimeSpan.FromMilliseconds(100))
            .DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC08_Outcome_PolicyIgnoringCancellation_StillDeniedWithinTimeout()
    {
        var policy = new FakeOutcomePolicy((_, _) =>
            new ValueTask<PolicyResult>(new TaskCompletionSource<PolicyResult>().Task));
        var fx = new EngineFixture().WithPolicy(LabRead, policy);
        var started = DateTime.UtcNow;

        var decision = await fx.Engine(policyTimeout: TimeSpan.FromMilliseconds(150))
            .DecideAsync(AccessRequestBuilder.Default().Build())
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task ACC08_Outcome_AlreadyCancelledToken_DeniedNotThrown()
    {
        var fx = new EngineFixture().WithPolicy(LabRead,
            FakeOutcomePolicy.Returning(PolicyOutcome.Permit, AccessDenialReason.None));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build(), cts.Token);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    // ---------- Kapije engine-a važe i dalje, pre politike ----------

    [Test]
    public async Task ACC08_Outcome_NonActiveAccount_PolicyIsNeverCalled()
    {
        var policy = FakeOutcomePolicy.Returning(PolicyOutcome.Permit, AccessDenialReason.None);

        var decision = await Decide(policy, AccessRequestBuilder.Default().WithState(AccountState.Blocked).Build());

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.AccountNotActive));
        Assert.That(policy.OutcomeCalls + policy.BoolCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task ACC08_Outcome_AnonymousOnProtectedAction_PolicyIsNeverCalled()
    {
        var policy = FakeOutcomePolicy.Returning(PolicyOutcome.Permit, AccessDenialReason.None);

        var decision = await Decide(policy, AccessRequestBuilder.Default().Anonymous().Build());

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.Unauthenticated));
        Assert.That(policy.OutcomeCalls + policy.BoolCalls, Is.EqualTo(0));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task ACC08_Outcome_BlankPolicyIdOrVersion_DeniedAndNotCalled(string? blank)
    {
        var policy = new FakeOutcomePolicy(
            (_, _) => ValueTask.FromResult(FakeOutcomePolicy.Result(PolicyOutcome.Permit, AccessDenialReason.None)),
            id: blank!);
        var registry = new SingleRegistry(policy);
        var engine = EngineFixture.Build(new EngineFixture().Catalog, registry, new RecordingAuditWriter());

        var decision = await engine.DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(policy.OutcomeCalls, Is.EqualTo(0));
    }

    private sealed class SingleRegistry(IAccessPolicy policy) : IAccessPolicyRegistry
    {
        public bool TryGet(string action, out IAccessPolicy? found)
        {
            found = policy;
            return true;
        }
    }

    // ---------- Audit i curenje traga ----------

    [Test]
    public async Task ACC08_Outcome_DecisionIsAuditedAndAuditFailureDoesNotChangeIt()
    {
        var policy = FakeOutcomePolicy.Returning(PolicyOutcome.Permit, AccessDenialReason.None);
        var recording = new EngineFixture().WithPolicy(LabRead, policy);
        var recorded = await recording.Engine().DecideAsync(AccessRequestBuilder.Default().Build());

        var failing = new EngineFixture().WithPolicy(LabRead, FakeOutcomePolicy.Returning(PolicyOutcome.Permit, AccessDenialReason.None));
        var withFailingAudit = await failing.Engine(audit: new ThrowingAuditWriter())
            .DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(recording.Audit.Entries.Count, Is.EqualTo(1));
        Assert.That(recording.Audit.Entries[0].Decision, Is.EqualTo(recorded));
        Assert.That(withFailingAudit.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public void ACC08_Outcome_AccessDecisionHasNoTraceField()
    {
        var names = typeof(AccessDecision).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.That(names, Is.EqualTo(new[] { "CorrelationId", "DenialReason", "Outcome", "PolicyId", "PolicyVersion" }));
        Assert.That(typeof(AccessDecision).GetProperties().Any(p => p.PropertyType == typeof(DecisionTrace)), Is.False);
    }

    // ---------- Zajedno sa skupom pravila ----------

    private sealed class RuleBackedPolicy(RuleSet<int> rules) : IOutcomeAccessPolicy
    {
        public string Id => "rule-backed";
        public string Version => "v1";

        public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);

        public ValueTask<PolicyResult> EvaluateOutcomeAsync(AccessRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(rules.Evaluate(0).ToPolicyResult());
    }

    private static async Task<AccessDecision> DecideWithRules(Rule<int>[] requirements, params Rule<int>[] prohibitions)
    {
        var fx = new EngineFixture().WithPolicy(LabRead, new RuleBackedPolicy(new RuleSet<int>(requirements, prohibitions)));
        return await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build());
    }

    [Test]
    public async Task ACC08_Outcome_RuleSetPermit_BecomesAllow()
    {
        var decision = await DecideWithRules(
            [RuleTestData.Rule("r1", Tri.True)], RuleTestData.Rule("p1", Tri.False));

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(decision.PolicyId, Is.EqualTo("rule-backed"));
    }

    [Test]
    public async Task ACC08_Outcome_RuleSetMissingValue_BecomesDenyMissingAttribute()
    {
        var decision = await DecideWithRules([RuleTestData.Rule("r1", Tri.Unknown)]);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public async Task ACC08_Outcome_RuleSetProhibitionOrUnmetRequirement_BecomesDenyContextMismatch()
    {
        var prohibition = await DecideWithRules([RuleTestData.Rule("r1", Tri.True)], RuleTestData.Rule("p1", Tri.True));
        var unmet = await DecideWithRules([RuleTestData.Rule("r1", Tri.False)]);

        Assert.That(prohibition.DenialReason, Is.EqualTo(AccessDenialReason.ContextMismatch));
        Assert.That(unmet.DenialReason, Is.EqualTo(AccessDenialReason.ContextMismatch));
    }
}
