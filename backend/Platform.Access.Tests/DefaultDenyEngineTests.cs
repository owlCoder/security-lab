using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Tests.Support;
using NUnit.Framework;

namespace Platform.Access.Tests;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public class DefaultDenyEngineTests
{
    private static readonly string LabRead = AccessActions.LabRead.Id;

    public static IEnumerable<string> InvalidActions() =>
    [
        "",
        " ",
        "Lab.Create",
        "lab.create ",
        " lab.read",
        "lab.read\n",
        "lab.read\r\n",
        "lab.read\0",
        "lab..read",
        "lab",
        "lab/read",
        "../etc/passwd",
        "lab.read'--",
        "<script>",
        "ｌａｂ.ｒｅａｄ",
        "lаb.read",
        "lab.create",
        new string('a', 10_000),
    ];

    [TestCaseSource(nameof(InvalidActions))]
    public async Task ACC05_InvalidOrUnknownAction_DeniedAsUnknownAction(string action)
    {
        var fx = new EngineFixture().WithPermitAllPolicies();

        var decision = await fx.Engine().DecideAsync(
            AccessRequestBuilder.Default().WithAction(action).Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.UnknownAction));
    }

    [Test]
    public async Task ACC05_NullAction_DeniedAsUnknownAction()
    {
        var fx = new EngineFixture().WithPermitAllPolicies();
        var request = new AccessRequest(new SubjectRef("s", AccountState.Active), null!, null, "c");

        var decision = await fx.Engine().DecideAsync(request);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.UnknownAction));
    }

    [Test]
    public async Task ACC05_NullRequest_DeniedWithGeneratedCorrelationId()
    {
        var fx = new EngineFixture().WithPermitAllPolicies();

        var decision = await fx.Engine().DecideAsync(null!);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(string.IsNullOrWhiteSpace(decision.CorrelationId), Is.False);
    }


    [Test]
    public async Task ACC05_KnownActionWithoutPolicy_DeniedAsNoPolicy()
    {
        var fx = new EngineFixture();

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.NoPolicy));
        Assert.That(decision.PolicyId, Is.Null);
    }

    [Test]
    public async Task ACC05_NoActionInCatalogWithoutPolicyIsEverAllowed_AllSubjectStates()
    {
        var fx = new EngineFixture();
        var engine = fx.Engine();
        SubjectRef?[] subjects =
        [
            null,
            new("s", AccountState.Active),
            new("s", AccountState.Blocked),
            new("s", AccountState.Archived),
            new("s", (AccountState)99),
        ];

        foreach (var definition in AccessActions.All)
        {
            foreach (var subject in subjects)
            {
                var decision = await engine.DecideAsync(
                    AccessRequestBuilder.Default().WithAction(definition.Id).WithSubject(subject).Build());

                Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny),
                    $"{definition.Id} / {subject?.State.ToString() ?? "anonymous"}");
            }
        }
    }

    [Test]
    public async Task ACC05_RegistryReportsFoundButReturnsNull_DeniedAsNoPolicy()
    {
        var engine = EngineFixture.Build(new EngineFixture().Catalog, new TrueButNullRegistry(), new RecordingAuditWriter());

        var decision = await engine.DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.NoPolicy));
    }


    [Test]
    public async Task ACC05_AnonymousOnProtectedAction_DeniedAsUnauthenticated()
    {
        var policy = FakePolicy.Permit();
        var fx = new EngineFixture().WithPolicy(LabRead, policy);

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Anonymous().Build());

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.Unauthenticated));
        Assert.That(policy.Calls, Is.EqualTo(0));
    }

    [TestCase(AccountState.Blocked)]
    [TestCase(AccountState.Archived)]
    [TestCase((AccountState)99)]
    public async Task ACC05_NonActiveAccount_DeniedEvenIfPolicyWouldPermit(AccountState state)
    {
        var policy = FakePolicy.Permit();
        var fx = new EngineFixture().WithPolicy(LabRead, policy);

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().WithState(state).Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.AccountNotActive));
        Assert.That(policy.Calls, Is.EqualTo(0), "Politika se ne sme ni pozvati za neaktivan nalog.");
    }

    [Test]
    public async Task ACC05_PublicActionWithPermitPolicy_AllowedWithoutAuthentication()
    {
        var fx = new EngineFixture().WithPolicy(AccessActions.PublicInfoRead.Id, FakePolicy.Permit());

        var decision = await fx.Engine().DecideAsync(
            AccessRequestBuilder.Default().WithAction(AccessActions.PublicInfoRead.Id).Anonymous().WithResource(null).Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [TestCase(AccountState.Blocked)]
    [TestCase(AccountState.Archived)]
    public async Task ACC05_NonActiveAccountOnPublicAction_DecidedByPublicPolicy(AccountState state)
    {
        // Odluka prati analizu 8.1: uslov aktivnog naloga važi samo za nejavne akcije.
        var fx = new EngineFixture().WithPolicy(AccessActions.PublicInfoRead.Id, FakePolicy.Permit());

        var decision = await fx.Engine().DecideAsync(
            AccessRequestBuilder.Default().WithAction(AccessActions.PublicInfoRead.Id).WithState(state).WithResource(null).Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task ACC05_PublicActionWithoutPolicy_StillDenied()
    {
        var fx = new EngineFixture();

        var decision = await fx.Engine().DecideAsync(
            AccessRequestBuilder.Default().WithAction(AccessActions.PublicInfoRead.Id).Anonymous().WithResource(null).Build());

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.NoPolicy));
    }


    [Test]
    public async Task ACC05_PolicyPermits_AllowedWithPolicyIdentityAndCorrelation()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, new FakePolicy((_, _) => ValueTask.FromResult(true), "p-lab-read", "v7"));

        var decision = await fx.Engine().DecideAsync(
            AccessRequestBuilder.Default().WithCorrelationId("corr-42").Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.None));
        Assert.That(decision.PolicyId, Is.EqualTo("p-lab-read"));
        Assert.That(decision.PolicyVersion, Is.EqualTo("v7"));
        Assert.That(decision.CorrelationId, Is.EqualTo("corr-42"));
    }

    [Test]
    public async Task ACC05_PolicyRefuses_Denied()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.Refuse());

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyDenied));
        Assert.That(decision.PolicyId, Is.Null);
    }

    [Test]
    public async Task ACC05_MissingCorrelationId_GeneratedServerSide()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.Permit());

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().WithCorrelationId("").Build());

        Assert.That(string.IsNullOrWhiteSpace(decision.CorrelationId), Is.False);
    }

    [Test]
    public async Task ACC05_PolicyChangedBetweenRequests_NoCachingOfDecisions()
    {
        var permit = true;
        var fx = new EngineFixture().WithPolicy(LabRead, new FakePolicy((_, _) => ValueTask.FromResult(permit)));
        var engine = fx.Engine();
        var request = AccessRequestBuilder.Default().Build();

        var first = await engine.DecideAsync(request);
        permit = false; // npr. nastavniku je ukinuta uloga
        var second = await engine.DecideAsync(request);

        Assert.That(first.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(second.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }


    [Test]
    public async Task ACC05_PolicyThrowsSynchronously_DeniedAsPolicyError()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.Throwing());

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC05_PolicyFaultsAsynchronously_DeniedAsPolicyError()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.FaultedAsync());

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC05_PolicyThrowsOperationCanceled_DeniedNotPropagated()
    {
        var fx = new EngineFixture().WithPolicy(LabRead,
            new FakePolicy((_, _) => throw new OperationCanceledException()));

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC05_CallerCancellation_DeniedNotThrown()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.Cooperative());
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build(), cts.Token);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC05_AlreadyCancelledToken_DeniedNotThrown()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.Permit());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().Build(), cts.Token);

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }

    [Test]
    public async Task ACC05_CooperativeSlowPolicy_TimesOutAsDeny()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.Cooperative());

        var decision = await fx.Engine(policyTimeout: TimeSpan.FromMilliseconds(100))
            .DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC05_PolicyIgnoringCancellation_StillReturnsDenyWithinTimeout()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.NeverCompletes());
        var started = DateTime.UtcNow;

        var decision = await fx.Engine(policyTimeout: TimeSpan.FromMilliseconds(150))
            .DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task ACC05_CatalogThrows_DeniedAsPolicyError()
    {
        var engine = EngineFixture.Build(new ThrowingCatalog(), new EngineFixture().Registry, new RecordingAuditWriter());

        var decision = await engine.DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC05_RegistryThrows_DeniedAsPolicyError()
    {
        var engine = EngineFixture.Build(new EngineFixture().Catalog, new ThrowingRegistry(), new RecordingAuditWriter());

        var decision = await engine.DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [Test]
    public async Task ACC05_PolicyIdentityThrows_DeniedAndDoesNotThrow()
    {
        var registry = new TestRegistry(new BrokenIdentityPolicy(() => "id", () => throw new InvalidOperationException()));
        var engine = EngineFixture.Build(new EngineFixture().Catalog, registry, new RecordingAuditWriter());

        var decision = await engine.DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.PolicyError));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task ACC05_PolicyWithBlankId_DeniedEvenIfItPermits(string? blankId)
    {
        var registry = new TestRegistry(new BrokenIdentityPolicy(() => blankId!, () => "v1"));
        var engine = EngineFixture.Build(new EngineFixture().Catalog, registry, new RecordingAuditWriter());

        var decision = await engine.DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
    }


    [Test]
    public async Task ACC05_AuditThrows_AllowStaysAllow()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.Permit());

        var decision = await fx.Engine(audit: new ThrowingAuditWriter()).DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    public async Task ACC05_AuditThrows_DenyStaysDeny()
    {
        var fx = new EngineFixture();

        var decision = await fx.Engine(audit: new ThrowingAuditWriter()).DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(decision.DenialReason, Is.EqualTo(AccessDenialReason.NoPolicy));
    }

    [Test]
    public async Task ACC05_AuditNeverCompletes_DecisionStillReturnedWithinTimeout()
    {
        var fx = new EngineFixture();
        var started = DateTime.UtcNow;

        var decision = await fx.Engine(auditTimeout: TimeSpan.FromMilliseconds(150), audit: new NeverCompletingAuditWriter())
            .DecideAsync(AccessRequestBuilder.Default().Build());

        Assert.That(decision.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task ACC05_EveryDecisionIsPassedToAudit_AlsoWhenCallerCancelled()
    {
        var fx = new EngineFixture().WithPolicy(LabRead, FakePolicy.Permit());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var decision = await fx.Engine().DecideAsync(AccessRequestBuilder.Default().WithCorrelationId("corr-audit").Build(), cts.Token);

        Assert.That(fx.Audit.Entries.Count, Is.EqualTo(1));
        Assert.That(fx.Audit.Entries[0].Decision, Is.EqualTo(decision));
        Assert.That(fx.Audit.Entries[0].Decision.CorrelationId, Is.EqualTo("corr-audit"));
    }


    [TestCase(0)]
    [TestCase(-1)]
    public void ACC05_InvalidTimeoutOptions_RejectedAtConstruction(int milliseconds)
    {
        var fx = new EngineFixture();

        Assert.That(
            () => EngineFixture.Build(fx.Catalog, fx.Registry, fx.Audit, TimeSpan.FromMilliseconds(milliseconds)),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ACC05_InfiniteTimeoutOptions_RejectedAtConstruction()
    {
        var fx = new EngineFixture();

        Assert.That(
            () => EngineFixture.Build(fx.Catalog, fx.Registry, fx.Audit, Timeout.InfiniteTimeSpan),
            Throws.InstanceOf<ArgumentOutOfRangeException>());
    }

    private sealed class TestRegistry(Platform.Access.Contracts.IAccessPolicy policy)
        : Platform.Access.Contracts.IAccessPolicyRegistry
    {
        public bool TryGet(string action, out Platform.Access.Contracts.IAccessPolicy? found)
        {
            found = policy;
            return true;
        }
    }
}
