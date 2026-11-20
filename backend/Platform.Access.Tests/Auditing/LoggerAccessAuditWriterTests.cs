using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Auditing;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public sealed class LoggerAccessAuditWriterTests
{
    private sealed class RecordingLogger : ILogger<LoggerAccessAuditWriter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static AccessRequest Request(string action = "course.read", string correlationId = "corr-1") =>
        new(null, action, null, correlationId);

    [Test]
    public async Task Deny_IsLoggedAsWarningWithReasonAndCorrelation()
    {
        var logger = new RecordingLogger();

        await new LoggerAccessAuditWriter(logger).WriteAsync(
            Request(), AccessDecision.Deny(AccessDenialReason.NoPolicy, "corr-1"));

        Assert.That(logger.Entries.Single(), Is.EqualTo((LogLevel.Warning,
            "access.denied action=course.read denialReason=NoPolicy correlationId=corr-1")));
    }

    [Test]
    public async Task Allow_IsNotLogged()
    {
        var logger = new RecordingLogger();

        await new LoggerAccessAuditWriter(logger).WriteAsync(
            Request(), AccessDecision.Allow("course.role", "corr-1"));

        Assert.That(logger.Entries, Is.Empty);
    }

    [Test]
    public async Task UntrustedActionAndCorrelation_AreNotWrittenToTheLog()
    {
        var logger = new RecordingLogger();

        await new LoggerAccessAuditWriter(logger).WriteAsync(
            Request("course.read\r\nforged", "corr\r\nforged"),
            AccessDecision.Deny(AccessDenialReason.UnknownAction, "corr\r\nforged"));

        Assert.That(logger.Entries.Single().Message, Does.Not.Contain("forged"));
    }

    [Test]
    public void AddAccessControl_WithoutCentralAudit_UsesLoggerWriter()
    {
        using var provider = new ServiceCollection().AddAccessControl().BuildServiceProvider();

        Assert.That(provider.GetServices<IAccessAuditWriter>().Single(), Is.TypeOf<LoggerAccessAuditWriter>());
    }
}
