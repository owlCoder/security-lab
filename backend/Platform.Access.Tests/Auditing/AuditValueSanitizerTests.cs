using Platform.Access.Infrastructure.Auditing;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
public sealed class AuditValueSanitizerTests
{
    [TestCase("abc\r\ndef", "abcdef")]
    [TestCase("a\0b", "ab")]
    [TestCase("a\tb\u001bc", "abc")]
    [TestCase("a\u0085b", "ab")]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_ControlAndFormatCharacters_AreRemoved(string raw, string expected)
    {
        Assert.That(AuditValueSanitizer.Clean(raw, 64), Is.EqualTo(expected));
    }

    private static IEnumerable<TestCaseData> UnicodeSeparatorCases()
    {
        yield return new TestCaseData("a" + (char)0x2028 + "b" + (char)0x2029 + "c", "abc");
        yield return new TestCaseData("user" + (char)0x202E + "evil", "userevil");
        yield return new TestCaseData("a" + (char)0x200B + "b", "ab");
    }

    [TestCaseSource(nameof(UnicodeSeparatorCases))]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_UnicodeSeparatorsAndFormatCharacters_AreRemoved(string raw, string expected)
    {
        Assert.That(AuditValueSanitizer.Clean(raw, 64), Is.EqualTo(expected));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_LongValue_IsTruncatedToLimit()
    {
        var raw = new string('a', 10_000);

        Assert.That(AuditValueSanitizer.Clean(raw, 64), Has.Length.EqualTo(64));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_Truncation_NeverLeavesLoneSurrogate()
    {
        var raw = new string('a', 63) + "\U0001F600";

        var cleaned = AuditValueSanitizer.Clean(raw, 64);

        Assert.That(cleaned, Is.EqualTo(new string('a', 63)));
        Assert.That(char.IsHighSurrogate(cleaned[^1]), Is.False);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_LoneSurrogates_AreRemoved()
    {
        Assert.That(AuditValueSanitizer.Clean("a" + (char)0xD800 + "b", 64), Is.EqualTo("ab"), "lone high surrogate");
        Assert.That(AuditValueSanitizer.Clean("a" + (char)0xDC00 + "b", 64), Is.EqualTo("ab"), "lone low surrogate");
        Assert.That(AuditValueSanitizer.Clean("a" + (char)0xDC00 + (char)0xD800 + "b", 64), Is.EqualTo("ab"), "reversed pair");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_ValidSurrogatePair_IsKept_AndCountsAsTwoCharacters()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);

        Assert.That(AuditValueSanitizer.Clean("a" + emoji + "b", 64), Is.EqualTo("a" + emoji + "b"));
        Assert.That(AuditValueSanitizer.Clean("ab" + emoji, 3), Is.EqualTo("ab"), "a pair is never cut in half");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_InvisibleCharacterOutsideBmp_IsRemoved()
    {
        var tag = char.ConvertFromUtf32(0xE0041);

        Assert.That(AuditValueSanitizer.Clean("a" + tag + "b", 64), Is.EqualTo("ab"));
    }

    [TestCase(null)]
    [TestCase("")]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_NullOrEmpty_GivesEmptyString(string? raw)
    {
        Assert.That(AuditValueSanitizer.Clean(raw, 64), Is.Empty);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_PlainValue_IsUnchanged()
    {
        Assert.That(AuditValueSanitizer.Clean("attempt:42", 64), Is.EqualTo("attempt:42"));
    }
}
