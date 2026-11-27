using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Platform.Identity.Enums;
using Platform.Identity.Helpers;
using Platform.Identity.Testing;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Mfa;

/// <summary>
/// AUD-03: at Trace level MVC logs the arguments of every controller action with ToString()
/// ("Executing action method ... with arguments (...)"). The MFA requests must not put the typed TOTP or
/// backup code into that entry.
/// </summary>
[NonParallelizable]
[Category("AUD-03")]
public sealed class MfaLoggingIntegrationTests : MfaIntegrationTestBase
{
    // Not a valid code: the arguments are logged before the action runs, so the outcome does not matter, and a
    // value this distinctive cannot show up in an unrelated log entry by chance.
    private const string TypedCode = "Trace-Log-Mfa-Code-2026";

    [Test]
    [Category("US-18")]
    public async Task Verify_TraceLoggingEnabled_ShouldNotLogTheCode()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "mfa-trace-verify@platform.com", Password);
        await MfaTestData.AddEnabledAsync(Factory, user.Id, TotpHelpers.GenerateSecret());
        var session = await SessionTestData.CreateAsync(Factory, user.Id, AuthLevel.MfaPending);
        var entries = new ConcurrentQueue<string>();
        using var client = ClientWithTraceLogging(entries);

        // Act
        await PostWithSessionAsync("/api/auth/mfa/verify", session.RawToken, new { Code = TypedCode }, client);

        // Assert
        AssertCodeNotLogged(entries, "AuthController.MfaVerify");
    }

    [Test]
    [Category("US-17")]
    public async Task EnrollConfirm_TraceLoggingEnabled_ShouldNotLogTheCode()
    {
        // Arrange
        var user = await UserTestData.AddAsync(Factory, "mfa-trace-confirm@platform.com", Password);
        await MfaTestData.AddPendingAsync(Factory, user.Id, TotpHelpers.GenerateSecret(),
            DateTimeOffset.UtcNow.AddMinutes(10));
        var session = await SessionTestData.CreateAsync(Factory, user.Id);
        var entries = new ConcurrentQueue<string>();
        using var client = ClientWithTraceLogging(entries);

        // Act
        await PostWithSessionAsync("/api/auth/mfa/enroll/confirm", session.RawToken, new { Code = TypedCode }, client);

        // Assert
        AssertCodeNotLogged(entries, "AuthController.MfaEnrollConfirm");
    }

    private static void AssertCodeNotLogged(ConcurrentQueue<string> entries, string action) =>
        Assert.Multiple(() =>
        {
            // The entry that carried the request must be there, so a host that logs nothing cannot pass.
            Assert.That(entries, Has.Some.Matches<string>(entry =>
                entry is not null && entry.Contains(action + " ") && entry.Contains("with arguments")));
            // Filter first, so a failure lists only the entries that leak and not the whole log.
            Assert.That(entries.Where(entry => entry.Contains(TypedCode)), Is.Empty);
        });

    private static HttpClient ClientWithTraceLogging(ConcurrentQueue<string> entries)
    {
        var logger = new Mock<ILogger>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        logger.Setup(l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            // Arguments[2] is the log state (its ToString() is the message), Arguments[3] the exception.
            .Callback(new InvocationAction(call => entries.Enqueue($"{call.Arguments[2]} {call.Arguments[3]}")));

        var provider = new Mock<ILoggerProvider>();
        provider.Setup(p => p.CreateLogger(It.IsAny<string>())).Returns(logger.Object);

        return Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Logging:LogLevel:Default", "Trace");
            builder.UseSetting("Logging:LogLevel:Microsoft", "Trace");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(provider.Object);
            });
        }).CreateCsrfClient();
    }
}
