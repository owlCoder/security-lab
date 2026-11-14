using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.Identity.Services;

namespace Platform.RateLimiting.Tests;

/// <summary>
/// IDN-05 (J): "Prekomerni neuspesni pokusaji aktiviraju usporavanje/blokadu."
///
/// Za razliku od <see cref="RateLimitingHttpTests"/> (mala test aplikacija sa probnim endpointima), ovi
/// testovi gadjaju PRAVI <c>POST /api/auth/login</c> kroz kompletan pipeline iz Program.cs
/// (Access middleware, rate limiter, AuthController, LoginService). Baza nije potrebna: zamenjeni su
/// samo ICredentialVerifier i ISessionCreator, jer njihova trajna implementacija nije predmet IDN-05.
/// </summary>
[TestFixture, Category("Integration"), Category("IDN-05")]
public sealed class LoginEndpointRateLimitingTests
{
    private const string LoginPath = "/api/auth/login";
    private const string ClientA = "203.0.113.10";
    private const string ClientB = "203.0.113.20";
    private const int Limit = 3;

    private const string KnownEmail = FakeCredentialVerifier.ValidEmail;
    private const string WrongPassword = "Pogresna-Lozinka-1!";
    private const string CorrectPassword = FakeCredentialVerifier.ValidPassword;

    // ---------------------------------------------------------------- limit na pravom endpointu

    [Test]
    public async Task FailedLogins_AboveTheLimit_AreAnswered429_WithRetryAfter()
    {
        using var api = new LoginApiFactory(Limit);

        for (var i = 1; i <= Limit; i++)
            Assert.That((await Login(api, ClientA, KnownEmail, WrongPassword)).Response.StatusCode,
                Is.EqualTo(StatusCodes.Status401Unauthorized), $"Pokusaj {i} treba da stigne do provere lozinke.");

        var blocked = await Login(api, ClientA, KnownEmail, WrongPassword);

        Assert.Multiple(() =>
        {
            Assert.That(blocked.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests),
                "Pravi login endpoint nema [EnableRateLimiting(RateLimitPolicies.Login)] na akciji.");
            Assert.That(int.TryParse(blocked.Response.Headers.RetryAfter.ToString(), out var seconds), Is.True,
                "Retry-After mora biti broj sekundi.");
            Assert.That(seconds, Is.InRange(1, 60));
            Assert.That(blocked.Response.Headers.CacheControl.ToString(), Does.Contain("no-store"));
            Assert.That(blocked.Response.Headers["X-Correlation-ID"].ToString(), Is.Not.Empty);
        });
        Assert.That(await BodyCode(blocked), Is.EqualTo("rate_limited"));
    }

    [Test]
    public async Task ThrottledAttempt_NeverReachesThePasswordCheck()
    {
        using var api = new LoginApiFactory(Limit);
        for (var i = 0; i < Limit; i++) await Login(api, ClientA, KnownEmail, WrongPassword);
        var checksBefore = api.Credentials.Calls;

        var blocked = await Login(api, ClientA, KnownEmail, WrongPassword);

        Assert.Multiple(() =>
        {
            Assert.That(blocked.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
            Assert.That(api.Credentials.Calls, Is.EqualTo(checksBefore),
                "Blokirani zahtev ne sme da potrosi proveru lozinke (brute force nastavlja da dobija odgovor).");
        });
    }

    [Test]
    public async Task CorrectPassword_FromAThrottledClient_IsRejected_AndIssuesNoSession()
    {
        using var api = new LoginApiFactory(Limit);
        for (var i = 0; i < Limit; i++) await Login(api, ClientA, KnownEmail, WrongPassword);

        var attempt = await Login(api, ClientA, KnownEmail, CorrectPassword);

        Assert.Multiple(() =>
        {
            Assert.That(attempt.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests),
                "Posle blokade ni tacna lozinka ne sme da prodje: inace napadac sazna da je pogodio.");
            Assert.That(attempt.Response.Headers.SetCookie.ToString(), Does.Not.Contain(SessionCookie.Name));
        });
    }

    [Test]
    public async Task Throttling_IsPerClient_AnotherClientCanStillLogIn()
    {
        using var api = new LoginApiFactory(Limit);
        for (var i = 0; i < Limit; i++) await Login(api, ClientA, KnownEmail, WrongPassword);
        Assert.That((await Login(api, ClientA, KnownEmail, WrongPassword)).Response.StatusCode,
            Is.EqualTo(StatusCodes.Status429TooManyRequests));

        var other = await Login(api, ClientB, KnownEmail, CorrectPassword);

        Assert.Multiple(() =>
        {
            Assert.That(other.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(other.Response.Headers.SetCookie.ToString(), Does.Contain(SessionCookie.Name));
        });
    }

    [Test]
    public async Task RotatingForwardedForHeader_DoesNotBypassTheLimit()
    {
        // Nijedan proxy nije podesen, pa se X-Forwarded-For mora ignorisati i na pravom endpointu.
        using var api = new LoginApiFactory(Limit);
        for (var i = 0; i < Limit; i++)
            await Login(api, ClientA, KnownEmail, WrongPassword, forwardedFor: $"198.51.100.{i + 1}");

        var spoofed = await Login(api, ClientA, KnownEmail, WrongPassword, forwardedFor: "198.51.100.99");

        Assert.That(spoofed.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
    }

    // ---------------------------------------------------------------- neotkrivanje naloga (IDN-04)

    [Test]
    public async Task Throttle429_IsIdenticalForExistingAndNonExistingEmail()
    {
        using var api = new LoginApiFactory(Limit);
        for (var i = 0; i < Limit; i++)
        {
            await Login(api, ClientA, KnownEmail, WrongPassword);
            await Login(api, ClientB, "nepostojeci@example.test", WrongPassword);
        }

        var known = await Login(api, ClientA, KnownEmail, WrongPassword);
        var unknown = await Login(api, ClientB, "nepostojeci@example.test", WrongPassword);

        Assert.Multiple(() =>
        {
            Assert.That(known.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
            Assert.That(unknown.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
        });
        Assert.That(await Body(unknown), Is.EqualTo(await Body(known)),
            "Odgovor ne sme da otkriva da li nalog postoji.");
    }

    // ---------------------------------------------------------------- audit / log (AUD-03)

    [Test]
    public async Task ThrottledAttempt_IsLoggedWithCorrelationId_ButWithoutCredentials()
    {
        using var api = new LoginApiFactory(Limit);
        for (var i = 0; i < Limit; i++) await Login(api, ClientA, KnownEmail, WrongPassword);

        var blocked = await Login(api, ClientA, KnownEmail, WrongPassword);

        var correlation = blocked.Response.Headers["X-Correlation-ID"].ToString();
        var all = string.Join("\n", api.Logs.Messages);
        Assert.Multiple(() =>
        {
            Assert.That(blocked.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
            Assert.That(api.Logs.Messages.Any(m => m.Contains("Rate limit exceeded") && m.Contains(correlation)), Is.True,
                "Odbijanje mora biti u logu sa istim correlation ID-jem kao u odgovoru.");
            Assert.That(all, Does.Not.Contain(KnownEmail));
            Assert.That(all, Does.Not.Contain(WrongPassword));
            Assert.That(all, Does.Not.Contain(CorrectPassword));
        });
    }

    // ---------------------------------------------------------------- osnovni tok ostaje ispravan

    [Test]
    public async Task SuccessfulLogin_WithinTheLimit_StillWorks_AndTokenIsOnlyInTheCookie()
    {
        using var api = new LoginApiFactory(Limit);

        var ok = await Login(api, ClientA, KnownEmail, CorrectPassword);

        var setCookie = ok.Response.Headers.SetCookie.ToString();
        var body = await Body(ok);
        Assert.Multiple(() =>
        {
            Assert.That(ok.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
            Assert.That(setCookie, Does.Contain(SessionCookie.Name).And.Contain("httponly").IgnoreCase);
            Assert.That(body, Does.Contain(api.Credentials.AccountId.ToString()));
            Assert.That(body, Does.Not.Contain("token").IgnoreCase);
        });
    }

    // Account-level lockout uses the real credential store and PostgreSQL in
    // Platform.Identity.Tests.Controllers.LockoutEndpointTests.

    // ---------------------------------------------------------------- pomocno

    /// <summary>Salje zahtev direktno u pipeline (kao RateLimitHost): jedino tako test bira IP adresu klijenta.</summary>
    private static Task<HttpContext> Login(LoginApiFactory api, string remoteIp, string email, string password,
        string? forwardedFor = null)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { email, password }));
        return api.Server.SendAsync(ctx =>
        {
            ctx.Request.Method = HttpMethods.Post;
            ctx.Request.Path = LoginPath;
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            if (forwardedFor is not null) ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;
            ctx.Request.ContentType = "application/json";
            ctx.Request.ContentLength = bytes.Length;
            ctx.Request.Body = new MemoryStream(bytes);
        });
    }

    private static async Task<string> Body(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private static async Task<string?> BodyCode(HttpContext ctx)
    {
        using var json = JsonDocument.Parse(await Body(ctx));
        return json.RootElement.GetProperty("code").GetString();
    }

    private sealed class LoginApiFactory(int permitLimit) : WebApplicationFactory<Program>
    {
        public FakeCredentialVerifier Credentials { get; } = new();
        public CapturingLoggerProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // WebApplicationFactory runs the real Program registrations: central audit,
            // authentication action policies, session subject provider and strict Access middleware.
            // Do not add a second Access stack or replace its decisions in this factory.
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(Logs);
            });
            builder.UseSetting("RateLimiting:Login:PermitLimit", permitLimit.ToString());
            builder.UseSetting("RateLimiting:Login:WindowSeconds", "60");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICredentialVerifier>();
                services.AddSingleton<ICredentialVerifier>(Credentials);
                services.RemoveAll<ISessionCreator>();
                services.AddSingleton<ISessionCreator, FakeSessionCreator>();
            });
        }
    }
}

/// <summary>Jedan poznat nalog; broji koliko puta je lozinka zaista proveravana.</summary>
internal sealed class FakeCredentialVerifier : ICredentialVerifier
{
    public const string ValidEmail = "student@example.test";
    public const string ValidPassword = "Tacna-Lozinka-2026!";

    private int _calls;
    public Guid AccountId { get; } = Guid.NewGuid();
    public int Calls => Volatile.Read(ref _calls);

    public Task<CredentialResult> VerifyAsync(string email, string password, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        var valid = CredentialVerifier.NormalizeEmail(email) == ValidEmail && password == ValidPassword;
        return Task.FromResult(valid ? new CredentialResult(true, AccountId, AccountStatus.Active) : CredentialResult.Invalid);
    }
}

internal sealed class FakeSessionCreator : ISessionCreator
{
    public Task<IssuedSession?> CreateAsync(Guid userId, AuthenticationLevel level, CancellationToken ct = default) =>
        Task.FromResult<IssuedSession?>(new IssuedSession(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1)));
}
