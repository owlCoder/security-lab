using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OtpNet;
using Platform.Api.Contracts.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Testing;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>
/// US-22 (IDN-06): the TOTP secret, the otpauth:// URI, the codes and the ciphertext never reach a log record, and
/// the secret leaves the API only once, in the mfa/enroll response. Every record of the test host is captured,
/// at every level, with its message, structured values, scopes and exception.
/// </summary>
[NonParallelizable]
[Category("US-22")]
public sealed class MfaSecretExposureIntegrationTests : MfaIntegrationTestBase
{
    private ConcurrentQueue<string> _records = null!;
    private WebApplicationFactory<Program> _host = null!;
    private HttpClient _client = null!;

    private IReadOnlyList<string> Records => _records.ToArray();

    [SetUp]
    public void StartCapturingHost()
    {
        _records = new ConcurrentQueue<string>();
        var provider = CapturingLoggerProvider(_records).Object;
        _host = Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(provider);
            // A rule for this provider (by its proxy type name) wins over every category rule from appsettings,
            // so nothing is filtered out.
            logging.Services.Configure<LoggerFilterOptions>(options => options.Rules.Add(
                new LoggerFilterRule(provider.GetType().FullName, null, LogLevel.Trace, null)));
        }));
        _client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [TearDown]
    public void StopCapturingHost()
    {
        _client.Dispose();
        _host.Dispose();
    }

    // Every MFA step of the criteria: enroll, confirm, verify with success, wrong code, replayed code and the
    // attempt threshold. The capture itself is checked too, otherwise an empty capture would always pass.
    [Test]
    public async Task MfaFlow_EveryStep_NoLogRecordContainsSecretUriCodeOrCiphertext()
    {
        // Arrange
        const string email = "mfa-exposure-logs@platform.com";
        var user = await UserTestData.AddAsync(Factory, email, Password);
        var session = await LoginTokenAsync(email);

        // Act
        var enroll = await PostAsync("/api/auth/mfa/enroll", session, null);
        var uri = (await enroll.Content.ReadFromJsonAsync<MfaEnrollResponse>())!.Uri;
        var secret = SecretFrom(uri);
        var pendingCiphertext = (await GetCredentialsAsync(user.Id))!.PendingTotpSecretCiphertext!;
        var confirmCode = MfaTestData.CurrentCode(secret);
        var confirm = await PostAsync("/api/auth/mfa/enroll/confirm", session, new { Code = confirmCode });
        var ciphertext = (await GetCredentialsAsync(user.Id))!.TotpSecretCiphertext!;

        var verifyCode = MfaTestData.NextCode(secret);
        var verified = await PostAsync("/api/auth/mfa/verify", await LoginTokenAsync(email), new { Code = verifyCode });
        var replayed = await PostAsync("/api/auth/mfa/verify", await LoginTokenAsync(email), new { Code = verifyCode });

        var wrongCode = WrongCode(secret);
        var pending = await LoginTokenAsync(email);
        var wrong = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < MaxFailedAttempts; attempt++)
            wrong.Add((await PostAsync("/api/auth/mfa/verify", pending, new { Code = wrongCode })).StatusCode);

        // Assert
        Assert.That(enroll.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(verified.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(replayed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(wrong, Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await GetCredentialsAsync(user.Id))!.MfaLockedUntil, Is.Not.Null, "the threshold was reached");

        var records = Records;
        Assert.That(records.Any(r => r.Contains("/api/auth/mfa/verify")), Is.True,
            "the capture sees the request logs of the MFA routes");
        Assert.That(records, Has.None.Contains(secret), "Base32 secret");
        Assert.That(records, Has.None.Contains("otpauth://"), "otpauth:// URI");
        Assert.That(records, Has.None.Contains(pendingCiphertext), "pending ciphertext");
        Assert.That(records, Has.None.Contains(ciphertext), "ciphertext");
        foreach (var code in new[] { confirmCode, verifyCode, wrongCode })
            Assert.That(records.Where(r => ContainsCode(r, code)), Is.Empty, $"TOTP code {code}");
    }

    // Negative control: the check itself finds a leak, so the test above cannot pass by not looking.
    [Test]
    public void LogCapture_RecordWithTheSecretInAStructuredValue_IsFound()
    {
        // Arrange
        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        var logger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("US22.Control");

        // Act
        logger.LogDebug("Enrollment for {Uri}", $"otpauth://totp/x?secret={secret}");

        // Assert
        Assert.That(Records, Has.Some.Contains(secret));
        Assert.That(Records, Has.Some.Contains("otpauth://"));
    }

    // MVC logs action arguments with ToString() at Trace level; a record would print the code and the URI.
    [Test]
    public void MfaContracts_ToString_HideCodeAndUri()
    {
        // Arrange
        var request = new MfaVerifyRequest("375532");
        var response = new MfaEnrollResponse("otpauth://totp/SecurityLab:x?secret=JBSWY3DPEHPK3PXP");

        // Act
        var text = $"{request} {response}";

        // Assert
        Assert.That(text, Does.Not.Contain("375532").And.Not.Contain("otpauth://").And.Not.Contain("JBSWY3DPEHPK3PXP"));
        Assert.That(request.Code, Is.EqualTo("375532"), "only the text form is hidden, not the value");
    }

    [Test]
    public async Task Enroll_Response_IsTheOnlyResponseWithTheSecret()
    {
        // Arrange
        const string email = "mfa-exposure-responses@platform.com";
        await UserTestData.AddAsync(Factory, email, Password);
        var session = await LoginTokenAsync(email);

        // Act
        var enroll = await PostAsync("/api/auth/mfa/enroll", session, null);
        var enrollText = await TextOf(enroll);
        var secret = SecretFrom((await enroll.Content.ReadFromJsonAsync<MfaEnrollResponse>())!.Uri);
        var confirmCode = MfaTestData.CurrentCode(secret);
        var confirm = await PostAsync("/api/auth/mfa/enroll/confirm", session, new { Code = confirmCode });

        var verifyCode = MfaTestData.NextCode(secret);
        var verify = await PostAsync("/api/auth/mfa/verify", await LoginTokenAsync(email), new { Code = verifyCode });
        var active = SessionTokenFrom(verify);
        var me = await GetAsync("/api/auth/me", active);
        var sessions = await GetAsync("/api/auth/sessions", active);
        var replayed = await PostAsync("/api/auth/mfa/verify", await LoginTokenAsync(email), new { Code = verifyCode });

        // Assert
        Assert.That(enroll.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(enrollText, Does.Contain(secret), "the enroll response hands out the secret once");
        var later = new Dictionary<string, HttpResponseMessage>
        {
            ["confirm"] = confirm, ["verify"] = verify, ["me"] = me, ["sessions"] = sessions, ["replayed verify"] = replayed
        };
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(verify.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(me.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(sessions.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        foreach (var (name, response) in later)
        {
            var text = await TextOf(response);
            Assert.That(text, Does.Not.Contain(secret), $"{name}: secret");
            Assert.That(text, Does.Not.Contain("otpauth://"), $"{name}: otpauth:// URI");
            Assert.That(ContainsCode(text, confirmCode) || ContainsCode(text, verifyCode), Is.False, $"{name}: code");
        }
    }

    // A refused confirm (wrong code) and a second enroll on an active MFA do not hand out a secret either.
    [Test]
    public async Task RefusedConfirmAndSecondEnroll_Responses_HaveNoSecret()
    {
        // Arrange
        const string email = "mfa-exposure-refused@platform.com";
        await UserTestData.AddAsync(Factory, email, Password);
        var session = await LoginTokenAsync(email);
        var enroll = await PostAsync("/api/auth/mfa/enroll", session, null);
        var secret = SecretFrom((await enroll.Content.ReadFromJsonAsync<MfaEnrollResponse>())!.Uri);

        // Act
        var wrongCode = WrongCode(secret);
        var refused = await PostAsync("/api/auth/mfa/enroll/confirm", session, new { Code = wrongCode });
        await PostAsync("/api/auth/mfa/enroll/confirm", session, new { Code = MfaTestData.CurrentCode(secret) });
        var second = await PostAsync("/api/auth/mfa/enroll", session, null);

        // Assert
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        foreach (var response in new[] { refused, second })
        {
            var text = await TextOf(response);
            Assert.That(text, Does.Not.Contain(secret).And.Not.Contain("otpauth://"));
            Assert.That(ContainsCode(text, wrongCode), Is.False);
        }
    }

    // US-15: every POST carries a CSRF pair. A pair is bound to the session it was issued for, and these tests use
    // several session cookies, so the login gets a pair without a session and each POST one for its own session.
    private async Task<string> LoginTokenAsync(string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { Email = email, Password })
        };
        (await CsrfPair.FetchAsync(_client)).ApplyTo(request);
        var response = await _client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return SessionTokenFrom(response);
    }

    private async Task<HttpResponseMessage> PostAsync(string url, string rawToken, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        (await CsrfPair.FetchAsync(_client, rawToken)).ApplyTo(request, rawToken);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetAsync(string url, string rawToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={rawToken}");
        return await _client.SendAsync(request);
    }

    /// <summary>Headers and body: a secret in a header (for example a cookie) is an exposure too.</summary>
    private static async Task<string> TextOf(HttpResponseMessage response) =>
        string.Join('\n', response.Headers.Concat(response.Content.Headers)
            .Select(header => $"{header.Key}: {string.Join(", ", header.Value)}"))
        + '\n' + await response.Content.ReadAsStringAsync();

    private static string SecretFrom(string uri) => HttpUtility.ParseQueryString(new Uri(uri).Query)["secret"]!;

    /// <summary>A six-digit code that is valid in no step around now, so it is always refused.</summary>
    private static string WrongCode(string secret)
    {
        var totp = new Totp(Base32Encoding.ToBytes(secret));
        var valid = Enumerable.Range(-2, 5).Select(step => totp.ComputeTotp(DateTime.UtcNow.AddSeconds(30 * step)));
        return Enumerable.Range(0, 1_000_000).Select(n => n.ToString("D6")).First(code => !valid.Contains(code));
    }

    /// <summary>
    /// The code as a value of its own: not part of a longer run of digits or hex characters, as in trace IDs,
    /// GUIDs or ticks, where six given digits can appear by chance.
    /// </summary>
    private static bool ContainsCode(string text, string code) =>
        Regex.IsMatch(text, $"(?<![0-9A-Fa-f]){code}(?![0-9A-Fa-f])");

    /// <summary>
    /// A mocked logger provider: every logger it creates records each call as text (category, message, structured
    /// values, scopes and exception) into <paramref name="records"/>.
    /// </summary>
    private static Mock<ILoggerProvider> CapturingLoggerProvider(ConcurrentQueue<string> records)
    {
        var provider = new Mock<ILoggerProvider>();
        IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
        provider.As<ISupportExternalScope>()
            .Setup(p => p.SetScopeProvider(It.IsAny<IExternalScopeProvider>()))
            .Callback<IExternalScopeProvider>(scopeProvider => scopes = scopeProvider);
        provider.Setup(p => p.CreateLogger(It.IsAny<string>())).Returns<string>(category =>
        {
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            logger.Setup(l => l.BeginScope(It.IsAny<It.IsAnyType>()))
                .Returns(new InvocationFunc(call => scopes.Push(call.Arguments[0])));
            logger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
                .Callback(new InvocationAction(call =>
                {
                    var (state, exception) = (call.Arguments[2], (Exception?)call.Arguments[3]);
                    var message = ((Delegate)call.Arguments[4]).DynamicInvoke(state, exception);
                    var record = new StringBuilder()
                        .Append(call.Arguments[0]).Append(' ').Append(category).Append(' ').Append(call.Arguments[1])
                        .Append(": ").AppendLine(message?.ToString());
                    AppendValues(record, state);
                    scopes.ForEachScope((scope, text) => AppendValues(text.Append("scope: "), scope), record);
                    if (exception is not null)
                        record.AppendLine(exception.ToString());
                    records.Enqueue(record.ToString());
                }));
            return logger.Object;
        });
        return provider;
    }

    private static void AppendValues(StringBuilder record, object? state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
            foreach (var (key, value) in values)
                record.Append(key).Append('=').Append(value).AppendLine();
        else
            record.Append(state).AppendLine();
    }
}
