using Platform.Access.Application.Rules;
using Platform.Access.Application.Services;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Rules;

[TestFixture]
[Property("RequirementId", "ACC-08")]
public class SnapshotConditionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static EvaluationSnapshot Snapshot(
        IDictionary<string, AttributeValue>? attributes = null,
        string? status = null,
        DateTimeOffset? now = null,
        bool withResource = true)
    {
        var builder = EvaluationSnapshotBuilder.Default().WithNow(now ?? Now);
        if (withResource)
        {
            builder.WithResource(new ResourceDescriptor(
                "attempt", "r-1", Guid.NewGuid(), Guid.NewGuid(), status,
                new Dictionary<string, AttributeValue>(attributes ?? new Dictionary<string, AttributeValue>())));
        }

        return builder.Build();
    }

    private static Dictionary<string, AttributeValue> Attrs(params (string Name, object? Value)[] items) =>
        items.ToDictionary(
            item => item.Name,
            item => item.Value is null ? AttributeValue.Missing : AttributeValue.Of(item.Value));

    // ---------- AttrEquals ----------

    [Test]
    public void ACC08_AttrEquals_MatchTrue_DifferentFalse()
    {
        var snapshot = Snapshot(Attrs(("Active", true), ("Kind", "exam")));

        Assert.That(SnapshotConditions.AttrEquals("Active", true).Evaluate(snapshot), Is.EqualTo(Tri.True));
        Assert.That(SnapshotConditions.AttrEquals("Active", false).Evaluate(snapshot), Is.EqualTo(Tri.False));
        Assert.That(SnapshotConditions.AttrEquals("Kind", "exam").Evaluate(snapshot), Is.EqualTo(Tri.True));
        Assert.That(SnapshotConditions.AttrEquals("Kind", "Exam").Evaluate(snapshot), Is.EqualTo(Tri.False));
    }

    [Test]
    public void ACC08_AttrEquals_MissingEverywhere_IsUnknownNeverFalse()
    {
        var absentKey = Snapshot(Attrs(("Other", 1)));
        var notPresent = Snapshot(new Dictionary<string, AttributeValue> { ["Active"] = AttributeValue.Missing });
        var presentButNull = Snapshot(new Dictionary<string, AttributeValue> { ["Active"] = new(true, null) });
        var noResource = Snapshot(withResource: false);
        var condition = SnapshotConditions.AttrEquals("Active", true);

        Assert.That(condition.Evaluate(absentKey), Is.EqualTo(Tri.Unknown));
        Assert.That(condition.Evaluate(notPresent), Is.EqualTo(Tri.Unknown));
        Assert.That(condition.Evaluate(presentButNull), Is.EqualTo(Tri.Unknown));
        Assert.That(condition.Evaluate(noResource), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_AttrEquals_WrongTypeInSource_IsUnknownNotFalse() =>
        Assert.That(
            SnapshotConditions.AttrEquals("Count", 5).Evaluate(Snapshot(Attrs(("Count", "5")))),
            Is.EqualTo(Tri.Unknown));

    public static IEnumerable<string> InvalidAttributeNames() =>
        ["", " ", "1abc", "has space", "a\nb", "a\0b", "ａｂｃ", new string('a', 65)];

    [TestCaseSource(nameof(InvalidAttributeNames))]
    public void ACC08_InvalidAttributeName_RejectedAtConstruction(string name)
    {
        Assert.That(() => SnapshotConditions.AttrEquals(name, 1), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => SnapshotConditions.AttributePresent(name), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => SnapshotConditions.NowBefore(name), Throws.InstanceOf<AccessConfigurationException>());
    }

    [Test]
    public void ACC08_NullOrUnsupportedConstant_RejectedAtConstruction()
    {
        Assert.That(() => SnapshotConditions.AttrEquals("A", null!), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => SnapshotConditions.AttrEquals("A", 1.5), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => SnapshotConditions.AttrEquals("A", new object()), Throws.InstanceOf<AccessConfigurationException>());
    }

    // ---------- AttrIn / StatusIn ----------

    [Test]
    public void ACC08_AttrIn_MemberTrue_NonMemberFalse_MissingUnknown()
    {
        var condition = SnapshotConditions.AttrIn("Kind", "exam", "quiz");

        Assert.That(condition.Evaluate(Snapshot(Attrs(("Kind", "quiz")))), Is.EqualTo(Tri.True));
        Assert.That(condition.Evaluate(Snapshot(Attrs(("Kind", "lab")))), Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(Attrs())), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_AttrIn_EmptyOrTooManyOrBadValues_Rejected()
    {
        Assert.That(() => SnapshotConditions.AttrIn("Kind"), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => SnapshotConditions.AttrIn("Kind", Enumerable.Range(0, 65).Cast<object>().ToArray()),
            Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => new AttrInCondition("Kind", new object?[] { "a", null }),
            Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => SnapshotConditions.AttrIn("Kind", Enumerable.Range(0, 64).Cast<object>().ToArray()),
            Throws.Nothing);
    }

    [Test]
    public void ACC08_StatusIn_Whitelist_IsOrdinalAndCaseSensitive()
    {
        var condition = SnapshotConditions.StatusIn("InProgress");

        Assert.That(condition.Evaluate(Snapshot(status: "InProgress")), Is.EqualTo(Tri.True));
        Assert.That(condition.Evaluate(Snapshot(status: "Submitted")), Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(status: "inprogress")), Is.EqualTo(Tri.False));
    }

    [Test]
    public void ACC08_StatusIn_MissingStatusOrResource_IsUnknown()
    {
        var condition = SnapshotConditions.StatusIn("InProgress");

        Assert.That(condition.Evaluate(Snapshot(status: null)), Is.EqualTo(Tri.Unknown));
        Assert.That(condition.Evaluate(Snapshot(withResource: false)), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_StatusIn_EmptyListOrBlankStatus_Rejected()
    {
        Assert.That(() => SnapshotConditions.StatusIn(), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => SnapshotConditions.StatusIn("InProgress", ""), Throws.InstanceOf<AccessConfigurationException>());
        Assert.That(() => SnapshotConditions.StatusIn("InProgress", null!), Throws.InstanceOf<AccessConfigurationException>());
    }

    // ---------- AttributePresent ----------

    [Test]
    public void ACC08_AttributePresent_PresentTrue_AbsentFalse_NoResourceUnknown()
    {
        var condition = SnapshotConditions.AttributePresent("Hold");

        Assert.That(condition.Evaluate(Snapshot(Attrs(("Hold", "yes")))), Is.EqualTo(Tri.True));
        Assert.That(condition.Evaluate(Snapshot(Attrs())), Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(new Dictionary<string, AttributeValue> { ["Hold"] = AttributeValue.Missing })),
            Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(new Dictionary<string, AttributeValue> { ["Hold"] = new(true, null) })),
            Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(withResource: false)), Is.EqualTo(Tri.Unknown));
    }

    // ---------- Vreme: NowBefore / NowAfter ----------

    [Test]
    public void ACC08_NowBefore_BoundaryMinusPlusOneSecond()
    {
        var deadline = Attrs(("Deadline", Now));
        var condition = SnapshotConditions.NowBefore("Deadline");

        Assert.That(condition.Evaluate(Snapshot(deadline, now: Now.AddSeconds(-1))), Is.EqualTo(Tri.True));
        Assert.That(condition.Evaluate(Snapshot(deadline, now: Now)), Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(deadline, now: Now.AddSeconds(1))), Is.EqualTo(Tri.False));
    }

    [Test]
    public void ACC08_NowAfter_IsInclusiveComplementOfNowBefore()
    {
        var deadline = Attrs(("Deadline", Now));
        var after = SnapshotConditions.NowAfter("Deadline");
        var before = SnapshotConditions.NowBefore("Deadline");

        foreach (var offset in new[] { -1, 0, 1 })
        {
            var snapshot = Snapshot(deadline, now: Now.AddSeconds(offset));
            Assert.That(after.Evaluate(snapshot), Is.EqualTo(TriLogic.Not(before.Evaluate(snapshot))), $"offset {offset}");
        }

        Assert.That(after.Evaluate(Snapshot(deadline, now: Now)), Is.EqualTo(Tri.True));
    }

    [Test]
    public void ACC08_NowConditions_MissingOrNonTimeAttribute_AreUnknown()
    {
        foreach (var condition in new[] { SnapshotConditions.NowBefore("Deadline"), SnapshotConditions.NowAfter("Deadline") })
        {
            Assert.That(condition.Evaluate(Snapshot(Attrs())), Is.EqualTo(Tri.Unknown));
            Assert.That(condition.Evaluate(Snapshot(Attrs(("Deadline", "2026-11-20")))), Is.EqualTo(Tri.Unknown));
            Assert.That(condition.Evaluate(Snapshot(Attrs(("Deadline", 5)))), Is.EqualTo(Tri.Unknown));
            Assert.That(condition.Evaluate(Snapshot(withResource: false)), Is.EqualTo(Tri.Unknown));
        }
    }

    [Test]
    public void ACC08_NowConditions_UseSnapshotClockNotAttributeOffset()
    {
        var instant = Now.ToOffset(TimeSpan.FromHours(5));

        Assert.That(
            SnapshotConditions.NowBefore("Deadline").Evaluate(Snapshot(Attrs(("Deadline", instant)), now: Now)),
            Is.EqualTo(Tri.False));
    }

    // ---------- LessThan ----------

    [Test]
    public void ACC08_LessThan_BetweenAttributes_StrictAndFailClosed()
    {
        var condition = SnapshotConditions.LessThan("AttemptsUsed", "MaxAttempts");

        Assert.That(condition.Evaluate(Snapshot(Attrs(("AttemptsUsed", 2), ("MaxAttempts", 3)))), Is.EqualTo(Tri.True));
        Assert.That(condition.Evaluate(Snapshot(Attrs(("AttemptsUsed", 3), ("MaxAttempts", 3)))), Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(Attrs(("AttemptsUsed", 4), ("MaxAttempts", 3)))), Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(Attrs(("AttemptsUsed", 2)))), Is.EqualTo(Tri.Unknown));
        Assert.That(condition.Evaluate(Snapshot(Attrs(("MaxAttempts", 3)))), Is.EqualTo(Tri.Unknown));
        Assert.That(condition.Evaluate(Snapshot(Attrs(("AttemptsUsed", "2"), ("MaxAttempts", 3)))), Is.EqualTo(Tri.Unknown));
        Assert.That(condition.Evaluate(Snapshot(withResource: false)), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_LessThan_AgainstConstant()
    {
        var condition = SnapshotConditions.LessThanValue("Score", 10);

        Assert.That(condition.Evaluate(Snapshot(Attrs(("Score", 9L)))), Is.EqualTo(Tri.True));
        Assert.That(condition.Evaluate(Snapshot(Attrs(("Score", 10)))), Is.EqualTo(Tri.False));
        Assert.That(condition.Evaluate(Snapshot(Attrs())), Is.EqualTo(Tri.Unknown));
    }

    [Test]
    public void ACC08_LessThan_UnorderedConstant_RejectedAtConstruction() =>
        Assert.That(() => LessThanCondition.Value("Score", "ten"), Throws.InstanceOf<AccessConfigurationException>());

    // ---------- Sastavljeno pravilo (ilustracija; nije skup pravila nijedne akcije) ----------

    private static RuleSet<EvaluationSnapshot> StartStyleRules() => new(
        [
            new Rule<EvaluationSnapshot>("r-deadline", SnapshotConditions.NowBefore("Deadline")),
            new Rule<EvaluationSnapshot>("r-attempts", SnapshotConditions.LessThan("AttemptsUsed", "MaxAttempts")),
            new Rule<EvaluationSnapshot>("r-assignment", SnapshotConditions.AttrEquals("AssignmentActive", true)),
        ],
        [
            new Rule<EvaluationSnapshot>("p-archived", SnapshotConditions.AttrEquals("CourseArchived", true)),
        ]);

    private static Dictionary<string, AttributeValue> OpenAttempt(Action<Dictionary<string, AttributeValue>>? change = null)
    {
        var attributes = Attrs(
            ("Deadline", Now.AddHours(1)), ("AttemptsUsed", 1), ("MaxAttempts", 3),
            ("AssignmentActive", true), ("CourseArchived", false));
        change?.Invoke(attributes);
        return attributes;
    }

    [Test]
    public void ACC08_ComposedRules_AllConditionsMet_Permit() =>
        Assert.That(StartStyleRules().Evaluate(Snapshot(OpenAttempt())).IsPermit, Is.True);

    [Test]
    public void ACC08_ComposedRules_DeadlineReached_DeniedAsContextMismatch()
    {
        var result = StartStyleRules().Evaluate(Snapshot(OpenAttempt(a => a["Deadline"] = AttributeValue.Of(Now)), now: Now));

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Deny));
        Assert.That(result.DenialReason, Is.EqualTo(AccessDenialReason.ContextMismatch));
        Assert.That(result.RuleId, Is.EqualTo("r-deadline"));
    }

    [Test]
    public void ACC08_ComposedRules_ArchivedCourse_ProhibitionDenies()
    {
        var result = StartStyleRules().Evaluate(Snapshot(OpenAttempt(a => a["CourseArchived"] = AttributeValue.Of(true))));

        Assert.That(result.Failure, Is.EqualTo(RuleFailure.ProhibitionTriggered));
        Assert.That(result.RuleId, Is.EqualTo("p-archived"));
    }

    [Test]
    public void ACC08_ComposedRules_MissingRequirementAttribute_IndeterminateAsMissingAttribute()
    {
        var result = StartStyleRules().Evaluate(Snapshot(OpenAttempt(a => a.Remove("MaxAttempts"))));

        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Indeterminate));
        Assert.That(result.DenialReason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public void ACC08_ComposedRules_MissingProhibitionAttribute_NeverPermit()
    {
        // Odsustvo atributa zabrane ne sme da znači "zabrana nije aktivna".
        var result = StartStyleRules().Evaluate(Snapshot(OpenAttempt(a => a.Remove("CourseArchived"))));

        Assert.That(result.IsPermit, Is.False);
        Assert.That(result.DenialReason, Is.EqualTo(AccessDenialReason.MissingAttribute));
    }

    [Test]
    public void ACC08_ComposedRules_NoResource_NeverPermit()
    {
        var result = StartStyleRules().Evaluate(Snapshot(withResource: false));

        Assert.That(result.IsPermit, Is.False);
        Assert.That(result.Outcome, Is.EqualTo(PolicyOutcome.Indeterminate));
    }

    [Test]
    public void ACC08_ComposedRules_SameSnapshot_SameDecision()
    {
        var snapshot = Snapshot(OpenAttempt());
        var rules = StartStyleRules();

        Assert.That(rules.Evaluate(snapshot), Is.EqualTo(rules.Evaluate(snapshot)));
    }
}
