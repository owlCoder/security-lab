using Platform.Access.Application.Rules;
using Platform.Access.Application.Services;
using Platform.Access.Tests.Support;
using static Platform.Access.Tests.Support.RuleTestData;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public class ConditionCombinatorTests
{
    [Test]
    public void ACC08_AllOf_AllTrue_IsTrue() =>
        Assert.That(Conditions.AllOf(True(), True()).Evaluate(0), Is.EqualTo(Tri.True));

    [Test]
    public void ACC08_AllOf_OneFalseWinsOverUnknown() =>
        Assert.That(Conditions.AllOf(Unknown(), False(), True()).Evaluate(0), Is.EqualTo(Tri.False));

    [Test]
    public void ACC08_AllOf_UnknownWithoutFalse_IsUnknown() =>
        Assert.That(Conditions.AllOf(True(), Unknown()).Evaluate(0), Is.EqualTo(Tri.Unknown));

    [Test]
    public void ACC08_AnyOf_OneTrueWinsOverUnknown() =>
        Assert.That(Conditions.AnyOf(Unknown(), True(), False()).Evaluate(0), Is.EqualTo(Tri.True));

    [Test]
    public void ACC08_AnyOf_AllFalse_IsFalse() =>
        Assert.That(Conditions.AnyOf(False(), False()).Evaluate(0), Is.EqualTo(Tri.False));

    [Test]
    public void ACC08_AnyOf_UnknownWithoutTrue_IsUnknown() =>
        Assert.That(Conditions.AnyOf(False(), Unknown()).Evaluate(0), Is.EqualTo(Tri.Unknown));

    [Test]
    public void ACC08_Not_UnknownStaysUnknown() =>
        Assert.That(Conditions.Not(Unknown()).Evaluate(0), Is.EqualTo(Tri.Unknown));

    [Test]
    public void ACC08_ChildReturningUndefinedValue_IsTreatedAsUnknown()
    {
        var bad = Of((Tri)99);

        Assert.That(Conditions.AllOf(True(), bad).Evaluate(0), Is.EqualTo(Tri.Unknown));
        Assert.That(Conditions.AnyOf(False(), bad).Evaluate(0), Is.EqualTo(Tri.Unknown));
        Assert.That(Conditions.Not(bad).Evaluate(0), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_EmptyAllOfAndAnyOf_RejectedAtConstruction()
    {
        Assert.That(() => new AllOfCondition<int>([]), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => new AnyOfCondition<int>([]), Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_NullArgumentsAndNullChildren_Rejected()
    {
        Assert.That(() => new AllOfCondition<int>(null!), Throws.InstanceOf<ArgumentNullException>());
        Assert.That(() => new NotCondition<int>(null!), Throws.InstanceOf<ArgumentNullException>());
        Assert.That(() => new AnyOfCondition<int>(new ICondition<int>[] { True(), null! }),
            Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_Combinators_ExposeChildrenForLimitChecks()
    {
        var leaf = True();

        Assert.That(leaf.Children.Count, Is.EqualTo(0));
        Assert.That(Conditions.AllOf(leaf, True()).Children.Count, Is.EqualTo(2));
        Assert.That(Conditions.Not(leaf).Children.Count, Is.EqualTo(1));
    }

    [Test]
    public void ACC08_CallerCannotMutateChildrenAfterConstruction()
    {
        var array = new[] { True(), True() };
        var condition = new AllOfCondition<int>(array);

        array[0] = False(); // izmena izvornog niza posle konstrukcije

        Assert.That(condition.Evaluate(0), Is.EqualTo(Tri.True));
    }
}
