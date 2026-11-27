
using Platform.Access.Application.Rules;
using Platform.Access.Application.Services;
using Platform.Access.Domain.Models;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public sealed class ContextualRuleIdsTests
{
    private sealed class TrueCondition : ICondition<EvaluationSnapshot>
    {
        public Tri Evaluate(EvaluationSnapshot context) => Tri.True;
    }

    private static RuleSet<EvaluationSnapshot> CreateRules(string id) =>
        new(
        [
            new Rule<EvaluationSnapshot>(id, new TrueCondition())
        ]);

    [Test]
    public void ACC08_ContextualRule_WithRulePrefix_IsValid()
    {
        Assert.DoesNotThrow(() =>
            ContextualRuleIds.Validate(
                CreateRules("rule.assignment.active")));
    }

    [Test]
    public void ACC08_ContextualRule_WithoutPrefix_IsRejected()
    {
        Assert.Throws<AccessConfigurationException>(() =>
            ContextualRuleIds.Validate(
                CreateRules("assignment.active")));
    }

    [TestCase("resource.present")]
    [TestCase("membership.active")]
    [TestCase("resource.course")]
    [TestCase("relation.allowed")]
    public void ACC08_ContextualRule_CannotUseGuardId(string id)
    {
        // Guard ID ne može da prođe ni bez prefiksa.
        Assert.Throws<AccessConfigurationException>(() =>
            ContextualRuleIds.Validate(CreateRules(id)));
    }
}
