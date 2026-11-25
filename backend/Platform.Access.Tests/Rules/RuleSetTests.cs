using Platform.Access.Application.Rules;
using Platform.Access.Application.Services;
using Platform.Access.Domain.Enums;
using Platform.Access.Tests.Support;
using static Platform.Access.Tests.Support.RuleTestData;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public class RuleSetTests
{
    private static RuleSet<int> Set(Rule<int>[] requirements, params Rule<int>[] prohibitions) =>
        new(requirements, prohibitions);

    // ---------- Ishodi (deny-overrides, fail-closed) ----------

    [Test]
    public void ACC08_AllRequirementsTrueAndNoProhibition_Permit()
    {
        var result = Set([Rule("r1", Tri.True), Rule("r2", Tri.True)], Rule("p1", Tri.False)).Evaluate(0);

        Assert.That(result.IsPermit, Is.True);
        Assert.That(result.Failure, Is.EqualTo(RuleFailure.None));
    }

    [Test]
    public void ACC08_ProhibitionTrue_OverridesAllRequirements()
    {
        var result = Set([Rule("r1", Tri.True)], Rule("p1", Tri.True)).Evaluate(0);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Failure, Is.EqualTo(RuleFailure.ProhibitionTriggered));
        Assert.That(result.RuleId, Is.EqualTo("p1"));
    }

    [Test]
    public void ACC08_RequirementFalse_Deny()
    {
        var result = Set([Rule("r1", Tri.True), Rule("r2", Tri.False)]).Evaluate(0);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Failure, Is.EqualTo(RuleFailure.RequirementNotMet));
        Assert.That(result.RuleId, Is.EqualTo("r2"));
    }

    [Test]
    public void ACC08_RequirementUnknown_IndeterminateNeverPermit()
    {
        var result = Set([Rule("r1", Tri.True), Rule("r2", Tri.Unknown)]).Evaluate(0);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Indeterminate));
        Assert.That(result.Failure, Is.EqualTo(RuleFailure.MissingValue));
        Assert.That(result.IsPermit, Is.False);
    }

    [Test]
    public void ACC08_ProhibitionUnknown_CannotRuleOutForbidden_IndeterminateNeverPermit()
    {
        var result = Set([Rule("r1", Tri.True)], Rule("p1", Tri.Unknown)).Evaluate(0);

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Indeterminate));
        Assert.That(result.RuleId, Is.EqualTo("p1"));
    }

    [Test]
    public void ACC08_Priority_ProhibitionBeatsUnmetRequirementBeatsMissingValue()
    {
        var all = Set([Rule("r-false", Tri.False), Rule("r-unknown", Tri.Unknown)], Rule("p-true", Tri.True))
            .Evaluate(0);
        var unmetAndUnknown = Set([Rule("r-false", Tri.False), Rule("r-unknown", Tri.Unknown)]).Evaluate(0);

        Assert.That(all.Failure, Is.EqualTo(RuleFailure.ProhibitionTriggered));
        Assert.That(unmetAndUnknown.Failure, Is.EqualTo(RuleFailure.RequirementNotMet));
        Assert.That(unmetAndUnknown.Outcome, Is.EqualTo(PolicyOutcome.Deny));
    }

    [Test]
    public void ACC08_FailingRuleId_IsSmallestOrdinal_RegardlessOfOrder()
    {
        var result = Set([Rule("zz", Tri.False), Rule("aa", Tri.False), Rule("mm", Tri.False)]).Evaluate(0);

        Assert.That(result.RuleId, Is.EqualTo("aa"));
    }

    [Test]
    public void ACC08_Evaluate_IsRepeatableAndHasNoState()
    {
        var set = Set([Rule("r1", Tri.True)], Rule("p1", Tri.False));

        Assert.That(set.Evaluate(0), Is.EqualTo(set.Evaluate(1)));
    }

    [Test]
    public void ACC08_ThrowingCondition_PropagatesToEngineBoundary()
    {
        var set = new RuleSet<int>([new Rule<int>("r1", new ThrowingCondition())]);

        Assert.That(() => set.Evaluate(0), Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public void ACC08_NullContext_Rejected()
    {
        var set = Set([Rule("r1", Tri.True)]);

        Assert.That(() => new RuleSet<string>([new Rule<string>("r1", new StringTrue())]).Evaluate(null!),
            Throws.InstanceOf<ArgumentNullException>());
        Assert.That(set.Evaluate(0).IsPermit, Is.True);
    }

    private sealed class StringTrue : ICondition<string>
    {
        public Tri Evaluate(string context) => Tri.True;
    }

    // ---------- Konfiguracija se odbija pri konstrukciji ----------

    [Test]
    public void ACC08_NoRequirements_Rejected_BecauseItWouldPermitEverything()
    {
        Assert.That(() => new RuleSet<int>([]), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => new RuleSet<int>([], [Rule("p1", Tri.False)]),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_DuplicateRuleIds_RejectedAcrossRequirementsAndProhibitions()
    {
        Assert.That(() => Set([Rule("same", Tri.True)], Rule("same", Tri.False)),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_NullRule_Rejected()
    {
        Assert.That(() => new RuleSet<int>(new Rule<int>[] { null! }),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    public static IEnumerable<string> InvalidRuleIds() =>
    [
        "",
        "Upper",
        "has space",
        "line\nbreak",
        "nul\0",
        "ｆｕｌｌｗｉｄｔｈ",
        new string('a', 65)
    ];

    [TestCaseSource(nameof(InvalidRuleIds))]
    public void ACC08_InvalidRuleId_Rejected(string id) =>
        Assert.That(() => new Rule<int>(id, True()), Throws.InstanceOf<AccessConfigurationException>());

    [Test]
    public void ACC08_NullRuleIdOrCondition_Rejected()
    {
        Assert.That(() => new Rule<int>(null!, True()), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => new Rule<int>("r1", null!), Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_ValidRuleIds_Accepted()
    {
        Assert.That(new Rule<int>("attempt.create:deadline-1", True()).Id, Is.EqualTo("attempt.create:deadline-1"));
        Assert.That(new Rule<int>(new string('a', 64), True()).Id.Length, Is.EqualTo(64));
    }

    // ---------- Ograničenja veličine (T15) ----------

    private static ICondition<int> Nested(int depth)
    {
        var condition = True();
        for (var level = 1; level < depth; level++)
        {
            condition = Conditions.Not(condition);
        }

        return condition;
    }

    [Test]
    public void ACC08_DepthAtLimit_AcceptedAndOneOver_Rejected()
    {
        Assert.That(() => Set([new Rule<int>("r1", Nested(8))]), Throws.Nothing);
        Assert.That(() => Set([new Rule<int>("r1", Nested(9))]), Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_VeryDeepTree_RejectedWithoutStackOverflow()
    {
        Assert.That(() => Set([new Rule<int>("r1", Nested(50_000))]),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_NodeCountAtLimit_AcceptedAndOneOver_Rejected()
    {
        // AllOf (1 čvor) + 63 lista = 64 čvora.
        var atLimit = new AllOfCondition<int>(Enumerable.Range(0, 63).Select(_ => True()));
        var overLimit = new AllOfCondition<int>(Enumerable.Range(0, 64).Select(_ => True()));

        Assert.That(() => Set([new Rule<int>("r1", atLimit)]), Throws.Nothing);
        Assert.That(() => Set([new Rule<int>("r1", overLimit)]), Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_SharedSubtreeExplosion_RejectedQuickly()
    {
        // Svaki nivo duplira prethodni (DAG): bez prekida bi imao 2^20 putanja.
        var shared = True();
        for (var level = 0; level < 20; level++)
        {
            shared = Conditions.AllOf(shared, shared);
        }

        var started = DateTime.UtcNow;

        Assert.That(() => new RuleSet<int>([new Rule<int>("r1", shared)], null, new RuleLimits(MaxDepth: 64)),
            Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void ACC08_TooManyRules_Rejected()
    {
        // Podrazumevano 24 pravila: ostaje mesta za korake kapija u tragu (najviše 32 koraka).
        var rules = Enumerable.Range(0, 25).Select(i => Rule($"r{i}", Tri.True)).ToArray();

        Assert.That(() => Set(rules), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => Set(rules.Take(24).ToArray()), Throws.Nothing);
    }

    [Test]
    public void ACC08_RuleCountAboveTraceCeiling_RejectedEvenWithCustomLimits()
    {
        var rules = Enumerable.Range(0, 33).Select(i => Rule($"r{i}", Tri.True)).ToArray();

        Assert.That(() => new RuleSet<int>(rules, null, new RuleLimits(MaxRules: 33)),
            Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => new RuleSet<int>(rules.Take(32).ToArray(), null, new RuleLimits(MaxRules: 32)),
            Throws.Nothing);
    }

    [Test]
    public void ACC08_ConditionLyingAboutChildren_RejectedNotCrashing() =>
        Assert.That(() => Set([new Rule<int>("r1", new NullChildrenCondition())]),
            Throws.InstanceOf<AccessConfigurationException>());

    [TestCase(0, 8, 64)]
    [TestCase(24, 0, 64)]
    [TestCase(24, 8, -1)]
    public void ACC08_NonPositiveLimits_Rejected(int rules, int depth, int nodes) =>
        Assert.That(() => new RuleSet<int>([Rule("r1", Tri.True)], null, new RuleLimits(rules, depth, nodes)),
            Throws.InstanceOf<AccessConfigurationException>());

    [Test]
    public void ACC08_CallerCannotMutateRulesAfterConstruction()
    {
        var requirements = new[] { Rule("r1", Tri.True) };
        var set = new RuleSet<int>(requirements);

        requirements[0] = Rule("r1", Tri.False);

        Assert.That(set.Evaluate(0).IsPermit, Is.True);
    }
}
