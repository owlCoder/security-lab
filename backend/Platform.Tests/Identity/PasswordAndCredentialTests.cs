using Platform.Identity.Domain;
using Platform.Identity.Services;

namespace Platform.Tests.Identity;

[TestFixture]
[Category("US-04")]
public class PasswordAndCredentialTests
{
    private const string Password = "correct horse battery staple";

    private readonly BCryptPasswordHasher _hasher = new();
    private string _hash = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => _hash = _hasher.Hash(Password);

    [Test]
    public void Hash_IsBCrypt_NotPlaintext_AndSalted()
    {
        Assert.That(_hash, Does.StartWith("$2"));
        Assert.That(_hash, Does.Not.Contain(Password));
        Assert.That(_hasher.Hash(Password), Is.Not.EqualTo(_hash));
    }

    [Test]
    public void Verify_AcceptsCorrectPassword_AndRejectsWrongOne()
    {
        Assert.That(_hasher.Verify(Password, _hash), Is.True);
        Assert.That(_hasher.Verify("wrong password", _hash), Is.False);
    }

    [Test]
    public void Verify_NeverThrows_ForMissingOrMalformedHash()
    {
        Assert.That(_hasher.Verify(Password, null), Is.False);
        Assert.That(_hasher.Verify(Password, "not-a-bcrypt-hash"), Is.False);
    }

    [Test]
    public void Verify_DoesNotTruncateLongPasswords()
    {
        var longPassword = new string('a', 100);
        var hash = _hasher.Hash(longPassword);

        Assert.That(_hasher.Verify(longPassword, hash), Is.True);
        Assert.That(_hasher.Verify(new string('a', 99) + "b", hash), Is.False);
    }

    [Test]
    public async Task Verifier_AcceptsCorrectCredentials_WithEmailNormalization()
    {
        var id = Guid.NewGuid();
        var verifier = VerifierWith(new CredentialRecord(id, AccountStatus.Active, _hash));

        var result = await verifier.VerifyAsync("  Student@Example.COM ", Password);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.AccountId, Is.EqualTo(id));
        Assert.That(result.Status, Is.EqualTo(AccountStatus.Active));
    }

    [Test]
    public async Task Verifier_ReturnsStatus_ForBlockedAccount_SoTheCallerCanRefuse()
    {
        var verifier = VerifierWith(new CredentialRecord(Guid.NewGuid(), AccountStatus.Blocked, _hash));

        var result = await verifier.VerifyAsync("student@example.com", Password);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Status, Is.EqualTo(AccountStatus.Blocked));
    }

    [Test]
    public async Task Verifier_RejectsWrongPassword_UnknownEmail_AndAccountWithoutHash()
    {
        var withHash = VerifierWith(new CredentialRecord(Guid.NewGuid(), AccountStatus.Active, _hash));
        var withoutHash = VerifierWith(new CredentialRecord(Guid.NewGuid(), AccountStatus.Active, null));
        var empty = VerifierWith(null);

        Assert.That((await withHash.VerifyAsync("student@example.com", "wrong")).IsValid, Is.False);
        Assert.That((await withoutHash.VerifyAsync("student@example.com", Password)).IsValid, Is.False);
        Assert.That((await empty.VerifyAsync("nobody@example.com", Password)).IsValid, Is.False);
    }

    private CredentialVerifier VerifierWith(CredentialRecord? record) => new(new FakeStore(record), _hasher);

    private sealed class FakeStore(CredentialRecord? record) : ICredentialStore
    {
        public Task<CredentialRecord?> FindByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
            Task.FromResult(normalizedEmail == "student@example.com" ? record : null);
    }
}
