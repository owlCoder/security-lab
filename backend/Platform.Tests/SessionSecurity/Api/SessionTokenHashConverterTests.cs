using System.Security.Cryptography;
using System.Text;
using Platform.Api.Modules.SessionSecurity.Sessions;
using Platform.SessionSecurity.Infrastructure;

// IDN-11 TIM 09
namespace Platform.Tests.SessionSecurity.Api;

[TestFixture]
public sealed class SessionTokenHashConverterTests
{
    private const string Token = "token-a";

    // SHA-256 as hex: 32 bytes, 64 characters.
    private const int HashSizeInBytes = 32;
    private const int HexLength = 64;

    private static readonly TestCaseData[] MalformedHashes =
    [
        new TestCaseData(new string('A', HexLength - 1)).SetName("TryToBytes_WhenHashHas63Characters_ReturnsFalseAndEmptyBytes"),
        new TestCaseData(new string('A', HexLength + 1)).SetName("TryToBytes_WhenHashHas65Characters_ReturnsFalseAndEmptyBytes"),
        new TestCaseData(new string('A', HexLength - 1) + "G").SetName("TryToBytes_WhenHashHasNonHexCharacter_ReturnsFalseAndEmptyBytes"),
        new TestCaseData(new string('A', HexLength - 1) + " ").SetName("TryToBytes_WhenHashHasWhitespace_ReturnsFalseAndEmptyBytes"),
    ];

    [Test]
    public void TryToBytes_WhenHashIsNull_ReturnsFalse()
    {
        var converted = SessionTokenHashConverter.TryToBytes(null, out var bytes);

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.False);
            Assert.That(bytes, Is.Empty);
        });
    }

    [Test]
    public void TryToBytes_WhenHashIsEmpty_ReturnsFalse()
    {
        var converted = SessionTokenHashConverter.TryToBytes(string.Empty, out var bytes);

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.False);
            Assert.That(bytes, Is.Empty);
        });
    }

    [TestCaseSource(nameof(MalformedHashes))]
    public void TryToBytes_WhenHashIsMalformed_ReturnsFalseAndEmptyBytes(string hexHash)
    {
        var converted = SessionTokenHashConverter.TryToBytes(hexHash, out var bytes);

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.False);
            Assert.That(bytes, Is.Empty);
        });
    }

    [Test]
    public void ToHex_WhenHashIsNull_Throws()
    {
        // null! is intentional: the test checks the guard against a caller that ignores nullability.
        Assert.Throws<ArgumentNullException>(() => SessionTokenHashConverter.ToHex(null!));
    }

    [Test]
    public void TryToBytes_WhenHashHas64HexCharacters_Returns32Bytes()
    {
        var hexHash = new CryptoSessionTokenGenerator().ComputeHash(Token);

        var converted = SessionTokenHashConverter.TryToBytes(hexHash, out var bytes);

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.True);
            Assert.That(bytes, Has.Length.EqualTo(HashSizeInBytes));
        });
    }

    [Test]
    public void ToHex_AfterTryToBytes_ReturnsOriginalHash()
    {
        var hexHash = new CryptoSessionTokenGenerator().ComputeHash(Token);
        SessionTokenHashConverter.TryToBytes(hexHash, out var bytes);

        var roundTripped = SessionTokenHashConverter.ToHex(bytes);

        Assert.That(roundTripped, Is.EqualTo(hexHash));
    }

    // D2: Team 1 stores SHA-256 over the UTF-8 token as bytes (SessionManager.ValidateAsync),
    // the core uses the same hash as hex, so both sides find the same session.
    [Test]
    public void ToHex_OfIdentityTokenHash_EqualsCoreTokenHash()
    {
        var identityHash = SHA256.HashData(Encoding.UTF8.GetBytes(Token));

        var hexHash = SessionTokenHashConverter.ToHex(identityHash);

        Assert.That(hexHash, Is.EqualTo(new CryptoSessionTokenGenerator().ComputeHash(Token)));
    }

    // Lower-case hex encodes the same bytes, so accepting it cannot match a different session.
    [Test]
    public void TryToBytes_WhenHashIsLowerCase_ReturnsSameBytesAsUpperCase()
    {
        var upperCase = new CryptoSessionTokenGenerator().ComputeHash(Token);
        SessionTokenHashConverter.TryToBytes(upperCase, out var expected);

        var converted = SessionTokenHashConverter.TryToBytes(upperCase.ToLowerInvariant(), out var bytes);

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.True);
            Assert.That(bytes, Is.EqualTo(expected));
        });
    }
}
