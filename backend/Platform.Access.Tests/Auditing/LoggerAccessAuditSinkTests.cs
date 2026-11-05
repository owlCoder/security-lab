using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
public sealed class LoggerAccessAuditSinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private CollectingLogger<LoggerAccessAuditSink> _logger = null!;
    private LoggerAccessAuditSink _sink = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new CollectingLogger<LoggerAccessAuditSink>();
        _sink = new LoggerAccessAuditSink(_logger);
    }

    private static AccessAuditRecord Denied(int count = 1) => new(
        Now, AccessAuditRecord.DeniedEventType, "user-1", "lab.read", "attempt:42", "deny",
        "corr-1", "no_policy", null, "v0", count);

    private static AccessAuditRecord PrivilegedAllowed() => new(
        Now, AccessAuditRecord.PrivilegedAllowedEventType, "admin-1", "account.status.change", "-", "allow",
        "corr-2", null, "identity.account.status.change.admin-only", "v1");

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Deny_IsLoggedAsWarning_WithDeniedEventName()
    {
        await _sink.WriteAsync(Denied());

        var entry = _logger.Entries.Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entry.EventId.Name, Is.EqualTo("access.denied"));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_PrivilegedAllow_IsLoggedAsInformation_WithAllowedEventName()
    {
        await _sink.WriteAsync(PrivilegedAllowed());

        var entry = _logger.Entries.Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Information));
        Assert.That(entry.EventId.Name, Is.EqualTo("access.allowed.privileged"));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Deny_EventId_DiffersFromAllowEventId()
    {
        await _sink.WriteAsync(Denied());
        await _sink.WriteAsync(PrivilegedAllowed());

        Assert.That(_logger.Entries[0].EventId.Id, Is.Not.EqualTo(_logger.Entries[1].EventId.Id));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_LoggedValues_CarryTheRecordFields()
    {
        await _sink.WriteAsync(Denied(count: 7));

        var state = _logger.Entries.Single().State;
        Assert.Multiple(() =>
        {
            Assert.That(state["Actor"], Is.EqualTo("user-1"));
            Assert.That(state["Action"], Is.EqualTo("lab.read"));
            Assert.That(state["Target"], Is.EqualTo("attempt:42"));
            Assert.That(state["Outcome"], Is.EqualTo("deny"));
            Assert.That(state["CorrelationId"], Is.EqualTo("corr-1"));
            Assert.That(state["DenialReason"], Is.EqualTo("no_policy"));
            Assert.That(state["PolicyVersion"], Is.EqualTo("v0"));
            Assert.That(state["Count"], Is.EqualTo(7));
        });
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    [Property("RequirementId", "AUD-03")]
    public async Task ACC05_LoggedValues_AreExactlyTheWhitelist()
    {
        await _sink.WriteAsync(Denied());
        await _sink.WriteAsync(PrivilegedAllowed());

        string[] whitelist =
        [
            "Time", "Actor", "Action", "Target", "Outcome", "CorrelationId",
            "DenialReason", "PolicyId", "PolicyVersion", "Count",
        ];

        foreach (var entry in _logger.Entries)
        {
            var keys = entry.State.Keys.Where(k => k != "{OriginalFormat}");
            Assert.That(keys, Is.EquivalentTo(whitelist));
        }
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    [Property("RequirementId", "AUD-03")]
    public async Task ACC05_AuditRecord_ContainsNoSecrets()
    {
        await _sink.WriteAsync(Denied());
        await _sink.WriteAsync(PrivilegedAllowed());

        var secrets = new Regex("password|token|secret|authorization|cookie|bearer", RegexOptions.IgnoreCase);
        foreach (var entry in _logger.Entries)
        {
            Assert.That(secrets.IsMatch(entry.Message), Is.False, entry.Message);
        }
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Message_IsSingleLine()
    {
        await _sink.WriteAsync(Denied());

        Assert.That(_logger.Entries.Single().Message.Any(char.IsControl), Is.False);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_NullRecord_IsRejected()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () => await _sink.WriteAsync(null!));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_UnknownEventType_IsLoggedAsDenied_NeverSilentlyDropped()
    {
        var odd = Denied() with { EventType = "something.else" };

        await _sink.WriteAsync(odd);

        Assert.That(_logger.Entries.Single().EventId.Name, Is.EqualTo("access.denied"));
    }
}
