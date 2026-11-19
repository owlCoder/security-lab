using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("IDN-09")]
public sealed class BackupCodeGeneratorTests
{
    private readonly BackupCodeGenerator _generator = new();

    private const string ValidCode =
        "01234567-89ABCDEF-01234567-89ABCDEF";

    [Test]
    public void GenerateCode_ReturnsCorrectFormat()
    {
        string code = _generator.GenerateCode();

        Assert.That(code, Has.Length.EqualTo(35));

        Assert.That(
            code,
            Does.Match(@"^[0-9A-F]{8}(-[0-9A-F]{8}){3}$"));
    }

    [Test]
    public void GenerateCode_ProducesUniqueCodes()
    {
        var codes = new HashSet<string>();

        for (int i = 0; i < 1000; i++)
        {
            string code = _generator.GenerateCode();

            Assert.That(codes.Add(code), Is.True);
        }

        Assert.That(codes.Count, Is.EqualTo(1000));
    }

    [Test]
    public void TryComputeHash_ReturnsCorrectSha256Hash()
    {
        bool result = _generator.TryComputeHash(
            ValidCode,
            out string codeHash);

        string expectedHash = Convert.ToHexString(
            SHA256.HashData(
                Encoding.ASCII.GetBytes(ValidCode)));

        Assert.That(result, Is.True);
        Assert.That(codeHash, Is.EqualTo(expectedHash));
        Assert.That(codeHash, Has.Length.EqualTo(64));
    }

    [Test]
    public void TryComputeHash_SameCodeProducesSameHash()
    {
        bool firstResult = _generator.TryComputeHash(
            ValidCode,
            out string firstHash);

        bool secondResult = _generator.TryComputeHash(
            ValidCode,
            out string secondHash);

        Assert.That(firstResult, Is.True);
        Assert.That(secondResult, Is.True);
        Assert.That(firstHash, Is.EqualTo(secondHash));
    }

    [Test]
    public void TryComputeHash_NormalizesLetterCase()
    {
        string lowercaseCode = ValidCode.ToLowerInvariant();

        bool firstResult = _generator.TryComputeHash(
            ValidCode,
            out string firstHash);

        bool secondResult = _generator.TryComputeHash(
            lowercaseCode,
            out string secondHash);

        Assert.That(firstResult, Is.True);
        Assert.That(secondResult, Is.True);
        Assert.That(firstHash, Is.EqualTo(secondHash));
    }

    [TestCase("")]
    [TestCase("short")]
    [TestCase("123456")]
    [TestCase("01234567-89ABCDEF-01234567-89ABCDEG")]
    [TestCase("01234567_89ABCDEF-01234567-89ABCDEF")]
    [TestCase("01234567-89ABCDEF-01234567-89ABCDEF ")]
    public void TryComputeHash_RejectsInvalidCode(
        string invalidCode)
    {
        bool result = _generator.TryComputeHash(
            invalidCode,
            out string codeHash);

        Assert.That(result, Is.False);
        Assert.That(codeHash, Is.Empty);
    }

    [Test]
    public void VerifyHash_AcceptsCorrectCode()
    {
        bool created = _generator.TryComputeHash(
            ValidCode,
            out string codeHash);

        Assert.That(created, Is.True);

        bool verified = _generator.VerifyHash(
            ValidCode,
            codeHash);

        Assert.That(verified, Is.True);
    }

    [Test]
    public void VerifyHash_RejectsDifferentCode()
    {
        _generator.TryComputeHash(
            ValidCode,
            out string codeHash);

        string differentCode =
            "11234567-89ABCDEF-01234567-89ABCDEF";

        bool verified = _generator.VerifyHash(
            differentCode,
            codeHash);

        Assert.That(verified, Is.False);
    }

    [Test]
    public void VerifyHash_AcceptsLowercaseStoredHash()
    {
        _generator.TryComputeHash(
            ValidCode,
            out string codeHash);

        string lowercaseHash = codeHash.ToLowerInvariant();

        bool verified = _generator.VerifyHash(
            ValidCode,
            lowercaseHash);

        Assert.That(verified, Is.True);
    }

    [Test]
    public void VerifyHash_RejectsInvalidStoredHashes()
    {
        string[] invalidHashes =
        [
            "",
            "invalid",
            new string('Z', 64),
            new string('A', 63),
            new string('A', 65)
        ];

        foreach (string hash in invalidHashes)
        {
            bool verified = _generator.VerifyHash(
                ValidCode,
                hash);

            Assert.That(verified, Is.False);
        }
    }

    [Test]
    public void VerifyHash_RejectsInvalidCode()
    {
        _generator.TryComputeHash(
            ValidCode,
            out string codeHash);

        bool verified = _generator.VerifyHash(
            "123456",
            codeHash);

        Assert.That(verified, Is.False);
    }
}
