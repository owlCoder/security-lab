using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;

namespace Laboratories.Tests;

[TestFixture]
public sealed class LabTitleSearchTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    public void TryCreateKey_WhenQueryIsMissingOrBlank_Rejects(string? query)
    {
        Assert.That(LabTitleSearch.TryCreateKey(query, out string? key), Is.False);
        Assert.That(key, Is.Null);
    }

    [TestCase("a\nb")]
    [TestCase("a\rb")]
    [TestCase("a\0b")]
    [TestCase("a\u001bb")]
    [TestCase("rbac\t")]
    public void TryCreateKey_WhenQueryContainsControlCharacters_Rejects(string query)
    {
        Assert.That(LabTitleSearch.TryCreateKey(query, out _), Is.False);
    }

    [Test]
    public void TryCreateKey_WhenQueryIsLongerThanLongestTitle_Rejects()
    {
        string query = new('a', Laboratory.MaxTitleLength + 1);

        Assert.That(LabTitleSearch.TryCreateKey(query, out _), Is.False);
    }

    [Test]
    public void TryCreateKey_AtMaximumLengthAfterTrim_Accepts()
    {
        string query = "  " + new string('a', LabTitleSearch.MaxQueryLength) + "  ";

        Assert.That(LabTitleSearch.TryCreateKey(query, out string? key), Is.True);
        Assert.That(key, Has.Length.EqualTo(LabTitleSearch.MaxQueryLength));
    }

    [TestCase("RBAC", "rbac")]
    [TestCase("  Kontrola   pristupa  ", "kontrola pristupa")]
    [TestCase("Šifrovanje", "sifrovanje")]
    [TestCase("ČĆŽŠĐ čćžšđ", "cczsd cczsd")]
    [TestCase("' OR '1'='1", "' or '1'='1")]
    public void TryCreateKey_NormalizesCaseDiacriticsAndSpaces(string query, string expectedKey)
    {
        Assert.That(LabTitleSearch.TryCreateKey(query, out string? key), Is.True);
        Assert.That(key, Is.EqualTo(expectedKey));
    }

    [Test]
    public void Fold_TreatsQueryAndTitleTheSameWay()
    {
        Assert.That(LabTitleSearch.Fold("Ključevi i šifre"), Is.EqualTo(LabTitleSearch.Fold("KLJUCEVI I SIFRE")));
    }
}
