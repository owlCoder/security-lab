using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;
using Platform.Api.Interfaces.Identity;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Login;

// IDN-05: lockout on the real login store (identity.accounts), in the shared test database.
// The class host replaces only the clock and the password hasher, both with Moq mocks: the clock so a test
// can move time, the hasher so a test can count, hold or abandon hash checks while the real BCrypt runs.
[TestFixture]
[Category("IDN-05")]
[NonParallelizable]
public sealed class AccountLockoutIntegrationTests : IntegrationTestBase
{
    private const string Password = "Str0ng-Password!2026";
    private const string WrongPassword = "Wr0ng-Password!2026";
    private const int MaxAttempts = 3;
    private const int LockoutMinutes = 15;

    private static readonly DateTimeOffset Start = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan HoldTimeout = TimeSpan.FromSeconds(30);

    private readonly BCryptPasswordHasher _bcrypt = new();
    private readonly Mock<TimeProvider> _clockMock = new() { CallBase = true };
    private readonly Mock<IPasswordHasher> _hasherMock = new(MockBehavior.Strict);
    private WebApplicationFactory<Program> _host = null!;
    private TimeProvider _clock => _clockMock.Object;
    private IPasswordHasher _hasher => _hasherMock.Object;
    private DateTimeOffset _now;

    // Hash checks held inside Verify (HoldHashChecks) and checks abandoned midway (AbandonAsync).
    private CountdownEvent? _entered;
    private ManualResetEventSlim? _open;
    private int _holds;
    private int _abandon;

    [OneTimeSetUp]
    public void CreateLockoutHost()
    {
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(password => _bcrypt.Hash(password));
        _hasherMock.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns<string, string?>(VerifyHash);

        _host = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Identity:Lockout:MaxFailedAccessAttempts", MaxAttempts.ToString());
            builder.UseSetting("Identity:Lockout:LockoutMinutes", LockoutMinutes.ToString());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_clockMock.Object);
                services.RemoveAll<IPasswordHasher>();
                services.AddSingleton(_hasherMock.Object);
            });
        });
    }

    [OneTimeTearDown]
    public async Task DisposeLockoutHost()
    {
        ReleaseHashChecks();
        _open?.Dispose();
        await _host.DisposeAsync();
    }

    [SetUp]
    public void ResetDoubles()
    {
        ReleaseHashChecks();
        Volatile.Write(ref _abandon, 0);
        _hasherMock.Invocations.Clear();
        SetNow(Start);
    }

    [Test]
    public async Task Verify_FailuresBelowLimit_ShouldNotLockAccount()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();

        // Act
        for (var i = 0; i < MaxAttempts - 1; i++)
            Assert.That(await VerifyAsync(email, WrongPassword), Is.EqualTo(CredentialResult.Invalid));

        // Assert
        var account = await FindAsync(id);
        Assert.That(account.FailedLoginCount, Is.EqualTo(MaxAttempts - 1));
        Assert.That(account.LockoutEnd, Is.Null);
    }

    [Test]
    public async Task Verify_FailuresReachLimit_ShouldLockForConfiguredTime()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();

        // Act
        var last = await FailAsync(email, MaxAttempts);

        // Assert
        Assert.That(last.IsLockedOut, Is.True);
        Assert.That(last.IsValid, Is.False);
        Assert.That(last.AccountId, Is.EqualTo(id));

        var account = await FindAsync(id);
        Assert.That(account.LockoutEnd, Is.EqualTo(_clock.GetUtcNow().AddMinutes(LockoutMinutes)));
        Assert.That(account.FailedLoginCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Verify_CorrectPasswordWhileLocked_ShouldReturnLockedOut()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);

        // Act
        var result = await VerifyAsync(email, Password);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.IsLockedOut, Is.True);
        Assert.That((await FindAsync(id)).LockoutEnd, Is.GreaterThan(_clock.GetUtcNow()));
    }

    [Test]
    public async Task Verify_AttemptsWhileLocked_ShouldNotCount_AndNotExtendLockout()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        var lockoutEnd = (await FindAsync(id)).LockoutEnd;
        Advance(TimeSpan.FromMinutes(1));

        // Act
        await FailAsync(email, MaxAttempts + 1);

        // Assert
        var account = await FindAsync(id);
        Assert.That(account.LockoutEnd, Is.EqualTo(lockoutEnd));
        Assert.That(account.FailedLoginCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Verify_CorrectPasswordBelowLimit_ShouldResetFailureCounter()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts - 1);

        // Act
        var success = await VerifyAsync(email, Password);

        // Assert
        Assert.That(success.IsValid, Is.True);
        Assert.That(success.AccountId, Is.EqualTo(id));
        Assert.That(success.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That((await FindAsync(id)).FailedLoginCount, Is.EqualTo(0));

        // After the reset a full run of failures is needed again.
        Assert.That((await FailAsync(email, MaxAttempts - 1)).IsLockedOut, Is.False);
    }

    [Test]
    public async Task Verify_ExpiredLockout_ShouldSucceed_AndClearLockout()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        Advance(TimeSpan.FromMinutes(LockoutMinutes).Add(TimeSpan.FromSeconds(1)));

        // Act
        var result = await VerifyAsync(email, Password);

        // Assert
        Assert.That(result.IsValid, Is.True);
        Assert.That((await FindAsync(id)).LockoutEnd, Is.Null);
    }

    [Test]
    public async Task Verify_LockoutEndingExactlyNow_ShouldSucceed()
    {
        // Arrange
        var (_, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        Advance(TimeSpan.FromMinutes(LockoutMinutes));

        // Act
        var result = await VerifyAsync(email, Password);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task Verify_AfterExpiredLockout_ShouldNeedFullRunOfFailuresAgain()
    {
        // Arrange
        var (_, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        Advance(TimeSpan.FromMinutes(LockoutMinutes).Add(TimeSpan.FromSeconds(1)));

        // Act
        var belowLimit = await FailAsync(email, MaxAttempts - 1);
        var atLimit = await FailAsync(email, 1);

        // Assert
        Assert.That(belowLimit.IsLockedOut, Is.False);
        Assert.That(atLimit.IsLockedOut, Is.True);
    }

    [Test]
    public async Task Verify_ParallelFailures_ShouldCountEveryAttempt()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();

        // Act
        await Task.WhenAll(Enumerable.Range(0, MaxAttempts)
            .Select(_ => Task.Run(() => VerifyAsync(email, WrongPassword))));

        // Assert
        Assert.That((await FindAsync(id)).LockoutEnd, Is.GreaterThan(_clock.GetUtcNow()));
    }

    [Test]
    public async Task Verify_EmailCaseAndWhitespace_ShouldCountAgainstSameAccount()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();

        // Act
        await VerifyAsync(email.ToUpperInvariant(), WrongPassword);
        await VerifyAsync($"  {email} ", WrongPassword);

        // Assert
        Assert.That((await FindAsync(id)).FailedLoginCount, Is.EqualTo(2));
    }

    [Test]
    public async Task Verify_FailuresOnOtherAccount_ShouldNotAffectThisAccount()
    {
        // Arrange
        var (_, email) = await CreateAccountAsync();
        var (otherId, otherEmail) = await CreateAccountAsync();

        // Act
        await FailAsync(email, MaxAttempts);

        // Assert
        Assert.That((await FindAsync(otherId)).FailedLoginCount, Is.EqualTo(0));
        Assert.That((await VerifyAsync(otherEmail, Password)).IsValid, Is.True);
    }

    [Test]
    public async Task Verify_UnknownEmail_ShouldReturnInvalid_AndCheckHash_AndCreateNoAccount()
    {
        // Arrange
        var email = NewEmail();

        var results = new List<CredentialResult>();

        // Act
        for (var i = 0; i < MaxAttempts + 1; i++)
            results.Add(await VerifyAsync(email, WrongPassword));

        // Assert
        Assert.That(results, Is.All.EqualTo(CredentialResult.Invalid));
        VerifyHashChecks(MaxAttempts + 1);
        Assert.That(await UserTestData.GetByEmailAsync(Factory, email), Is.Null);
    }

    [Test]
    public async Task Verify_LockedAccount_ShouldStillCheckHash()
    {
        // Arrange
        var (_, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);
        _hasherMock.Invocations.Clear();

        // Act
        await VerifyAsync(email, WrongPassword);

        // Assert
        VerifyHashChecks(1);
    }

    [Test]
    public async Task Verify_CorrectPasswordInFlightWhenAccountLocks_ShouldReturnLockedOut()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        HoldHashChecks(1);
        DateTimeOffset? lockoutEnd;
        CredentialResult result;

        try
        {
            // Act
            var inFlight = Task.Run(() => VerifyAsync(email, Password));
            WaitUntilHeld();
            await FailAsync(email, MaxAttempts);
            lockoutEnd = (await FindAsync(id)).LockoutEnd;
            ReleaseHashChecks();
            result = await inFlight;
        }
        finally
        {
            ReleaseHashChecks();
        }

        // Assert
        Assert.That(lockoutEnd, Is.GreaterThan(_clock.GetUtcNow()));
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.IsLockedOut, Is.True);
        Assert.That((await FindAsync(id)).LockoutEnd, Is.EqualTo(lockoutEnd));
    }

    [Test]
    public async Task Verify_GuessesOverLimitWhileEarlierRunning_ShouldReturnLockedOut()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        HoldHashChecks(MaxAttempts);
        CredentialResult[] overLimit;
        CredentialResult[] results;

        try
        {
            var running = Enumerable.Range(0, MaxAttempts)
                .Select(_ => Task.Run(() => VerifyAsync(email, WrongPassword)))
                .ToArray();
            WaitUntilHeld();

            // Act
            // No failure has finished yet, so only charging attempts up front can refuse these.
            overLimit = await Task.WhenAll(Enumerable.Range(0, 5)
                .Select(_ => Task.Run(() => VerifyAsync(email, Password))));

            ReleaseHashChecks();
            results = await Task.WhenAll(running);
        }
        finally
        {
            ReleaseHashChecks();
        }

        // Assert
        Assert.That(overLimit.Select(r => r.IsLockedOut), Is.All.True);
        Assert.That(overLimit.Concat(results).Select(r => r.IsValid), Is.All.False);
        Assert.That((await FindAsync(id)).LockoutEnd, Is.GreaterThan(_clock.GetUtcNow()));
    }

    [Test]
    public async Task Verify_CorrectPasswordInFlightBelowLimit_ShouldSucceed()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        HoldHashChecks(1);
        CredentialResult result;

        try
        {
            // Act
            var inFlight = Task.Run(() => VerifyAsync(email, Password));
            WaitUntilHeld();
            await VerifyAsync(email, WrongPassword);
            ReleaseHashChecks();
            result = await inFlight;
        }
        finally
        {
            ReleaseHashChecks();
        }

        // Assert
        Assert.That(result.IsValid, Is.True);

        var account = await FindAsync(id);
        Assert.That(account.FailedLoginCount, Is.EqualTo(0));
        Assert.That(account.LockoutEnd, Is.Null);
    }

    [Test]
    public async Task Verify_AbandonedChecks_ShouldCount_AndLockUntilLockoutEnds()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();

        // Act
        await AbandonAsync(email, MaxAttempts);

        // Assert
        Assert.That((await FindAsync(id)).FailedLoginCount, Is.EqualTo(MaxAttempts));

        // Abandoned guesses are not free: the next attempt gets no password check.
        Assert.That((await VerifyAsync(email, Password)).IsLockedOut, Is.True);

        Advance(TimeSpan.FromMinutes(LockoutMinutes));
        Assert.That((await VerifyAsync(email, Password)).IsValid, Is.True);
    }

    // An administrator unlock can commit after a login charged its attempt and before it reads the account.
    // The row lock of the unlock does not hold that login back: its UPDATE filters out the locked row without
    // waiting, so it charges nothing. The read then shows the account unlocked; using the password result there
    // would make every login in flight during an unlock a free guess (rule: only a charged attempt counts).
    [TestCase(Password)]
    [TestCase(WrongPassword)]
    public async Task Verify_AdminUnlockAfterChargeOnLockedAccount_ShouldReturnLockedOut(string password)
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        await FailAsync(email, MaxAttempts);

        // Act
        var result = await VerifyWithUnlockAfterChargeAsync(id, email, password);

        // Assert
        Assert.That(result, Is.EqualTo(CredentialResult.LockedOut(id)), "the outcome must not depend on the password");
        var account = await FindAsync(id);
        Assert.That(account.LockoutEnd, Is.Null, "the unlock itself stands");
        Assert.That(account.FailedLoginCount, Is.EqualTo(0), "the refused attempt is not counted");

        // The next attempt is charged as usual, so it may use the password.
        Assert.That((await VerifyAsync(email, Password)).IsValid, Is.True);
    }

    // Same window, but the account is not locked when the attempt starts: interrupted requests left the counter at
    // the limit, so the attempt sets the lockout itself. It updated the row, yet it was not charged, and the unlock
    // that commits before the read hides the lockout it set. Its password result must not be used either.
    [TestCase(Password)]
    [TestCase(WrongPassword)]
    public async Task Verify_AdminUnlockAfterAttemptSetLockout_ShouldReturnLockedOut(string password)
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();
        await AbandonAsync(email, MaxAttempts);
        var before = await FindAsync(id);
        Assert.That(before.FailedLoginCount, Is.EqualTo(MaxAttempts), "the counter is at the limit");
        Assert.That(before.LockoutEnd, Is.Null, "the account is not locked yet");

        // Act
        // The unlock asserts that it had a running lockout to clear: the one this attempt set.
        var result = await VerifyWithUnlockAfterChargeAsync(id, email, password);

        // Assert
        Assert.That(result, Is.EqualTo(CredentialResult.LockedOut(id)), "the outcome must not depend on the password");
        var account = await FindAsync(id);
        Assert.That(account.LockoutEnd, Is.Null, "the unlock itself stands");
        Assert.That(account.FailedLoginCount, Is.EqualTo(0), "the refused attempt is not counted");

        // The next attempt is charged as usual, so it may use the password.
        Assert.That((await VerifyAsync(email, Password)).IsValid, Is.True);
    }

    [Test]
    public async Task ReserveAttempt_AtLimit_ShouldSetLockout_AndNotCharge()
    {
        // Arrange
        var (id, email) = await CreateAccountAsync();

        for (var i = 1; i <= MaxAttempts; i++)
        {
            Assert.That(await ReserveAsync(email), Is.True, "an attempt up to the limit is charged");
            Assert.That((await FindAsync(id)).FailedLoginCount, Is.EqualTo(i));
        }

        // Act
        var atLimit = await ReserveAsync(email);

        // Assert
        Assert.That(atLimit, Is.False, "at the limit the attempt sets the lockout and is not charged");
        var locked = await FindAsync(id);
        Assert.That(locked.LockoutEnd, Is.EqualTo(_clock.GetUtcNow().AddMinutes(LockoutMinutes)));
        Assert.That(locked.FailedLoginCount, Is.EqualTo(0));

        Assert.That(await ReserveAsync(email), Is.False, "a locked account charges nothing");
        Assert.That(await ReserveAsync(NewEmail()), Is.False, "an unknown e-mail charges nothing");

        Advance(TimeSpan.FromMinutes(LockoutMinutes));
        Assert.That(await ReserveAsync(email), Is.True, "after the lockout ends the next attempt is charged");
        Assert.That((await FindAsync(id)).FailedLoginCount, Is.EqualTo(1));
    }

    private void SetNow(DateTimeOffset now)
    {
        _now = now;
        _clockMock.Setup(t => t.GetUtcNow()).Returns(now);
    }

    private void Advance(TimeSpan by) => SetNow(_now + by);

    // The real BCrypt check, with optional holds (so a test can act while checks are in flight) and abandons
    // (requests interrupted during the hash).
    private bool VerifyHash(string password, string? passwordHash)
    {
        if (Volatile.Read(ref _entered) is { } entered && Interlocked.Decrement(ref _holds) >= 0)
        {
            entered.Signal();
            _open!.Wait(HoldTimeout);
        }

        if (Volatile.Read(ref _abandon) > 0 && Interlocked.Decrement(ref _abandon) >= 0)
            throw new OperationCanceledException();

        return _bcrypt.Verify(password, passwordHash);
    }

    private void VerifyHashChecks(int count) =>
        _hasherMock.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string?>()), Times.Exactly(count));

    // Holds the next <paramref name="count"/> checks inside Verify until ReleaseHashChecks.
    // The previous gate is disposed only here: threads released from it may still be leaving its Wait.
    private void HoldHashChecks(int count)
    {
        _open?.Dispose();
        _open = new ManualResetEventSlim();
        _holds = count;
        Volatile.Write(ref _entered, new CountdownEvent(count));
    }

    private void WaitUntilHeld() => Assert.That(_entered!.Wait(HoldTimeout), Is.True);

    private void ReleaseHashChecks()
    {
        var entered = Interlocked.Exchange(ref _entered, null);
        _open?.Set();
        entered?.Dispose();
    }

    private async Task<bool> ReserveAsync(string email)
    {
        using var scope = _host.Services.CreateScope();
        var now = _clock.GetUtcNow();
        return await scope.ServiceProvider.GetRequiredService<ICredentialStore>()
            .ReserveAttemptAsync(email, MaxAttempts, now, now.AddMinutes(LockoutMinutes));
    }

    // Commits an administrator unlock right after the attempt is charged and before the account is read:
    // the window a login in flight hits during a real unlock, made deterministic. Every other store call goes
    // to the real EF store. The verifier is built by hand because the mock store has to wrap this scope's
    // EfCredentialStore and unlock this particular account; its other dependencies are the class host's.
    private async Task<CredentialResult> VerifyWithUnlockAfterChargeAsync(Guid id, string email, string password)
    {
        using var scope = _host.Services.CreateScope();
        var inner = scope.ServiceProvider.GetRequiredService<ICredentialStore>();
        var storeMock = new Mock<ICredentialStore>(MockBehavior.Strict);
        storeMock.Setup(s => s.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(inner.FindByEmailAsync);
        storeMock.Setup(s => s.ReserveAttemptAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(async (string normalizedEmail, int maxAttempts, DateTimeOffset now, DateTimeOffset lockoutEnd,
                CancellationToken ct) =>
            {
                var charged = await inner.ReserveAttemptAsync(normalizedEmail, maxAttempts, now, lockoutEnd, ct);
                await UnlockAsync(id);
                return charged;
            });
        storeMock.Setup(s => s.LockIfLimitReachedAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns<string, int, DateTimeOffset, DateTimeOffset, CancellationToken>(inner.LockIfLimitReachedAsync);
        storeMock.Setup(s => s.TryClearFailuresAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, DateTimeOffset, CancellationToken>(inner.TryClearFailuresAsync);

        var store = storeMock.Object;
        var verifier = new CredentialVerifier(
            store, _hasher, scope.ServiceProvider.GetRequiredService<IOptions<IdentityLockoutOptions>>(), _clock);

        return await verifier.VerifyAsync(email, password);
    }

    // The real administrator unlock (IDN-05), committed in its own scope, connection and transaction.
    private async Task UnlockAsync(Guid id)
    {
        using var scope = _host.Services.CreateScope();
        var statusStore = scope.ServiceProvider.GetRequiredService<IAccountStatusStore>();
        await using var work = await statusStore.BeginAsync();
        var account = await work.FindForUpdateAsync(id);
        Assert.That(await work.ClearLockoutAsync(account!, _clock.GetUtcNow()), Is.Not.Null, "the account was locked");
        await work.CommitAsync();
    }

    private static string NewEmail() => $"lockout-{Guid.NewGuid():N}@example.com";

    private static async Task<(Guid Id, string Email)> CreateAccountAsync()
    {
        var account = await UserTestData.AddAsync(Factory, NewEmail(), Password);
        return (account.Id, account.Email);
    }

    private async Task<CredentialResult> FailAsync(string email, int attempts)
    {
        var last = CredentialResult.Invalid;
        for (var i = 0; i < attempts; i++)
            last = await VerifyAsync(email, WrongPassword);

        return last;
    }

    // Requests interrupted during the hash: each attempt stays charged, and none of them reaches the lockout check.
    private async Task AbandonAsync(string email, int attempts)
    {
        Volatile.Write(ref _abandon, attempts);
        for (var i = 0; i < attempts; i++)
            await Assert.CatchAsync<OperationCanceledException>(() => VerifyAsync(email, WrongPassword));
    }

    private async Task<CredentialResult> VerifyAsync(string email, string password)
    {
        using var scope = _host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICredentialVerifier>().VerifyAsync(email, password);
    }

    private static async Task<UserAccount> FindAsync(Guid id) => (await UserTestData.GetAsync(Factory, id))!;
}
