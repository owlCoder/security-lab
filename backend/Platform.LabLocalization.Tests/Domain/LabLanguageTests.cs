using Platform.LabLocalization.Domain;

namespace Platform.LabLocalization.Tests.Domain;

[TestFixture]
public class LabLanguageTests
{
    [TestCase("sr", LabLanguage.Sr)]
    [TestCase("en", LabLanguage.En)]
    [TestCase("EN", LabLanguage.En)]
    public void Supported_language_is_accepted(string raw, LabLanguage expected)
    {
        Assert.That(LabLanguages.TryParse(raw, out var language), Is.True);
        Assert.That(language, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Missing_language_falls_back_to_sr(string? raw)
    {
        Assert.That(LabLanguages.TryParse(raw, out var language), Is.True);
        Assert.That(language, Is.EqualTo(LabLanguage.Sr));
    }

    [TestCase("xx")]
    [TestCase("1")]
    [TestCase("99")]
    [TestCase("Sr, En")]
    [TestCase("sr-Latn")]
    [TestCase(" en ")]
    public void Unsupported_language_is_rejected(string raw)
    {
        Assert.That(LabLanguages.TryParse(raw, out _), Is.False);
    }
}
