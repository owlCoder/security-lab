using Platform.Access.Application.Rules;
using Platform.Access.Domain.Constants;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public sealed class ContextualAccessRulesTests
{
    private IReadOnlyDictionary<string, RuleSet<EvaluationSnapshot>> rules = null!;

    [SetUp]
    public void SetUp()
    {
        rules = ContextualAccessRules.Create();
    }

    [Test]
    public void ACC08_Rules_AreRegisteredOnlyForActionsWithDefinedConditions()
    {
        Assert.That(rules.Keys, Is.EquivalentTo(new[]
        {
            AccessActions.AttemptCreate.Id,
            AccessActions.AttemptSubmit.Id,
            AccessActions.AttemptReadCourse.Id,
            AccessActions.AttemptReview.Id
        }));
    }

    [Test]
    public void ACC08_AllRuleIds_StartWithRulePrefix()
    {
        foreach (var ruleSet in rules.Values)
        {
            Assert.That(
                ruleSet.Requirements.Concat(ruleSet.Prohibitions)
                    .All(rule => rule.Id.StartsWith("rule.", StringComparison.Ordinal)),
                Is.True);
        }
    }

    [Test]
    public void ACC08_RuleIds_AreUniqueAcrossAllContextualRuleSets()
    {
        var ids = rules.Values
            .SelectMany(set => set.Requirements.Concat(set.Prohibitions))
            .Select(rule => rule.Id)
            .ToArray();

        Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length));
    }

    [Test]
    public void ACC08_AttemptSubmit_UsesInProgressWhitelist()
    {
        var rule = rules[AccessActions.AttemptSubmit.Id]
            .Requirements.Single();

        Assert.That(rule.Id, Is.EqualTo("rule.attempt.submit.in-progress"));
    }

    [Test]
    public void ACC08_AttemptCreate_UsesKnownSnapshotAttributes()
    {
        var ruleIds = rules[AccessActions.AttemptCreate.Id]
            .Requirements.Concat(
                rules[AccessActions.AttemptCreate.Id].Prohibitions)
            .Select(rule => rule.Id);

        Assert.That(ruleIds, Does.Contain("rule.attempt.create.assignment-active"));
        Assert.That(ruleIds, Does.Contain("rule.attempt.create.deadline"));
        Assert.That(ruleIds, Does.Contain("rule.attempt.create.attempts-remaining"));
        Assert.That(ruleIds, Does.Contain("rule.attempt.create.course-not-archived"));
    }
}