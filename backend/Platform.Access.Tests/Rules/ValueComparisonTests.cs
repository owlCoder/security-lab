using Platform.Access.Application.Rules;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public class ValueComparisonTests
{
    private enum Color { Red, Blue }
    private enum Shape { Round }

    // ---------- Jednakost ----------

    [Test]
    public void ACC08_Equal_SameTypesCompareByValue()
    {
        Assert.That(ValueComparison.AreEqual("a", "a"), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.AreEqual("a", "b"), Is.EqualTo(Tri.False));
        Assert.That(ValueComparison.AreEqual(true, true), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.AreEqual(true, false), Is.EqualTo(Tri.False));
        var id = Guid.NewGuid();
        Assert.That(ValueComparison.AreEqual(id, id), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.AreEqual(id, Guid.NewGuid()), Is.EqualTo(Tri.False));
        Assert.That(ValueComparison.AreEqual(Color.Red, Color.Red), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.AreEqual(Color.Red, Color.Blue), Is.EqualTo(Tri.False));
    }

    [Test]
    public void ACC08_Equal_StringsAreOrdinalAndCaseSensitive()
    {
        Assert.That(ValueComparison.AreEqual("InProgress", "inprogress"), Is.EqualTo(Tri.False));
        Assert.That(ValueComparison.AreEqual("a", "a "), Is.EqualTo(Tri.False));
        Assert.That(ValueComparison.AreEqual("\u00e9", "e\u0301"), Is.EqualTo(Tri.False)); // spojen vs razdvojen oblik
    }

    [Test]
    public void ACC08_Equal_IntegralTypesAreComparedByNumericValue()
    {
        Assert.That(ValueComparison.AreEqual(5, 5L), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.AreEqual((short)3, (byte)3), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.AreEqual(5, 6L), Is.EqualTo(Tri.False));
    }

    [Test]
    public void ACC08_Equal_MissingValueIsUnknownNeverFalse()
    {
        Assert.That(ValueComparison.AreEqual(null, "a"), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.AreEqual("a", null), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.AreEqual(null, null), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_Equal_MismatchedOrUnsupportedTypes_AreUnknownNeverFalse()
    {
        Assert.That(ValueComparison.AreEqual("5", 5), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.AreEqual(true, 1), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.AreEqual(Color.Red, Shape.Round), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.AreEqual(1.5, 1.5), Is.EqualTo(Tri.Unknown));   // double nije podržan
        Assert.That(ValueComparison.AreEqual(new object(), new object()), Is.EqualTo(Tri.Unknown));
    }

    // ---------- Pripadnost skupu ----------

    [Test]
    public void ACC08_In_FindsMatchAndRejectsNonMatch()
    {
        Assert.That(ValueComparison.IsIn("b", new object?[] { "a", "b" }), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.IsIn("c", new object?[] { "a", "b" }), Is.EqualTo(Tri.False));
        Assert.That(ValueComparison.IsIn("c", Array.Empty<object?>()), Is.EqualTo(Tri.False));
    }

    [Test]
    public void ACC08_In_MissingActualIsUnknown() =>
        Assert.That(ValueComparison.IsIn(null, new object?[] { "a" }), Is.EqualTo(Tri.Unknown));

    [Test]
    public void ACC08_In_TypeMismatchInSetWithoutMatchIsUnknown_ButMatchStillWins()
    {
        Assert.That(ValueComparison.IsIn("c", new object?[] { "a", 5 }), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.IsIn("a", new object?[] { 5, "a" }), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.IsIn("a", new object?[] { null, "a" }), Is.EqualTo(Tri.True));
    }

    [Test]
    public void ACC08_In_NullSetRejected() =>
        Assert.That(() => ValueComparison.IsIn("a", null!), Throws.InstanceOf<ArgumentNullException>());

    // ---------- Manje od ----------

    [Test]
    public void ACC08_LessThan_IntegersAndTimes()
    {
        Assert.That(ValueComparison.IsLessThan(2, 3L), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.IsLessThan(3, 3), Is.EqualTo(Tri.False));   // granica nije "manje"
        Assert.That(ValueComparison.IsLessThan(4, 3), Is.EqualTo(Tri.False));

        var now = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
        Assert.That(ValueComparison.IsLessThan(now, now.AddSeconds(1)), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.IsLessThan(now, now), Is.EqualTo(Tri.False));
    }

    [Test]
    public void ACC08_LessThan_MissingUnsupportedOrMixedTypesAreUnknown()
    {
        Assert.That(ValueComparison.IsLessThan(null, 3), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.IsLessThan(3, null), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.IsLessThan("a", "b"), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.IsLessThan(3, DateTimeOffset.UtcNow), Is.EqualTo(Tri.Unknown));
    }

    // ---------- Vreme (P1: striktno Now < rok) ----------

    private static readonly DateTimeOffset Deadline = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void ACC08_IsBefore_BoundaryMinusPlusOneSecond()
    {
        Assert.That(ValueComparison.IsBefore(Deadline.AddSeconds(-1), Deadline), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.IsBefore(Deadline, Deadline), Is.EqualTo(Tri.False));
        Assert.That(ValueComparison.IsBefore(Deadline.AddSeconds(1), Deadline), Is.EqualTo(Tri.False));
    }

    [Test]
    public void ACC08_IsAtOrAfter_BoundaryMinusPlusOneSecond()
    {
        Assert.That(ValueComparison.IsAtOrAfter(Deadline.AddSeconds(-1), Deadline), Is.EqualTo(Tri.False));
        Assert.That(ValueComparison.IsAtOrAfter(Deadline, Deadline), Is.EqualTo(Tri.True));
        Assert.That(ValueComparison.IsAtOrAfter(Deadline.AddSeconds(1), Deadline), Is.EqualTo(Tri.True));
    }

    [Test]
    public void ACC08_Time_MissingValueIsUnknown()
    {
        Assert.That(ValueComparison.IsBefore(null, Deadline), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.IsBefore(Deadline, null), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.IsAtOrAfter(null, Deadline), Is.EqualTo(Tri.Unknown));
        Assert.That(ValueComparison.IsAtOrAfter(Deadline, null), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_Time_IsBeforeAndIsAtOrAfterAreExactComplements_AcrossOffsets()
    {
        var random = new Random(20261113);

        for (var i = 0; i < 2_000; i++)
        {
            var limit = Deadline.AddSeconds(random.Next(-10, 11)).ToOffset(TimeSpan.FromHours(random.Next(-5, 6)));
            var now = Deadline.AddSeconds(random.Next(-10, 11)).ToOffset(TimeSpan.FromHours(random.Next(-5, 6)));

            var before = ValueComparison.IsBefore(now, limit);
            var atOrAfter = ValueComparison.IsAtOrAfter(now, limit);

            Assert.That(before, Is.EqualTo(TriLogic.Not(atOrAfter)), $"iteration {i}");
        }
    }

    [Test]
    public void ACC08_Time_IsComparedAsInstantNotLocalClock()
    {
        var utc = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var sameInstantOtherOffset = utc.ToOffset(TimeSpan.FromHours(2));

        Assert.That(ValueComparison.IsBefore(sameInstantOtherOffset, utc), Is.EqualTo(Tri.False));
        Assert.That(ValueComparison.IsAtOrAfter(sameInstantOtherOffset, utc), Is.EqualTo(Tri.True));
    }
}
