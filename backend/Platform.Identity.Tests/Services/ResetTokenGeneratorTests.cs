using System.Security.Cryptography;
using System.Text;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("IDN-04")]
public sealed class ResetTokenGeneratorTests
{
    private readonly ResetTokenGenerator _generator = new();

    [Test]
    public void GenerateToken_ReturnsCorrectLength()
    {
        string token = _generator.GenerateToken();

        Assert.That(token, Has.Length.EqualTo(43));
    }

    [Test]
    public void GenerateToken_UsesBase64UrlFormat()
    {
        string token = _generator.GenerateToken();

        foreach (char character in token)
        {
            bool valid = char.IsAsciiLetterOrDigit(character)
                || character == '-'
                || character == '_';

            Assert.That(valid, Is.True);
        }
    }

    [Test]
    public void GenerateToken_ProducesDifferentValues()
    {
        var tokens = new HashSet<string>();

        for (int i = 0; i < 1000; i++)
        {
            string token = _generator.GenerateToken();

            Assert.That(tokens.Add(token), Is.True);
        }

        Assert.That(tokens.Count, Is.EqualTo(1000));
    }

    [Test]
    public void TryComputeHash_ReturnsSha256Hash()
    {
        string token = _generator.GenerateToken();

        bool result = _generator.TryComputeHash(
            token, out string tokenHash);

        string expectedHash = Convert.ToHexString(
            SHA256.HashData(Encoding.ASCII.GetBytes(token)));

        Assert.That(result, Is.True);
        Assert.That(tokenHash, Is.EqualTo(expectedHash));
        Assert.That(tokenHash, Has.Length.EqualTo(64));
        Assert.That(tokenHash, Is.Not.EqualTo(token));
    }

    [Test]
    public void TryComputeHash_SameTokenProducesSameHash()
    {
        string token = _generator.GenerateToken();

        bool firstResult = _generator.TryComputeHash(
            token, out string firstHash);

        bool secondResult = _generator.TryComputeHash(
            token, out string secondHash);

        Assert.That(firstResult, Is.True);
        Assert.That(secondResult, Is.True);
        Assert.That(firstHash, Is.EqualTo(secondHash));
    }

    [Test]
    public void VerifyHash_AcceptsCorrectToken()
    {
        string token = _generator.GenerateToken();

        bool created = _generator.TryComputeHash(
            token, out string tokenHash);

        Assert.That(created, Is.True);
        Assert.That(_generator.VerifyHash(token, tokenHash), Is.True);
    }

    [Test]
    public void VerifyHash_RejectsDifferentToken()
    {
        string token = _generator.GenerateToken();

        _generator.TryComputeHash(token, out string tokenHash);

        char replacement = token[0] == 'A' ? 'B' : 'A';
        string changedToken = replacement + token[1..];

        Assert.That(
            _generator.VerifyHash(changedToken, tokenHash),
            Is.False);
    }

    [Test]
    public void TryComputeHash_RejectsInvalidTokenFormats()
    {
        string[] invalidTokens =
        [
            "",
            "short",
            new string('A', 42),
            new string('A', 44),
            new string('A', 42) + "+",
            new string('A', 42) + "/",
            new string('A', 42) + "=",
            new string('A', 42) + " "
        ];

        foreach (string token in invalidTokens)
        {
            bool result = _generator.TryComputeHash(
                token, out string tokenHash);

            Assert.That(result, Is.False);
            Assert.That(tokenHash, Is.Empty);
        }
    }

    [Test]
    public void VerifyHash_RejectsInvalidStoredHashes()
    {
        string token = _generator.GenerateToken();

        Assert.That(_generator.VerifyHash(token, ""), Is.False);
        Assert.That(_generator.VerifyHash(token, "invalid"), Is.False);
        Assert.That(
            _generator.VerifyHash(token, new string('Z', 64)),
            Is.False);
    }

    [Test]
    public void VerifyHash_RejectsInvalidToken()
    {
        string token = _generator.GenerateToken();

        _generator.TryComputeHash(token, out string tokenHash);

        Assert.That(
            _generator.VerifyHash("invalid", tokenHash),
            Is.False);
    }
}
