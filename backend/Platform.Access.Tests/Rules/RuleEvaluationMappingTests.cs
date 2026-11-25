using System.Text.RegularExpressions;
using Platform.Access.Application.Rules;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using static Platform.Access.Tests.Support.RuleTestData;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public class RuleEvaluationMappingTests
{
    private static RuleEvaluation Evaluate(Rule<int>[] requirements, params Rule<int>[] prohibitions) =>
        new RuleSet<int>(requirements, prohibitions).Evaluate(0);

    [Test]
    public void ACC08_Permit_MapsToPermitWithoutReason()
    {
        var result = Evaluate([Rule("r1", Tri.True)], Rule("p1", Tri.False)).ToPolicyResult();

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Permit));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.None));
    }

    [Test]
    public void ACC08_ProhibitionTriggered_MapsToContextMismatch()
    {
        var result = Evaluate([Rule("r1", Tri.True)], Rule("p1", Tri.True)).ToPolicyResult();

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.ContextMismatch));
    }

    [Test]
    public void ACC08_RequirementNotMet_MapsToContextMismatch()
    {
        var result = Evaluate([Rule("r1", Tri.False)]).ToPolicyResult();

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.ContextMismatch));
    }

    [Test]
    public void ACC08_MissingValue_MapsToIndeterminateAndMissingAttribute()
    {
        var result = Evaluate([Rule("r1", Tri.Unknown)]).ToPolicyResult();

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Indeterminate));
        Assert.That(result.Reason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public void ACC08_OnlyPermitHasNoReason_EveryOtherOutcomeNamesOne()
    {
        var values = new[] { Tri.True, Tri.False, Tri.Unknown };

        foreach (var requirement in values)
        {
            foreach (var prohibition in values)
            {
                var result = Evaluate([Rule("r1", requirement)], Rule("p1", prohibition)).ToPolicyResult();

                Assert.That(result.Outcome == PolicyOutcome.Permit, Is.EqualTo(result.Reason == AccessDenialReason.None),
                    $"{requirement}/{prohibition}");
            }
        }
    }

    [Test]
    public void ACC08_Trace_ListsEveryRuleOnceWithItsResult_OrderedByIdOrdinal()
    {
        var result = Evaluate(
            [Rule("r-b", Tri.True), Rule("r-a", Tri.False), Rule("r-c", Tri.Unknown)],
            Rule("p-b", Tri.False), Rule("p-a", Tri.True), Rule("p-c", Tri.Unknown));

        var steps = result.ToPolicyResult().Trace.Steps;

        Assert.That(steps.Select(s => s.Id).ToArray(),
            Is.EqualTo(new[] { "p-a", "p-b", "p-c", "r-a", "r-b", "r-c" }));
        Assert.That(steps.Single(s => s.Id == "r-b").Result, Is.EqualTo(TraceResult.Passed));
        Assert.That(steps.Single(s => s.Id == "r-a").Result, Is.EqualTo(TraceResult.Failed));
        Assert.That(steps.Single(s => s.Id == "r-c").Result, Is.EqualTo(TraceResult.Indeterminate));
        // Zabrana: netačna je "prošla", tačna je "pala".
        Assert.That(steps.Single(s => s.Id == "p-b").Result, Is.EqualTo(TraceResult.Passed));
        Assert.That(steps.Single(s => s.Id == "p-a").Result, Is.EqualTo(TraceResult.Failed));
        Assert.That(steps.Single(s => s.Id == "p-c").Result, Is.EqualTo(TraceResult.Indeterminate));
    }

    [Test]
    public void ACC08_Trace_StepsAreWithinTraceLimitAndHaveValidIds()
    {
        var rules = Enumerable.Range(0, RuleLimits.Default.MaxRules).Select(i => Rule($"rule.{i:00}", Tri.True)).ToArray();

        var steps = Evaluate(rules).ToPolicyResult().Trace.Steps;

        Assert.That(steps.Count <= 32, Is.True);
        Assert.That(steps.All(s => Regex.IsMatch(s.Id, @"\A[a-z0-9._:-]{1,64}\z")), Is.True);
    }

    [Test]
    public void ACC08_Trace_ContainsOnlyIdAndResult_NoAttributeValuesLeak()
    {
        var properties = typeof(TraceStep).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.That(properties, Is.EqualTo(new[] { "Id", "Result" }));
    }

    [Test]
    public void ACC08_Trace_DoesNotDependOnRuleOrder()
    {
        var a = Evaluate([Rule("r1", Tri.True), Rule("r2", Tri.False)], Rule("p1", Tri.False));
        var b = Evaluate([Rule("r2", Tri.False), Rule("r1", Tri.True)], Rule("p1", Tri.False));

        Assert.That(a.Steps.SequenceEqual(b.Steps), Is.True);
        Assert.That(a, Is.EqualTo(b));
    }

    [Test]
    public void ACC08_Evaluations_AreEqualOnlyWhenOutcomeReasonRuleAndTraceMatch()
    {
        var first = Evaluate([Rule("r1", Tri.False)]);
        var sameAgain = Evaluate([Rule("r1", Tri.False)]);
        var differentRule = Evaluate([Rule("r2", Tri.False)]);

        Assert.That(first, Is.EqualTo(sameAgain));
        Assert.That(first.GetHashCode(), Is.EqualTo(sameAgain.GetHashCode()));
        Assert.That(first.Equals(differentRule), Is.False);
        Assert.That(first.Equals(null), Is.False);
    }
}
