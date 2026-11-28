using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Platform.Api.Services.Identity;

namespace Platform.Identity.Tests.Services;

[TestFixture]
public sealed class SmtpEmailSenderTests
{
    [Test]
    public async Task Production_rejects_plaintext_smtp_before_connecting()
    {
        var sender = CreateSender(
            "Production",
            port: 1025,
            enableSsl: false,
            allowInsecureLocalMailpit: true);

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            async () => await sender.SendAsync("to@example.test", "subject", "body"));

        Assert.That(exception!.Message, Does.Contain("Unencrypted SMTP"));
    }

    [Test]
    public async Task Development_rejects_plaintext_without_explicit_mailpit_opt_in()
    {
        var sender = CreateSender(
            "Development",
            port: 1025,
            enableSsl: false);

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            async () => await sender.SendAsync("to@example.test", "subject", "body"));

        Assert.That(exception!.Message, Does.Contain("Unencrypted SMTP"));
    }

    [Test]
    public async Task Development_rejects_plaintext_to_non_loopback_hosts()
    {
        var sender = CreateSender(
            "Development",
            port: 1025,
            enableSsl: false,
            allowInsecureLocalMailpit: true,
            host: "smtp.example.test");

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            async () => await sender.SendAsync("to@example.test", "subject", "body"));

        Assert.That(exception!.Message, Does.Contain("Unencrypted SMTP"));
    }

    [Test]
    public async Task Development_rejects_plaintext_mailpit_when_authentication_is_configured()
    {
        var sender = CreateSender(
            "Development",
            port: 1025,
            enableSsl: false,
            allowInsecureLocalMailpit: true,
            userName: "smtp-user",
            password: "smtp-password");

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            async () => await sender.SendAsync("to@example.test", "subject", "body"));

        Assert.That(exception!.Message, Does.Contain("Unencrypted SMTP"));
    }

    [Test]
    public async Task Explicit_development_mailpit_opt_in_allows_local_plaintext_smtp()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RejectMailFromAsync(listener);
        var sender = CreateSender(
            "Development",
            port,
            enableSsl: false,
            allowInsecureLocalMailpit: true,
            host: "127.0.0.1");

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            async () => await sender.SendAsync("to@example.test", "subject", "body"));
        await server;

        Assert.That(exception!.Message, Is.EqualTo("The email could not be delivered."));
        Assert.That(exception.InnerException, Is.Not.Null);
    }

    [Test]
    public async Task Production_tls_configuration_attempts_starttls()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RequireStartTlsAsync(listener);
        var sender = CreateSender("Production", port, enableSsl: true, host: "127.0.0.1");

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            async () => await sender.SendAsync("to@example.test", "subject", "body"));
        var startTlsRequested = await server;

        Assert.That(exception!.Message, Is.EqualTo("The email could not be delivered."));
        Assert.That(startTlsRequested, Is.True);
    }

    [Test]
    public async Task Unavailable_smtp_server_is_reported_as_delivery_failure()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = CloseConnectionWithoutGreetingAsync(listener);
        var sender = CreateSender("Production", port, enableSsl: true, host: "127.0.0.1");

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            async () => await sender.SendAsync("to@example.test", "subject", "body"));
        await server;

        Assert.That(exception!.Message, Is.EqualTo("The email could not be delivered."));
        Assert.That(exception.InnerException, Is.Not.Null);
    }

    [Test]
    public async Task Smtp_server_rejection_is_reported_as_delivery_failure()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = RejectMailFromAsync(listener);
        var sender = CreateSender(
            "Development",
            port,
            enableSsl: false,
            allowInsecureLocalMailpit: true,
            host: "localhost");

        var exception = await Assert.ThrowsAsync<EmailDeliveryException>(
            async () => await sender.SendAsync("to@example.test", "subject", "body"));
        await server;

        Assert.That(exception!.Message, Is.EqualTo("The email could not be delivered."));
        Assert.That(exception.InnerException, Is.Not.Null);
    }

    [Test]
    public void Confirmation_delivery_logs_do_not_include_smtp_exception_or_confirmation_link()
    {
        const string confirmationLink = "https://frontend.example.test/confirm-email#one-time-secret";
        var logger = new RecordingLogger();
        var exception = new EmailDeliveryException(
            "Delivery failed.",
            new InvalidOperationException(confirmationLink));

        EmailDeliveryLogging.ConfirmationFailed(logger, Guid.NewGuid(), exception);

        Assert.That(string.Join(Environment.NewLine, logger.Messages), Does.Not.Contain(confirmationLink));
        Assert.That(logger.Exceptions, Has.All.Null);
    }

    private static SmtpEmailSender CreateSender(
        string environment,
        int port,
        bool enableSsl,
        bool allowInsecureLocalMailpit = false,
        string host = "localhost",
        string? userName = null,
        string? password = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = host,
            ["Email:Smtp:Port"] = port.ToString(),
            ["Email:Smtp:EnableSsl"] = enableSsl.ToString(),
            ["Email:Smtp:AllowInsecureLocalMailpit"] = allowInsecureLocalMailpit.ToString(),
            ["Email:Smtp:User"] = userName,
            ["Email:Smtp:Password"] = password,
            ["Email:FromAddress"] = "sender@example.test"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(environment => environment.EnvironmentName).Returns(environment);

        return new SmtpEmailSender(configuration, hostEnvironment.Object);
    }

    private static async Task RejectMailFromAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true)
        {
            AutoFlush = true,
            NewLine = "\r\n"
        };

        await writer.WriteLineAsync("220 localhost");
        var greeting = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(greeting, Does.StartWith("EHLO "));
        await writer.WriteLineAsync("250 localhost");

        var mailFrom = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(mailFrom, Does.StartWith("MAIL FROM:"));
        await writer.WriteLineAsync("550 sender rejected");
    }

    private static async Task<bool> RequireStartTlsAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, true)
        {
            AutoFlush = true,
            NewLine = "\r\n"
        };

        await writer.WriteLineAsync("220 localhost");
        var greeting = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(greeting, Does.StartWith("EHLO "));
        await writer.WriteLineAsync("250-localhost");
        await writer.WriteLineAsync("250-STARTTLS");
        await writer.WriteLineAsync("250 SIZE 100000");

        var startTls = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        if (!string.Equals(startTls, "STARTTLS", StringComparison.OrdinalIgnoreCase))
            return false;

        await writer.WriteLineAsync("220 Begin TLS negotiation");
        return true;
    }

    private static async Task CloseConnectionWithoutGreetingAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
