using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Auditing;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
[Property("RequirementId", "ACC-08-09")]
public sealed class DecisionTraceFormatterTests
{
    private readonly DecisionTraceFormatter formatter = new();

    private static DecisionTrace Trace(params TraceStep[] steps) => new(steps);

    [Test]
    public void Steps_AreFormattedAsIdentifierAndResult()
    {
        var text = formatter.Format(Trace(
            new TraceStep("resource.course", TraceResult.Passed),
            new TraceStep("grant.holder", TraceResult.Failed)));

        Assert.That(text, Is.EqualTo("resource.course=passed,grant.holder=failed"));
    }

    [TestCase(TraceResult.Passed, "passed")]
    [TestCase(TraceResult.Failed, "failed")]
    [TestCase(TraceResult.Indeterminate, "indeterminate")]
    [TestCase(TraceResult.Skipped, "skipped")]
    public void EveryResult_HasStableName(TraceResult result, string expected) =>
        Assert.That(formatter.Format(Trace(new TraceStep("gate.one", result))), Is.EqualTo($"gate.one={expected}"));

    [Test]
    public void UndefinedResult_IsReportedAsInvalid() =>
        Assert.That(formatter.Format(Trace(new TraceStep("gate.one", (TraceResult)99))), Is.EqualTo("gate.one=invalid"));

    [Test]
    public void EmptyTrace_IsReportedAsNone() =>
        Assert.That(formatter.Format(Trace()), Is.EqualTo("none"));

    [Test]
    public void NullTrace_IsReportedAsNone() =>
        Assert.That(formatter.Format(null), Is.EqualTo("none"));

    [TestCase("")]
    [TestCase("Grant.Holder")]
    [TestCase("grant holder")]
    [TestCase("grant.holder\r\nforged=passed")]
    [TestCase("grant.holder\0")]
    [TestCase("grant,holder")]
    [TestCase("grant=holder")]
    public void UntrustedIdentifier_IsNeverWritten(string id)
    {
        var text = formatter.Format(Trace(new TraceStep(id, TraceResult.Failed)));

        Assert.That(text, Is.EqualTo("invalid=failed"));
    }

    [Test]
    public void TooLongIdentifier_IsNeverWritten() =>
        Assert.That(
            formatter.Format(Trace(new TraceStep(new string('a', 65), TraceResult.Failed))),
            Is.EqualTo("invalid=failed"));

    [Test]
    public void NullIdentifier_IsNeverWritten() =>
        Assert.That(
            formatter.Format(Trace(new TraceStep(null!, TraceResult.Failed))),
            Is.EqualTo("invalid=failed"));

    [Test]
    public void LongTrace_IsCutAtSixteenStepsAndMarkedTruncated()
    {
        var steps = Enumerable.Range(0, 17)
            .Select(index => new TraceStep($"gate.n{index}", TraceResult.Passed))
            .ToArray();

        var text = formatter.Format(Trace(steps));

        Assert.That(text.Split(','), Has.Length.EqualTo(17));
        Assert.That(text, Does.EndWith(",truncated"));
        Assert.That(text, Does.Not.Contain("gate.n16"));
    }

    [Test]
    public void SixteenSteps_AreNotMarkedTruncated()
    {
        var steps = Enumerable.Range(0, 16)
            .Select(index => new TraceStep($"gate.n{index}", TraceResult.Passed))
            .ToArray();

        Assert.That(formatter.Format(Trace(steps)), Does.Not.Contain("truncated"));
    }
}
