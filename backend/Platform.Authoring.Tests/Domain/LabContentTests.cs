using Platform.Authoring.Domain;

namespace Platform.Authoring.Tests.Domain;

[TestFixture]
public class LabContentTests
{
    private static string CodeOf(TestDelegate act) => Assert.Throws<DomainRuleException>(act)!.Code;

    [Test]
    public void Create_trims_title_and_goal()
    {
        var content = LabContent.Create("  Access control  ", "  Decide.  ", LabType.AccessControl);

        Assert.That(content.Title, Is.EqualTo("Access control"));
        Assert.That(content.Goal, Is.EqualTo("Decide."));
        Assert.That(content.Type, Is.EqualTo(LabType.AccessControl));
    }

    [Test]
    public void Contents_with_the_same_values_are_equal()
    {
        var a = LabContent.Create("Lab", "Goal", LabType.LogAnalysis);
        var b = LabContent.Create(" Lab ", "Goal", LabType.LogAnalysis);

        Assert.That(a, Is.EqualTo(b));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("bad\ttitle")]
    [TestCase("line\nbreak")]
    [TestCase("nul\0char")]
    public void Title_must_be_present_and_free_of_control_characters(string? title)
    {
        Assert.That(CodeOf(() => LabContent.Create(title, "goal", LabType.AccessControl)),
            Is.EqualTo(AuthoringErrorCodes.InvalidTitle));
    }

    [Test]
    public void Title_length_limit_is_inclusive()
    {
        var ok = LabContent.Create(new string('a', LabContent.MaxTitleLength), "", LabType.AccessControl);
        Assert.That(ok.Title.Length, Is.EqualTo(LabContent.MaxTitleLength));

        Assert.That(CodeOf(() => LabContent.Create(new string('a', LabContent.MaxTitleLength + 1), "", LabType.AccessControl)),
            Is.EqualTo(AuthoringErrorCodes.InvalidTitle));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void A_draft_may_have_an_empty_goal(string? goal)
    {
        var content = LabContent.Create("Lab", goal, LabType.AccessControl);

        Assert.That(content.Goal, Is.EqualTo(""));
    }

    [Test]
    public void Goal_may_contain_line_breaks_but_not_other_control_characters()
    {
        var ok = LabContent.Create("Lab", "line one\r\nline two\tindented", LabType.AccessControl);
        Assert.That(ok.Goal, Is.EqualTo("line one\r\nline two\tindented"));

        Assert.That(CodeOf(() => LabContent.Create("Lab", "bad\0goal", LabType.AccessControl)),
            Is.EqualTo(AuthoringErrorCodes.InvalidGoal));
    }

    [Test]
    public void Goal_length_limit_is_inclusive()
    {
        var ok = LabContent.Create("Lab", new string('g', LabContent.MaxGoalLength), LabType.AccessControl);
        Assert.That(ok.Goal.Length, Is.EqualTo(LabContent.MaxGoalLength));

        Assert.That(CodeOf(() => LabContent.Create("Lab", new string('g', LabContent.MaxGoalLength + 1), LabType.AccessControl)),
            Is.EqualTo(AuthoringErrorCodes.InvalidGoal));
    }

    [TestCase(0)]
    [TestCase(99)]
    [TestCase(-1)]
    public void Undefined_lab_type_is_rejected(int raw)
    {
        Assert.That(CodeOf(() => LabContent.Create("Lab", "goal", (LabType)raw)),
            Is.EqualTo(AuthoringErrorCodes.InvalidLabType));
    }

    [Test]
    public void Content_cannot_be_modified_from_outside_the_type()
    {
        var publicSetters = typeof(LabContent).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.That(publicSetters.Count, Is.EqualTo(0));
    }
}
