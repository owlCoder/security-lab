using Platform.Access.Application.Rules;
using Platform.Access.Domain.Enums;
using Platform.Access.Tests.Support;
using static Platform.Access.Tests.Support.RuleTestData;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public class RulePropertyTests
{
    private const int Seed = 20261113;
    private const int Iterations = 2_000;
    private static readonly Tri[] Values = [Tri.True, Tri.False, Tri.Unknown];

    private static Tri RandomTri(Random random) => Values[random.Next(Values.Length)];

    private static Rule<int>[] RandomRules(Random random, string prefix, int maxCount, int minCount)
    {
        var count = random.Next(minCount, maxCount + 1);
        return Enumerable.Range(0, count).Select(i => Rule($"{prefix}{i:00}", RandomTri(random))).ToArray();
    }

    // Referentna ("oracle") definicija ishoda, napisana nezavisno od RuleSet.Evaluate.
    private static bool ExpectedPermit(IEnumerable<Rule<int>> requirements, IEnumerable<Rule<int>> prohibitions) =>
        requirements.All(r => r.Condition.Evaluate(0) == Tri.True) &&
        prohibitions.All(p => p.Condition.Evaluate(0) == Tri.False);

    [Test]
    public void ACC08_Permit_IffAllRequirementsTrueAndAllProhibitionsFalse()
    {
        var random = new Random(Seed);

        for (var i = 0; i < Iterations; i++)
        {
            var requirements = RandomRules(random, "r", 5, 1);
            var prohibitions = RandomRules(random, "p", 4, 0);

            var result = new RuleSet<int>(requirements, prohibitions).Evaluate(0);

            Assert.That(result.IsPermit, Is.EqualTo(ExpectedPermit(requirements, prohibitions)), $"iteration {i}");
        }
    }

    [Test]
    public void ACC08_RuleOrder_NeverChangesVerdictReasonOrRuleId()
    {
        var random = new Random(Seed + 1);

        for (var i = 0; i < Iterations; i++)
        {
            var requirements = RandomRules(random, "r", 5, 1);
            var prohibitions = RandomRules(random, "p", 4, 0);

            var baseline = new RuleSet<int>(requirements, prohibitions).Evaluate(0);
            var shuffled = new RuleSet<int>(
                requirements.OrderBy(_ => random.Next()).ToArray(),
                prohibitions.OrderBy(_ => random.Next()).ToArray()).Evaluate(0);

            Assert.That(shuffled, Is.EqualTo(baseline), $"iteration {i}");
        }
    }

    [Test]
    public void ACC08_AddingAnyRule_NeverTurnsNonPermitIntoPermit()
    {
        var random = new Random(Seed + 2);

        for (var i = 0; i < Iterations; i++)
        {
            var requirements = RandomRules(random, "r", 4, 1);
            var prohibitions = RandomRules(random, "p", 3, 0);
            var before = new RuleSet<int>(requirements, prohibitions).Evaluate(0);

            var extraRequirement = Rule("r-extra", RandomTri(random));
            var extraProhibition = Rule("p-extra", RandomTri(random));
            var withRequirement = new RuleSet<int>(requirements.Append(extraRequirement), prohibitions).Evaluate(0);
            var withProhibition = new RuleSet<int>(requirements, prohibitions.Append(extraProhibition)).Evaluate(0);

            if (!before.IsPermit)
            {
                Assert.That(withRequirement.IsPermit, Is.False, $"requirement, iteration {i}");
                Assert.That(withProhibition.IsPermit, Is.False, $"prohibition, iteration {i}");
            }
        }
    }

    [Test]
    public void ACC08_ProhibitionTrue_AlwaysDeniesWhateverTheOtherRulesSay()
    {
        var random = new Random(Seed + 3);

        for (var i = 0; i < Iterations; i++)
        {
            var requirements = RandomRules(random, "r", 5, 1);
            var prohibitions = RandomRules(random, "p", 3, 0).Append(Rule("p-hit", Tri.True)).ToArray();

            var result = new RuleSet<int>(requirements, prohibitions).Evaluate(0);

            Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny), $"iteration {i}");
            Assert.That(result.Failure, Is.EqualTo(RuleFailure.ProhibitionTriggered));
        }
    }

    [Test]
    public void ACC08_NoRuleSetWithAUnknownRuleEverPermits()
    {
        var random = new Random(Seed + 4);

        for (var i = 0; i < Iterations; i++)
        {
            var requirements = RandomRules(random, "r", 4, 1).Append(Rule("r-unknown", Tri.Unknown)).ToArray();
            var prohibitions = RandomRules(random, "p", 3, 0);

            Assert.That(new RuleSet<int>(requirements, prohibitions).Evaluate(0).IsPermit, Is.False);
        }
    }

    // ---------- Slučajna stabla uslova naspram nezavisnog tumačenja ----------

    private abstract record Spec;
    private sealed record Leaf(Tri Value) : Spec;
    private sealed record NotSpec(Spec Inner) : Spec;
    private sealed record AllSpec(Spec[] Items) : Spec;
    private sealed record AnySpec(Spec[] Items) : Spec;

    private static Spec RandomSpec(Random random, int depth)
    {
        if (depth <= 1 || random.Next(4) == 0)
        {
            return new Leaf(RandomTri(random));
        }

        var items = Enumerable.Range(0, random.Next(1, 4)).Select(_ => RandomSpec(random, depth - 1)).ToArray();
        return random.Next(3) switch
        {
            0 => new NotSpec(items[0]),
            1 => new AllSpec(items),
            _ => new AnySpec(items)
        };
    }

    private static ICondition<int> Build(Spec spec) => spec switch
    {
        Leaf leaf => Of(leaf.Value),
        NotSpec not => Conditions.Not(Build(not.Inner)),
        AllSpec all => new AllOfCondition<int>(all.Items.Select(Build)),
        AnySpec any => new AnyOfCondition<int>(any.Items.Select(Build)),
        _ => throw new InvalidOperationException()
    };

    // Tumačenje bez TriLogic klase: tabele su ispisane po definiciji (Kleene).
    private static Tri Interpret(Spec spec)
    {
        switch (spec)
        {
            case Leaf leaf:
                return leaf.Value;
            case NotSpec not:
                var inner = Interpret(not.Inner);
                return inner == Tri.True ? Tri.False : inner == Tri.False ? Tri.True : Tri.Unknown;
            case AllSpec all:
                var allValues = all.Items.Select(Interpret).ToArray();
                return allValues.Contains(Tri.False) ? Tri.False
                    : allValues.Contains(Tri.Unknown) ? Tri.Unknown : Tri.True;
            case AnySpec any:
                var anyValues = any.Items.Select(Interpret).ToArray();
                return anyValues.Contains(Tri.True) ? Tri.True
                    : anyValues.Contains(Tri.Unknown) ? Tri.Unknown : Tri.False;
            default:
                throw new InvalidOperationException();
        }
    }

    [Test]
    public void ACC08_RandomConditionTrees_MatchIndependentInterpretation()
    {
        var random = new Random(Seed + 5);

        for (var i = 0; i < Iterations; i++)
        {
            var spec = RandomSpec(random, depth: 5);

            Assert.That(Build(spec).Evaluate(0), Is.EqualTo(Interpret(spec)), $"iteration {i}");
        }
    }

    [Test]
    public void ACC08_RandomConditionTrees_AreDeterministicAcrossRepeatedEvaluation()
    {
        var random = new Random(Seed + 6);

        for (var i = 0; i < 200; i++)
        {
            var condition = Build(RandomSpec(random, depth: 5));
            var first = condition.Evaluate(0);

            for (var repeat = 0; repeat < 5; repeat++)
            {
                Assert.That(condition.Evaluate(repeat), Is.EqualTo(first));
            }
        }
    }
}
