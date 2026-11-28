using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Platform.Identity.Testing;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration.Login;

/// <summary>
/// AUD-03, SEC-02: at Trace level MVC logs the arguments of every controller action with ToString()
/// ("Executing action method ... with arguments (...)"). The login request must not put the typed
/// password or e-mail into that entry.
/// </summary>
[TestFixture, NonParallelizable, Category("IDN-02"), Category("AUD-03")]
public class LoginLoggingIntegrationTests : IntegrationTestBase
{
    private const string Email = "trace-log@example.test";
    private const string Password = "Trace-Log-Password-2026!";
    private const string WrongPassword = "Wrong-Trace-Log-Password-2026!";

    [TestCase(Password, HttpStatusCode.OK)]
    [TestCase(WrongPassword, HttpStatusCode.Unauthorized)]
    public async Task Login_TraceLoggingEnabled_ShouldNotLogPasswordOrEmail(string password, HttpStatusCode expected)
    {
        // Arrange
        await UserTestData.AddAsync(Factory, Email, Password);
        var entries = new ConcurrentQueue<string>();
        using var client = ClientWithTraceLogging(entries);

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = Email, password });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(expected));
            // The entry that carried the request must be there, so a host that logs nothing cannot pass.
            Assert.That(entries, Has.Some.Matches<string>(entry =>
                entry.Contains("AuthController.Login") && entry.Contains("with arguments")));
            // Filter first, so a failure lists only the entries that leak and not the whole log.
            Assert.That(entries.Where(entry => entry.Contains(password)), Is.Empty);
            Assert.That(entries.Where(entry => entry.Contains(Email)), Is.Empty);
        });
    }

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
