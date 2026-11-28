using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Team21.Acc.Api.Services;
using Team21.Acc.Core.Groups;
using Team21.Acc.Core.Groups.Visibility;
using Team21.Acc.Core.Groups.Visibility.Interfaces;
using Team21.Acc.Core.Interfaces;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Infrastructure.Groups.Defaults;
using Team21.Acc.Tests.Support;

namespace Team21.Acc.Tests.Infrastructure;

[NonParallelizable]
public class LoggingAuditTrailTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 30, 0, TimeSpan.Zero);

    [TearDown]
    public void ClearHttpContext() => new HttpContextAccessor().HttpContext = null;

    [TestCase(AuditOutcome.Success)]
    [TestCase(AuditOutcome.Denied)]
    public async Task Record_PreservesStructuredActionResourceOutcomeAndTrustedContext(AuditOutcome outcome)
    {
        var actorId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var logger = new RecordingLogger<LoggingAuditTrail>();
        IAuditTrail audit = new LoggingAuditTrail(
            new CurrentActor(actorId), new FixedCorrelation("server-request-42"), new FixedTime(Now), logger);

        await audit.RecordAsync("group.member.add", resourceId, outcome);

        Assert.That(logger.Entries, Has.Count.EqualTo(1));
        var entry = logger.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Information));
            Assert.That(entry.Exception, Is.Null);
            Assert.That(entry.Fields.Keys, Is.EquivalentTo(new[]
            {
                "OccurredAt", "ActorId", "Action", "ResourceId", "Outcome", "CorrelationId", "{OriginalFormat}"
            }));
            Assert.That(entry.Fields["OccurredAt"], Is.EqualTo(Now.UtcDateTime));
            Assert.That(((DateTime)entry.Fields["OccurredAt"]!).Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(entry.Fields["ActorId"], Is.EqualTo(actorId));
            Assert.That(entry.Fields["Action"], Is.EqualTo("group.member.add"));
            Assert.That(entry.Fields["ResourceId"], Is.EqualTo(resourceId));
            Assert.That(entry.Fields["Outcome"], Is.EqualTo(outcome));
            Assert.That(entry.Fields["CorrelationId"], Is.EqualTo("server-request-42"));
        });
    }

    [Test]
    public async Task BothWriters_PreserveServerCorrelationWithoutCopyingHttpSecrets()
    {
        const string serverId = "trusted-server-request";
        string[] sensitiveMarkers =
        [
            "client-controlled-correlation", "synthetic-bearer-token", "synthetic-session-cookie",
            "synthetic-password", "synthetic-student-answer", "synthetic-evaluation-key"
        ];
        var http = new DefaultHttpContext { TraceIdentifier = serverId };
        http.Request.Headers["X-Correlation-ID"] = sensitiveMarkers[0];
        http.Request.Headers.Authorization = "Bearer " + sensitiveMarkers[1];
        http.Request.Headers.Cookie = "session=" + sensitiveMarkers[2];
        using var body = new MemoryStream(Encoding.UTF8.GetBytes(
            $$"""{"password":"{{sensitiveMarkers[3]}}","answer":"{{sensitiveMarkers[4]}}","key":"{{sensitiveMarkers[5]}}"}"""));
        http.Request.Body = body;

        var correlation = new HttpCorrelationContext(new HttpContextAccessor { HttpContext = http });
        var actorId = Guid.NewGuid();
        var auditLogger = new RecordingLogger<LoggingAuditTrail>();
        var visibilityLogger = new RecordingLogger<LoggingVisibilityAuditWriter>();
        var audit = new LoggingAuditTrail(new CurrentActor(actorId), correlation, new FixedTime(Now), auditLogger);
        IVisibilityAuditWriter visibilityAudit = new LoggingVisibilityAuditWriter(visibilityLogger);

        await audit.RecordAsync("group.member.add", Guid.NewGuid(), AuditOutcome.Success);
        await visibilityAudit.WriteDenialAsync(new VisibilityDenial(
            actorId, Guid.NewGuid(), new MaterialRef(MaterialType.Assignment, Guid.NewGuid()),
            MaterialAction.Read, Now.UtcDateTime, DenialReason.NotInGroup, correlation.CorrelationId));

        Assert.That(auditLogger.Entries, Has.Count.EqualTo(1));
        Assert.That(visibilityLogger.Entries, Has.Count.EqualTo(1));
        foreach (var entry in new[] { auditLogger.Entries.Single(), visibilityLogger.Entries.Single() })
        {
            Assert.That(entry.Fields["CorrelationId"], Is.EqualTo(serverId));
            Assert.That(entry.Fields["ActorId"], Is.EqualTo(actorId));
            foreach (var marker in sensitiveMarkers)
            {
                Assert.That(entry.Message, Does.Not.Contain(marker));
                foreach (var field in entry.Fields)
                {
                    Assert.That(field.Key, Does.Not.Contain(marker));
                    Assert.That(field.Value?.ToString() ?? string.Empty, Does.Not.Contain(marker));
                }
            }
        }
    }

    private sealed class CurrentActor(Guid? userId) : ICurrentActor
    {
        public Guid? UserId { get; } = userId;
        public bool IsAuthenticated => UserId is not null;
    }

    private sealed class FixedCorrelation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; } = correlationId;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
