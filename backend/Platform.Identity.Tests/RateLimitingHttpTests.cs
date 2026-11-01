using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Platform.Api.Modules.Identity.RateLimiting;

namespace Platform.Identity.Tests;

/// <summary>
/// Mala aplikacija sa istim podešavanjem kao Program.cs (AddIdentityRateLimiting / UseIdentityRateLimiting)
/// i probnim endpointima koji postoje samo u testu, jer IDN-02/IDN-04 još nisu implementirani.
/// </summary>
internal sealed class RateLimitHost : IAsyncDisposable
{
    public const string LoginPath = "/test/login";
    public const string ResetPath = "/test/password-reset";
    public const string OpenPath = "/test/open";
    public const string ControllerLoginPath = "/test/controller-login";

    private readonly WebApplication _app;
    private RateLimitHost(WebApplication app) => _app = app;

    public static async Task<RateLimitHost> StartAsync(IDictionary<string, string?> settings, ILoggerProvider? logs = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (logs is not null) builder.Logging.AddProvider(logs);
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddIdentityRateLimiting(builder.Configuration);
        // Login (IDN-02) je MVC kontroler, pa se proverava i atribut [EnableRateLimiting].
        builder.Services.AddControllers().AddApplicationPart(typeof(RateLimitHost).Assembly);

        var app = builder.Build();
        app.UseIdentityRateLimiting();
        app.MapPost(LoginPath, () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.Login);
        app.MapPost(ResetPath, () => Results.Ok()).RequireRateLimiting(RateLimitPolicies.PasswordReset);
        app.MapPost(OpenPath, () => Results.Ok());
        app.MapControllers();
        try { await app.StartAsync(); }
        catch { await app.DisposeAsync(); throw; }
        return new RateLimitHost(app);
    }

    public Task<HttpContext> Post(string path, string remoteIp, string? forwardedFor = null, string? jsonBody = null) =>
        ((TestServer)_app.Services.GetRequiredService<IServer>()).SendAsync(ctx =>
        {
            ctx.Request.Method = HttpMethods.Post;
            ctx.Request.Path = path;
            // TestServer ne postavlja IP sam; bez ovoga bi svi zahtevi delili particiju "unknown".
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            if (forwardedFor is not null) ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;
            if (jsonBody is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(jsonBody);
                ctx.Request.ContentType = "application/json";
                ctx.Request.ContentLength = bytes.Length;
                ctx.Request.Body = new MemoryStream(bytes);
            }
        });

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

/// <summary>Probni kontroler: proverava da limiter radi i sa atributom, kao na LoginController-u.</summary>
[ApiController]
[Route("test/controller-login")]
public sealed class TestLoginController : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public IActionResult Post() => Ok();
}

/// <summary>Hvata sve log poruke da bi test proverio šta se zaista upisuje (AUD-03).</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
    public void Dispose() { }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}

public sealed class RateLimitingHttpTests
{
    private const string Client = "203.0.113.10";
    private const string OtherClient = "203.0.113.20";
    private const string Proxy = "10.0.0.1";

    private static Dictionary<string, string?> Settings(int login = 2, int reset = 2, params (string Key, string Value)[] extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["RateLimiting:Login:PermitLimit"] = login.ToString(),
            ["RateLimiting:Login:WindowSeconds"] = "60",
            ["RateLimiting:PasswordReset:PermitLimit"] = reset.ToString(),
            ["RateLimiting:PasswordReset:WindowSeconds"] = "300"
        };
        foreach (var (key, value) in extra) settings[key] = value;
        return settings;
    }

    private static async Task Exhaust(RateLimitHost host, string path, int count, string ip, Func<int, string?>? forwardedFor = null)
    {
        for (var i = 0; i < count; i++)
        {
            var ctx = await host.Post(path, ip, forwardedFor?.Invoke(i));
            Assert.That(ctx.Response.StatusCode, Is.EqualTo(200), $"Zahtev {i + 1} od {count} je trebalo da prodje.");
        }
    }

    private static async Task<string> Body(HttpContext ctx)
    {
        using var reader = new StreamReader(ctx.Response.Body);
        return await reader.ReadToEndAsync();
    }

    [Test] public async Task RequestsAboveLimitGet429WithRetryAfter()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 2));
        await Exhaust(host, RateLimitHost.LoginPath, 2, Client);

        var rejected = await host.Post(RateLimitHost.LoginPath, Client);
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Response.StatusCode, Is.EqualTo(StatusCodes.Status429TooManyRequests));
            Assert.That(int.TryParse(rejected.Response.Headers.RetryAfter, out var seconds), Is.True, "Retry-After mora biti broj sekundi.");
            Assert.That(seconds, Is.InRange(1, 60));
            Assert.That(rejected.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(rejected.Response.Headers.ContainsKey("X-Correlation-ID"), Is.True);
        });
    }

    [Test] public async Task RejectionBodyIsGenericAndStable()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 1));
        await Exhaust(host, RateLimitHost.LoginPath, 1, Client);

        var first = await Body(await host.Post(RateLimitHost.LoginPath, Client));
        var second = await Body(await host.Post(RateLimitHost.LoginPath, Client));
        using var json = JsonDocument.Parse(first);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("code").GetString(), Is.EqualTo("rate_limited"));
            Assert.That(second, Is.EqualTo(first), "Odgovor ne sme da zavisi od zahteva (nema enumeracije naloga).");
        });
    }

    [Test] public async Task DifferentClientsHaveSeparateLimits()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 2));
        await Exhaust(host, RateLimitHost.LoginPath, 2, Client);
        Assert.That((await host.Post(RateLimitHost.LoginPath, Client)).Response.StatusCode, Is.EqualTo(429));

        var other = await host.Post(RateLimitHost.LoginPath, OtherClient);
        Assert.That(other.Response.StatusCode, Is.EqualTo(200));
    }

    [Test] public async Task LoginAndPasswordResetHaveSeparateCounters()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 1, reset: 1));
        await Exhaust(host, RateLimitHost.LoginPath, 1, Client);
        Assert.That((await host.Post(RateLimitHost.LoginPath, Client)).Response.StatusCode, Is.EqualTo(429));

        var reset = await host.Post(RateLimitHost.ResetPath, Client);
        Assert.That(reset.Response.StatusCode, Is.EqualTo(200));
        Assert.That((await host.Post(RateLimitHost.ResetPath, Client)).Response.StatusCode, Is.EqualTo(429));
    }

    [Test] public async Task EndpointsWithoutPolicyAreNotLimited()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 1));
        await Exhaust(host, RateLimitHost.OpenPath, 20, Client);
    }

    [Test] public async Task SpoofedForwardedForFromUntrustedSourceIsIgnored()
    {
        // Nijedan proxy nije podešen: X-Forwarded-For se mora ignorisati.
        await using var host = await RateLimitHost.StartAsync(Settings(login: 2));
        await Exhaust(host, RateLimitHost.LoginPath, 2, Client, i => $"198.51.100.{i + 1}");

        var spoofed = await host.Post(RateLimitHost.LoginPath, Client, "198.51.100.99");
        Assert.That(spoofed.Response.StatusCode, Is.EqualTo(429));
    }

    [Test] public async Task ForwardedForFromUnlistedProxyIsIgnored()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 2, extra: ("RateLimiting:KnownProxies:0", Proxy)));
        await Exhaust(host, RateLimitHost.LoginPath, 2, Client, i => $"198.51.100.{i + 1}");

        var spoofed = await host.Post(RateLimitHost.LoginPath, Client, "198.51.100.99");
        Assert.That(spoofed.Response.StatusCode, Is.EqualTo(429));
    }

    [Test] public async Task TrustedProxyForwardsRealClientAddress()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 2, extra: ("RateLimiting:KnownProxies:0", Proxy)));
        await Exhaust(host, RateLimitHost.LoginPath, 2, Proxy, _ => Client);
        Assert.That((await host.Post(RateLimitHost.LoginPath, Proxy, Client)).Response.StatusCode, Is.EqualTo(429));

        // Drugi klijent iza istog proxy-ja ima svoj brojač: limit nije po adresi proxy-ja.
        var other = await host.Post(RateLimitHost.LoginPath, Proxy, OtherClient);
        Assert.That(other.Response.StatusCode, Is.EqualTo(200));
    }

    [Test] public async Task TrustedNetworkIsConfiguredAsCidr()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 1, extra: ("RateLimiting:KnownNetworks:0", "10.0.0.0/24")));
        await Exhaust(host, RateLimitHost.LoginPath, 1, "10.0.0.7", _ => Client);

        // Isti klijent preko drugog proxy-ja iz iste mreže: i dalje isti brojač.
        var sameClient = await host.Post(RateLimitHost.LoginPath, "10.0.0.8", Client);
        var other = await host.Post(RateLimitHost.LoginPath, "10.0.0.8", OtherClient);
        Assert.That(sameClient.Response.StatusCode, Is.EqualTo(429));
        Assert.That(other.Response.StatusCode, Is.EqualTo(200));
    }

    [Test] public async Task ClientCannotPrependFakeAddressBehindTrustedProxy()
    {
        // Proxy dopisuje stvarnu adresu na kraj; klijent kontroliše sve ispred nje.
        await using var host = await RateLimitHost.StartAsync(Settings(login: 2, extra: ("RateLimiting:KnownProxies:0", Proxy)));
        await Exhaust(host, RateLimitHost.LoginPath, 2, Proxy, i => $"198.51.100.{i + 1}, {Client}");

        var rotated = await host.Post(RateLimitHost.LoginPath, Proxy, $"198.51.100.99, {Client}");
        Assert.That(rotated.Response.StatusCode, Is.EqualTo(429));
    }

    [Test] public async Task Ipv6AddressesInSameSlash64ShareLimit()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 1));
        await Exhaust(host, RateLimitHost.LoginPath, 1, "2001:db8:1:2::1");

        var sameBlock = await host.Post(RateLimitHost.LoginPath, "2001:db8:1:2:ffff::42");
        var otherBlock = await host.Post(RateLimitHost.LoginPath, "2001:db8:1:3::1");
        Assert.That(sameBlock.Response.StatusCode, Is.EqualTo(429));
        Assert.That(otherBlock.Response.StatusCode, Is.EqualTo(200));
    }

    [Test] public async Task ControllerActionWithAttributeIsLimited()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: 2));
        await Exhaust(host, RateLimitHost.ControllerLoginPath, 2, Client);
        Assert.That((await host.Post(RateLimitHost.ControllerLoginPath, Client)).Response.StatusCode, Is.EqualTo(429));
    }

    [Test] public async Task RejectionIsLoggedWithCorrelationIdButWithoutCredentials()
    {
        const string email = "ana.tajna@example.com";
        const string password = "Lozinka-123!";
        var body = $$"""{"email":"{{email}}","password":"{{password}}"}""";
        var logs = new CapturingLoggerProvider();
        await using var host = await RateLimitHost.StartAsync(Settings(login: 1), logs);
        await host.Post(RateLimitHost.ControllerLoginPath, Client, jsonBody: body);

        var rejected = await host.Post(RateLimitHost.ControllerLoginPath, Client, jsonBody: body);
        var correlation = rejected.Response.Headers["X-Correlation-ID"].ToString();
        var all = string.Join("\n", logs.Messages);
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Response.StatusCode, Is.EqualTo(429));
            Assert.That(correlation, Is.Not.Empty);
            Assert.That(logs.Messages.Any(m => m.Contains("Rate limit exceeded") && m.Contains(correlation)), Is.True,
                "Odbijanje mora biti u logu sa istim correlation ID-jem kao u odgovoru.");
            Assert.That(all, Does.Not.Contain(email));
            Assert.That(all, Does.Not.Contain(password));
        });
    }

    [Test] public async Task RetryAfterStaysWithinThePolicyWindow()
    {
        await using var host = await RateLimitHost.StartAsync(Settings(reset: 1));
        await Exhaust(host, RateLimitHost.ResetPath, 1, Client);

        var rejected = await host.Post(RateLimitHost.ResetPath, Client);
        Assert.That(int.Parse(rejected.Response.Headers.RetryAfter.ToString()), Is.InRange(1, 300));
    }

    [Test] public async Task DefaultLoginLimitIsThirtyPerWindow()
    {
        // Bez podešavanja važi podrazumevani limit od 30 prijava po prozoru.
        await using var host = await RateLimitHost.StartAsync(new Dictionary<string, string?>());
        await Exhaust(host, RateLimitHost.LoginPath, 30, Client);
        Assert.That((await host.Post(RateLimitHost.LoginPath, Client)).Response.StatusCode, Is.EqualTo(429));
    }

    [TestCase(1)] [TestCase(4)]
    public async Task LimitComesFromConfiguration(int permitLimit)
    {
        await using var host = await RateLimitHost.StartAsync(Settings(login: permitLimit));
        await Exhaust(host, RateLimitHost.LoginPath, permitLimit, Client);
        Assert.That((await host.Post(RateLimitHost.LoginPath, Client)).Response.StatusCode, Is.EqualTo(429));
    }

    [TestCase("RateLimiting:Login:PermitLimit", "0")]
    [TestCase("RateLimiting:PasswordReset:WindowSeconds", "0")]
    [TestCase("RateLimiting:Login:PermitLimit", "-5")]
    [TestCase("RateLimiting:KnownProxies:0", "nije-ip")]
    [TestCase("RateLimiting:KnownNetworks:0", "10.0.0.0/99")]
    public void InvalidConfigurationStopsStartup(string key, string value)
    {
        var settings = Settings();
        settings[key] = value;
        Assert.ThrowsAsync<OptionsValidationException>(async () => await RateLimitHost.StartAsync(settings));
    }
}

/// <summary>Proverava da je limiter zaista uključen u Program.cs.</summary>
public sealed class ProgramRateLimitingTests
{
    private sealed class ApiFactory(params (string Key, string Value)[] settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        }
    }

    [Test] public async Task ApiStartsWithDefaultsAndHealthIsNotLimited()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        for (var i = 0; i < 20; i++)
        {
            using var response = await client.GetAsync("/api/health");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test] public void ApiRefusesToStartWithInvalidLimit()
    {
        using var factory = new ApiFactory(("RateLimiting:Login:PermitLimit", "0"));
        var error = Assert.Catch(() => factory.CreateClient());
        Assert.That(Flatten(error).OfType<OptionsValidationException>(), Is.Not.Empty);
    }

    private static IEnumerable<Exception> Flatten(Exception? error)
    {
        for (; error is not null; error = error.InnerException)
        {
            yield return error;
            if (error is AggregateException aggregate)
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Flatten)) yield return inner;
        }
    }
}
