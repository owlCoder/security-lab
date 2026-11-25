using Platform.Access.Application.Rules;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public class TriLogicTests
{
    private static readonly Tri[] All = [Tri.True, Tri.False, Tri.Unknown];

    [TestCase(Tri.True, Tri.True, Tri.True)]
    [TestCase(Tri.True, Tri.False, Tri.False)]
    [TestCase(Tri.True, Tri.Unknown, Tri.Unknown)]
    [TestCase(Tri.False, Tri.True, Tri.False)]
    [TestCase(Tri.False, Tri.False, Tri.False)]
    [TestCase(Tri.False, Tri.Unknown, Tri.False)]
    [TestCase(Tri.Unknown, Tri.True, Tri.Unknown)]
    [TestCase(Tri.Unknown, Tri.False, Tri.False)]
    [TestCase(Tri.Unknown, Tri.Unknown, Tri.Unknown)]
    public void ACC08_And_FollowsKleeneTable(Tri left, Tri right, Tri expected) =>
        Assert.That(TriLogic.And(left, right), Is.EqualTo(expected));

    [TestCase(Tri.True, Tri.True, Tri.True)]
    [TestCase(Tri.True, Tri.False, Tri.True)]
    [TestCase(Tri.True, Tri.Unknown, Tri.True)]
    [TestCase(Tri.False, Tri.True, Tri.True)]
    [TestCase(Tri.False, Tri.False, Tri.False)]
    [TestCase(Tri.False, Tri.Unknown, Tri.Unknown)]
    [TestCase(Tri.Unknown, Tri.True, Tri.True)]
    [TestCase(Tri.Unknown, Tri.False, Tri.Unknown)]
    [TestCase(Tri.Unknown, Tri.Unknown, Tri.Unknown)]
    public void ACC08_Or_FollowsKleeneTable(Tri left, Tri right, Tri expected) =>
        Assert.That(TriLogic.Or(left, right), Is.EqualTo(expected));

    [TestCase(Tri.True, Tri.False)]
    [TestCase(Tri.False, Tri.True)]
    [TestCase(Tri.Unknown, Tri.Unknown)]
    public void ACC08_Not_KeepsUnknownUnknown(Tri value, Tri expected) =>
        Assert.That(TriLogic.Not(value), Is.EqualTo(expected));

    [Test]
    public void ACC08_DefaultValue_IsUnknownNotFalse() =>
        Assert.That(default(Tri), Is.EqualTo(Tri.Unknown));

    [TestCase(3)]
    [TestCase(99)]
    [TestCase(255)]
    public void ACC08_UndefinedValue_IsTreatedAsUnknown(int raw)
    {
        var undefined = (Tri)raw;

        Assert.That(TriLogic.Normalize(undefined), Is.EqualTo(Tri.Unknown));
        Assert.That(TriLogic.Not(undefined), Is.EqualTo(Tri.Unknown));
        Assert.That(TriLogic.And(undefined, Tri.True), Is.EqualTo(Tri.Unknown));
        Assert.That(TriLogic.Or(undefined, Tri.False), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_DeMorganAndCommutativity_HoldForAllCombinations()
    {
        foreach (var a in All)
        {
            foreach (var b in All)
            {
                Assert.That(TriLogic.Not(TriLogic.And(a, b)),
                    Is.EqualTo(TriLogic.Or(TriLogic.Not(a), TriLogic.Not(b))), $"{a},{b}");
                Assert.That(TriLogic.Not(TriLogic.Or(a, b)),
                    Is.EqualTo(TriLogic.And(TriLogic.Not(a), TriLogic.Not(b))), $"{a},{b}");
                Assert.That(TriLogic.And(a, b), Is.EqualTo(TriLogic.And(b, a)));
                Assert.That(TriLogic.Or(a, b), Is.EqualTo(TriLogic.Or(b, a)));
            }
        }
    }
}
