using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using Platform.Api.Services.Identity;
using Platform.Identity.Testing;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Login;

// IDN-05 through the real Platform.Api host: real login, real sessions and real Postgres.
// The class host lowers the lockout threshold and lifts the per-IP login limiter, so account lockout is
// tested on its own.
[TestFixture]
[Category("IDN-05")]
[NonParallelizable]
public sealed class LoginLockoutIntegrationTests : IntegrationTestBase
{
    private const string Password = "Str0ng-Password!2026";
    private const string WrongPassword = "Wr0ng-Password!2026";
    private const int MaxAttempts = 3;

    private WebApplicationFactory<Program> _host = null!;

    [OneTimeSetUp]
    public void CreateLockoutHost() => _host = CreateHost(MaxAttempts.ToString());

    [OneTimeTearDown]
    public async Task DisposeLockoutHost() => await _host.DisposeAsync();

    [Test]
    public async Task Login_UnknownWrongOrLockedAccount_ShouldReturnIdenticalResponses()
    {
        // Arrange
        var (_, wrongEmail) = await CreateStudentAsync();
        var (_, lockedEmail) = await CreateStudentAsync();
        await LockAsync(lockedEmail);

        // Act
        var unknown = await LoginAsync($"nobody-{Guid.NewGuid():N}@example.com", Password);
        var wrong = await LoginAsync(wrongEmail, WrongPassword);
        var lockedWrong = await LoginAsync(lockedEmail, WrongPassword);
        var lockedCorrect = await LoginAsync(lockedEmail, Password);

        // Assert
        Assert.That(unknown.Status, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(unknown.Body, Does.Contain("invalid_credentials"));
        Assert.That(wrong, Is.EqualTo(unknown));
        Assert.That(lockedWrong, Is.EqualTo(unknown));
        Assert.That(lockedCorrect, Is.EqualTo(unknown));
    }

    [Test]
    public async Task Login_AttemptThatTriggersLockout_ShouldReturnSameResponseAsFirstFailure()
    {
        // Arrange
        var (_, email) = await CreateStudentAsync();
        var first = await LoginAsync(email, WrongPassword);
        await LoginAsync(email, WrongPassword);

        // Act
        var triggering = await LoginAsync(email, WrongPassword);

        // Assert
        Assert.That(triggering, Is.EqualTo(first));
    }

    [Test]
    public async Task Login_SuccessBetweenFailures_ShouldResetCounter_AndReturn200()
    {
        // Arrange
        var (_, email) = await CreateStudentAsync();
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var round = 0; round < 3; round++)
        {
            for (var i = 0; i < MaxAttempts - 1; i++)
                await LoginAsync(email, WrongPassword);

            statuses.Add((await LoginAsync(email, Password)).Status);
        }

        // Assert: typos below the limit never add up across successful logins.
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Login_FailuresFromDifferentIps_ShouldLockAccount_AndReturn401WithoutSession()
    {
        // Arrange
        var (id, email) = await CreateStudentAsync();
        for (var i = 0; i < MaxAttempts; i++)
            Assert.That((await LoginFromAsync($"203.0.113.{100 + i}", email, WrongPassword)).Status,
                Is.EqualTo(StatusCodes.Status401Unauthorized));

        // Act
        var attempt = await LoginFromAsync("203.0.113.200", email, Password);

        // Assert
        var account = await UserTestData.GetAsync(Factory, id);
        Assert.Multiple(() =>
        {
            Assert.That(account!.LockoutEnd, Is.GreaterThan(DateTimeOffset.UtcNow));
            Assert.That(attempt.Status, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(attempt.Body, Does.Contain("invalid_credentials"));
            Assert.That(attempt.RetryAfter, Is.Empty, "Only IP throttling exposes Retry-After.");
            Assert.That(attempt.Cookie, Does.Not.Contain(SessionCookie.Name));
        });
        Assert.That(await SessionTestData.GetForUserAsync(Factory, id), Is.Empty);
    }

    [Test]
    public async Task Login_UnknownEmailAfterFailureLimit_ShouldMatchLockedAccountResponse()
    {
        // Arrange
        var (_, email) = await CreateStudentAsync();
        var unknownEmail = $"nobody-{Guid.NewGuid():N}@example.com";
        for (var i = 0; i < MaxAttempts; i++)
        {
            await LoginFromAsync($"203.0.113.{100 + i}", email, WrongPassword);
            await LoginFromAsync($"203.0.114.{100 + i}", unknownEmail, WrongPassword);
        }

        // Act
        // There is no account row for an unknown address. Its public response must still
        // match a locked account, including when the correct password is supplied.
        var known = await LoginFromAsync("203.0.113.200", email, Password);
        var unknown = await LoginFromAsync("203.0.114.200", unknownEmail, Password);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(known.Status, Is.EqualTo(StatusCodes.Status401Unauthorized));
            Assert.That(known.Cookie, Does.Not.Contain(SessionCookie.Name));
            Assert.That(known.RetryAfter, Is.Empty);
            Assert.That(unknown, Is.EqualTo(known));
        });
    }

    [Test]
    public void Startup_InvalidLockoutSetting_ShouldThrowOptionsValidationException()
    {
        // Arrange
        using var host = CreateHost(maxAttempts: "0");

        // Act & Assert
        Assert.Catch<OptionsValidationException>(() => host.CreateClient());
    }

    private static WebApplicationFactory<Program> CreateHost(string maxAttempts) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Identity:Lockout:MaxFailedAccessAttempts", maxAttempts);
            // These tests exercise account lockout independently of the IP limiter.
            builder.UseSetting("RateLimiting:Login:PermitLimit", "1000");
        });

    private async Task<(int Status, string? ContentType, string CacheControl, string Body, string Cookie, string RetryAfter)>
        LoginFromAsync(string remoteIp, string email, string password)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { email, password }));
        using var tokenClient = _host.CreateClient();
        var csrf = await CsrfPair.FetchAsync(tokenClient);
        var context = await _host.Server.SendAsync(http =>
        {
            http.Request.Method = HttpMethods.Post;
            csrf.ApplyTo(http.Request);
            http.Request.Path = "/api/auth/login";
            http.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            http.Request.ContentType = "application/json";
            http.Request.ContentLength = bytes.Length;
            http.Request.Body = new MemoryStream(bytes);
        });
        using var reader = new StreamReader(context.Response.Body);
        return (context.Response.StatusCode, context.Response.ContentType,
            context.Response.Headers.CacheControl.ToString(), await reader.ReadToEndAsync(),
            context.Response.Headers.SetCookie.ToString(), context.Response.Headers.RetryAfter.ToString());
    }

    private async Task<(HttpStatusCode Status, string? ContentType, string? CacheControl, string Body)> LoginAsync(
        string email, string password)
    {
        using var client = _host.CreateCsrfClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        return (
            response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            response.Headers.CacheControl?.ToString(),
            await response.Content.ReadAsStringAsync());
    }

    private async Task LockAsync(string email)
    {
        for (var i = 0; i < MaxAttempts; i++)
            await LoginAsync(email, WrongPassword);
    }

    private static async Task<(Guid Id, string Email)> CreateStudentAsync()
    {
        var account = await UserTestData.AddAsync(Factory, $"lockout-{Guid.NewGuid():N}@example.com", Password);
        return (account.Id, account.Email);
    }
}
