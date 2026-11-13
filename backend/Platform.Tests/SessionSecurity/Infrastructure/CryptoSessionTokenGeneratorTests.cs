using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Infrastructure;

[TestFixture]
public sealed class CryptoSessionTokenGeneratorTests
{
    private const int SampleSize = 1000;

    // 32 random bytes encoded as Base64Url without padding.
    private const int ExpectedTokenLength = 43;

    // SHA-256 as upper-case hex.
    private const int ExpectedHashLength = 64;

    [Test]
    public void Generate_ThousandTokens_AreAllUnique()
    {
        var generator = new CryptoSessionTokenGenerator();

        var tokens = Enumerable.Range(0, SampleSize).Select(_ => generator.Generate().Value).ToList();

        Assert.That(tokens, Is.Unique);
    }

    [Test]
    public void Generate_ReturnsBase64UrlTokenWith256Bits()
    {
        var token = new CryptoSessionTokenGenerator().Generate().Value;

        Assert.Multiple(() =>
        {
            Assert.That(token, Has.Length.EqualTo(ExpectedTokenLength));
            Assert.That(token, Does.Match("^[A-Za-z0-9_-]+$"));
        });
    }

    [Test]
    public void Generate_ReturnsSha256HashOfTokenThatIsNotTheToken()
    {
        var generator = new CryptoSessionTokenGenerator();

        var issued = generator.Generate();

        Assert.Multiple(() =>
        {
            Assert.That(issued.Hash, Is.EqualTo(generator.ComputeHash(issued.Value)));
            Assert.That(issued.Hash, Has.Length.EqualTo(ExpectedHashLength));
            Assert.That(issued.Hash, Does.Match("^[0-9A-F]+$"));
            Assert.That(issued.Hash, Does.Not.Contain(issued.Value));
        });
    }

    [Test]
    public void ComputeHash_WhenTokensDiffer_ReturnsDifferentHashes()
    {
        var generator = new CryptoSessionTokenGenerator();

        Assert.That(generator.ComputeHash("token-a"), Is.Not.EqualTo(generator.ComputeHash("token-b")));
    }

    [Test]
    public void ComputeHash_WhenTokenIsEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CryptoSessionTokenGenerator().ComputeHash(string.Empty));
    }
}
