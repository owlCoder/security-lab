using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;
using Platform.Identity.Tests.Helpers;
using Platform.Identity.Testing;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>IDN-06 US-18: the MFA lockout after wrong codes (Identity:Mfa:Lockout).</summary>
[NonParallelizable]
public sealed class MfaLockoutIntegrationTests : MfaIntegrationTestBase
{
    [Test]
    public async Task Verification_AfterFailedAttemptThreshold_RevokesPendingSession()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-verify-lockout@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());
        await LoginExistingAsync("mfa-verify-lockout@platform.com");
        var pendingSession = await GetCurrentSessionAsync();

        for (var attempt = 0; attempt < 5; attempt++)
            Assert.That(
                (await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "000000" })).StatusCode,
                Is.EqualTo(HttpStatusCode.Unauthorized));

        var revokedSession = await SessionTestData.GetAsync(Factory, pendingSession!.Id);
        Assert.That(revokedSession!.RevokedAt, Is.Not.Null);
    }

    // US-18 lockout: the lockout is enforced, also against a new password login and a correct code.
    [Test]
    [Category("US-18")]
    public async Task Verification_AfterThreshold_NewPasswordLoginWithCorrectCode_IsRefused()
    {
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-lockout-relogin@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        await LoginExistingAsync("mfa-lockout-relogin@platform.com");
        for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
            await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = "000000" });
        Assert.That((await GetCredentialsAsync(user.Id))!.MfaLockedUntil, Is.GreaterThan(DateTimeOffset.UtcNow));

        // The password is right, so login gives a new MfaPending session; the second factor stays locked.
        await LoginExistingAsync("mfa-lockout-relogin@platform.com");
        var pending = await GetCurrentSessionAsync();
        var response = await Client.PostAsJsonAsync("/api/auth/mfa/verify", new { Code = MfaTestData.CurrentCode(secret) });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("invalid_mfa"),
            "a locked account answers like a wrong code");
        var stored = await SessionTestData.GetAsync(Factory, pending!.Id);
        Assert.That(stored!.AuthLevel, Is.EqualTo(AuthLevel.MfaPending));
        Assert.That(stored.RevokedAt, Is.Not.Null, "the pending session of a locked account is revoked");
        var credentials = await GetCredentialsAsync(user.Id);
        Assert.That(credentials!.MfaLockedUntil, Is.Not.Null, "a correct code does not clear the lockout");
        Assert.That(credentials.LastTotpTimeStep, Is.Null, "no step is consumed while locked");
    }

    [Test]
    [Category("US-18")]
    public async Task Verification_AfterLockoutMinutes_CorrectCodeWorksAgain()
    {
        // The system clock moved by offset; the other TimeProvider members keep their behavior (CallBase).
        var offset = TimeSpan.Zero;
        var clock = new Mock<TimeProvider> { CallBase = true };
        clock.Setup(provider => provider.GetUtcNow()).Returns(() => DateTimeOffset.UtcNow + offset);
        using var shifted = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock.Object);
        }));
        using var client = shifted.CreateCsrfClient();
        var secret = TotpHelpers.GenerateSecret();
        var user = await UserTestData.AddAsync(Factory, "mfa-lockout-expiry@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, secret);
        var locking = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
            await PostWithSessionAsync("/api/auth/mfa/verify", locking.RawToken, new { Code = "000000" }, client);
        Assert.That((await GetCredentialsAsync(user.Id))!.MfaLockedUntil, Is.Not.Null);

        offset = TimeSpan.FromMinutes(LockoutMinutes) + TimeSpan.FromSeconds(1);
        var fresh = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var response = await PostWithSessionAsync(
            "/api/auth/mfa/verify", fresh.RawToken, new { Code = MfaTestData.CurrentCode(secret) }, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await SessionTestData.GetAsync(Factory, fresh.Session.Id))!.RevokedAt, Is.Not.Null);
        Assert.That((await SessionTestData.FindByTokenAsync(Factory, SessionTokenFrom(response)))!.AuthLevel,
            Is.EqualTo(AuthLevel.Active));
        var credentials = await GetCredentialsAsync(user.Id);
        Assert.That(credentials!.MfaLockedUntil, Is.Null);
        Assert.That(credentials.MfaFailedAttempts, Is.Zero);
    }

    [Test]
    [Category("US-18")]
    public async Task Verification_ParallelWrongCodes_CountEveryAttempt_AndLockAtTheThreshold()
    {
        var user = await UserTestData.AddAsync(Factory, "mfa-lockout-parallel@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());

        // One short of the threshold, all at once: no attempt may be lost.
        await Task.WhenAll(await WrongCodesInParallelAsync(user.Id, MaxFailedAttempts - 1));
        var belowThreshold = await GetCredentialsAsync(user.Id);
        Assert.That(belowThreshold!.MfaFailedAttempts, Is.EqualTo(MaxFailedAttempts - 1));
        Assert.That(belowThreshold.MfaLockedUntil, Is.Null);

        // Past the threshold: the first one that reaches it locks, the rest are refused without being counted.
        await Task.WhenAll(await WrongCodesInParallelAsync(user.Id, 3));
        var locked = await GetCredentialsAsync(user.Id);
        Assert.That(locked!.MfaFailedAttempts, Is.EqualTo(MaxFailedAttempts));
        Assert.That(locked.MfaLockedUntil, Is.GreaterThan(DateTimeOffset.UtcNow));
    }

    private static async Task<IEnumerable<Task<HttpResponseMessage>>> WrongCodesInParallelAsync(Guid userId, int count)
    {
        var tokens = new List<string>();
        for (var i = 0; i < count; i++)
            tokens.Add((await SessionTestData.CreateAsync(Factory, userId, AuthLevel.MfaPending)).RawToken);
        return tokens
            .Select(token => PostWithSessionAsync("/api/auth/mfa/verify", token, new { Code = "000000" }))
            .ToList();
    }
}
