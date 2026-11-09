using Microsoft.Extensions.Options;
using Moq;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[Category("US-04")]
public class PasswordAndCredentialTests
{
    private const string Password = "correct horse battery staple";

    // Passwords the hasher and verifier must handle the same way: ASCII, Cyrillic, mixed scripts, special characters.
    private static readonly string[] Passwords =
    [
        Password,
        "бурек је најјачи искрено",
        "thisIsAnotherPasswordTestWohooo",
        "Pisi kako читаш citaj kako пишеш",
        "p@$$w0rd!\"'<>&%\\"
    ];

    private readonly BCryptPasswordHasher _hasher = new();

    [TestCaseSource(nameof(Passwords))]
    public void Hash_IsBCrypt_NotPlaintext_AndSalted(string password)
    {
        var hash = _hasher.Hash(password);

        Assert.That(hash, Does.StartWith("$2"));
        Assert.That(hash, Does.Not.Contain(password));
        Assert.That(_hasher.Hash(password), Is.Not.EqualTo(hash));
    }

    [TestCaseSource(nameof(Passwords))]
    public void Verify_AcceptsCorrectPassword_AndRejectsWrongOne(string password)
    {
        var hash = _hasher.Hash(password);

        Assert.That(_hasher.Verify(password, hash), Is.True);
        Assert.That(_hasher.Verify("wrong password", hash), Is.False);
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

    [TestCaseSource(nameof(Passwords))]
    public async Task Verifier_AcceptsCorrectCredentials_WithEmailNormalization(string password)
    {
        var id = Guid.NewGuid();
        var verifier = VerifierWith(new CredentialRecord(id, AccountStatus.Active, _hasher.Hash(password)), out var store);

        var result = await verifier.VerifyAsync("  Student@Example.COM ", password);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.AccountId, Is.EqualTo(id));
        Assert.That(result.Status, Is.EqualTo(AccountStatus.Active));
        store.Verify(s => s.FindByEmailAsync("student@example.com", It.IsAny<CancellationToken>()), Times.Once());
    }

    [TestCaseSource(nameof(Passwords))]
    public async Task Verifier_ReturnsStatus_ForBlockedAccount_SoTheCallerCanRefuse(string password)
    {
        var verifier = VerifierWith(new CredentialRecord(Guid.NewGuid(), AccountStatus.Blocked, _hasher.Hash(password)));

        var result = await verifier.VerifyAsync("student@example.com", password);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.Status, Is.EqualTo(AccountStatus.Blocked));
    }

    [Test]
    public async Task Verifier_RejectsWrongPassword_UnknownEmail_AndAccountWithoutHash()
    {
        var withHash = VerifierWith(new CredentialRecord(Guid.NewGuid(), AccountStatus.Active, _hasher.Hash(Password)));
        var withoutHash = VerifierWith(new CredentialRecord(Guid.NewGuid(), AccountStatus.Active, null));
        var empty = VerifierWith(null);

        Assert.That((await withHash.VerifyAsync("student@example.com", "wrong")).IsValid, Is.False);
        Assert.That((await withoutHash.VerifyAsync("student@example.com", Password)).IsValid, Is.False);
        Assert.That((await empty.VerifyAsync("nobody@example.com", Password)).IsValid, Is.False);
    }

    [Test]
    public async Task Verifier_UnknownEmail_RunsTheSameStatements_SoTimingDoesNotReveal()
    {
        var verifier = VerifierWith(null, out var store);

        var result = await verifier.VerifyAsync("  Student@Example.COM ", Password);

        Assert.That(result, Is.EqualTo(CredentialResult.Invalid));
        VerifyFailurePath(store, "student@example.com");
    }

    [Test]
    public async Task Verifier_LockedAccount_IsRefused_EvenWithTheCorrectPassword_AndRunsTheSameStatements()
    {
        var id = Guid.NewGuid();
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(5);
        var verifier = VerifierWith(
            new CredentialRecord(id, AccountStatus.Active, _hasher.Hash(Password), LockoutEnd: lockoutEnd), out var store);

        var result = await verifier.VerifyAsync("student@example.com", Password);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.IsLockedOut, Is.True);
        Assert.That(result.AccountId, Is.EqualTo(id));
        VerifyFailurePath(store, "student@example.com");
    }

    [Test]
    public async Task Verifier_RefusesACorrectPassword_WhenTheAccountLockedDuringTheCheck()
    {
        var id = Guid.NewGuid();
        var verifier = VerifierWith(new CredentialRecord(id, AccountStatus.Active, _hasher.Hash(Password)), out var store);
        store.Setup(s => s.TryClearFailuresAsync(id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        store.Setup(s => s.LockIfLimitReachedAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTimeOffset.UtcNow.AddMinutes(5));

        var result = await verifier.VerifyAsync("student@example.com", Password);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.IsLockedOut, Is.True);
    }

    private static void VerifyFailurePath(Mock<ICredentialStore> store, string normalizedEmail)
    {
        store.Verify(s => s.ReserveAttemptAsync(
            normalizedEmail, It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once());
        store.Verify(s => s.LockIfLimitReachedAsync(
            normalizedEmail, It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once());
        store.Verify(s => s.TryClearFailuresAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    private CredentialVerifier VerifierWith(CredentialRecord? record) => VerifierWith(record, out _);

    // The store answers only for the normalized e-mail "student@example.com"; any other lookup returns null.
    private CredentialVerifier VerifierWith(CredentialRecord? record, out Mock<ICredentialStore> store)
    {
        store = new Mock<ICredentialStore>();
        store.Setup(s => s.FindByEmailAsync("student@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        store.Setup(s => s.TryClearFailuresAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return new CredentialVerifier(store.Object, _hasher, Options.Create(new IdentityLockoutOptions()), TimeProvider.System);
    }
}
