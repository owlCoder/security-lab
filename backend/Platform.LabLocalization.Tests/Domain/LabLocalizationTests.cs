using Platform.LabLocalization.Domain;

namespace Platform.LabLocalization.Tests.Domain;

// Without this alias "LabLocalization" would mean the namespace Platform.LabLocalization, not the class.
using LabLocalization = Platform.LabLocalization.Domain.LabLocalization;

[TestFixture]
public class LabLocalizationTests
{
    private static readonly Guid RevisionId = Guid.NewGuid();

    private static string CodeOf(TestDelegate act) => Assert.Throws<DomainRuleException>(act)!.Code;

    private static LabLocalization Valid(string? title = "Title", string? description = "", string? instructions = "") =>
        LabLocalization.Create(RevisionId, LabLanguage.En, title, description, instructions);

    [Test]
    public void Create_keeps_revision_and_language_and_trims_text()
    {
        var localization = LabLocalization.Create(RevisionId, LabLanguage.En, "  Access control  ", " Find it. ", " Step 1 ");

        Assert.That(localization.RevisionId, Is.EqualTo(RevisionId));
        Assert.That(localization.Language, Is.EqualTo(LabLanguage.En));
        Assert.That(localization.Title, Is.EqualTo("Access control"));
        Assert.That(localization.Description, Is.EqualTo("Find it."));
        Assert.That(localization.Instructions, Is.EqualTo("Step 1"));
    }

    [Test]
    public void Empty_revision_id_is_rejected()
    {
        Assert.That(CodeOf(() => LabLocalization.Create(Guid.Empty, LabLanguage.Sr, "Title", "", "")),
            Is.EqualTo(LocalizationErrorCodes.InvalidRevisionId));
    }

    [TestCase(2)]
    [TestCase(99)]
    [TestCase(-1)]
    public void Undefined_language_is_rejected(int raw)
    {
        Assert.That(CodeOf(() => LabLocalization.Create(RevisionId, (LabLanguage)raw, "Title", "", "")),
            Is.EqualTo(LocalizationErrorCodes.InvalidLanguage));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("bad\ttitle")]
    [TestCase("line\nbreak")]
    [TestCase("nul\0char")]
    public void Title_must_be_present_and_free_of_control_characters(string? title)
    {
        Assert.That(CodeOf(() => Valid(title: title)), Is.EqualTo(LocalizationErrorCodes.InvalidTitle));
    }

    [Test]
    public void Title_length_limit_is_inclusive()
    {
        Assert.That(Valid(title: new string('a', LabLocalization.MaxTitleLength)).Title.Length,
            Is.EqualTo(LabLocalization.MaxTitleLength));

        Assert.That(CodeOf(() => Valid(title: new string('a', LabLocalization.MaxTitleLength + 1))),
            Is.EqualTo(LocalizationErrorCodes.InvalidTitle));
    }

    [Test]
    public void Description_and_instructions_may_be_empty()
    {
        var localization = Valid(description: null, instructions: "   ");

        Assert.That(localization.Description, Is.EqualTo(""));
        Assert.That(localization.Instructions, Is.EqualTo(""));
    }

    [Test]
    public void Text_may_contain_line_breaks_but_not_other_control_characters()
    {
        var ok = Valid(description: "line one\r\nline two", instructions: "1.\tOpen\n2.\tCheck");
        Assert.That(ok.Description, Is.EqualTo("line one\r\nline two"));
        Assert.That(ok.Instructions, Is.EqualTo("1.\tOpen\n2.\tCheck"));

        Assert.That(CodeOf(() => Valid(description: "bad\0text")), Is.EqualTo(LocalizationErrorCodes.InvalidDescription));
        Assert.That(CodeOf(() => Valid(instructions: "bad\0text")), Is.EqualTo(LocalizationErrorCodes.InvalidInstructions));
    }

    [Test]
    public void Description_and_instructions_length_limits_are_inclusive()
    {
        Assert.That(Valid(description: new string('d', LabLocalization.MaxDescriptionLength)).Description.Length,
            Is.EqualTo(LabLocalization.MaxDescriptionLength));
        Assert.That(CodeOf(() => Valid(description: new string('d', LabLocalization.MaxDescriptionLength + 1))),
            Is.EqualTo(LocalizationErrorCodes.InvalidDescription));

        Assert.That(Valid(instructions: new string('i', LabLocalization.MaxInstructionsLength)).Instructions.Length,
            Is.EqualTo(LabLocalization.MaxInstructionsLength));
        Assert.That(CodeOf(() => Valid(instructions: new string('i', LabLocalization.MaxInstructionsLength + 1))),
            Is.EqualTo(LocalizationErrorCodes.InvalidInstructions));
    }

    [Test]
    public void Markup_is_stored_as_plain_text()
    {
        var localization = Valid(title: "<script>alert(1)</script>");

        Assert.That(localization.Title, Is.EqualTo("<script>alert(1)</script>"));
    }

    [Test]
    public void Localization_has_no_evaluator_so_a_translation_cannot_change_grading()
    {
        var evaluatorProperties = typeof(LabLocalization).GetProperties()
            .Where(p => p.Name.Contains("Evaluator", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        Assert.That(evaluatorProperties, Is.Empty);
    }

    [Test]
    public void Localization_cannot_be_modified_from_outside_the_type()
    {
        var publicSetters = typeof(LabLocalization).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => p.Name)
            .ToList();

        Assert.That(publicSetters, Is.Empty);
    }
}
