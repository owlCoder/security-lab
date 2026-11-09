using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
public sealed class FilteringAccessAuditWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] PrivilegedActions =
    [
        "lab.revision.publish", "attempt.review", "account.status.change",
        "system.config.manage", "results.sensitive.read", "course.create",
        "course.archive", "group.create", "group.member.add", "group.member.remove",
    ];

    private RecordingAuditSink _sink = null!;
    private FilteringAccessAuditWriter _writer = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new AccessAuditOptions();
        _sink = new RecordingAuditSink();
        _writer = new FilteringAccessAuditWriter(
            new AccessAuditRecordFactory(new ManualTimeProvider(Now), options), _sink, options);
    }

    private static AccessRequest Request(string action, SubjectRef? subject = null) =>
        new(subject ?? new SubjectRef("user-1", AccountState.Active), action, null, "corr-1");

    private static AccessDecision Allowed() => AccessDecision.Allow("some.policy", "corr-1", "v1");

    private static IEnumerable<AccessDenialReason> AllDenialReasons() =>
        Enum.GetValues<AccessDenialReason>().Where(r => r != AccessDenialReason.None);

    [TestCaseSource(nameof(AllDenialReasons))]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_EveryDeny_IsAudited(AccessDenialReason reason)
    {
        await _writer.WriteAsync(Request("lab.read"), AccessDecision.Deny(reason, "corr-1"));

        Assert.That(_sink.Records, Has.Count.EqualTo(1));
        Assert.That(_sink.Records[0].EventType, Is.EqualTo(AccessAuditRecord.DeniedEventType));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_DenyOfPrivilegedAction_IsAudited_AsDenied()
    {
        await _writer.WriteAsync(
            Request("account.status.change"),
            AccessDecision.Deny(AccessDenialReason.PolicyError, "corr-1", "v1"));

        Assert.That(_sink.Records.Single().EventType, Is.EqualTo(AccessAuditRecord.DeniedEventType));
    }

    [TestCaseSource(nameof(PrivilegedActions))]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_PrivilegedAllow_IsAudited(string action)
    {
        await _writer.WriteAsync(Request(action), Allowed());

        Assert.That(_sink.Records, Has.Count.EqualTo(1));
        Assert.That(_sink.Records[0].EventType, Is.EqualTo(AccessAuditRecord.PrivilegedAllowedEventType));
        Assert.That(_sink.Records[0].Action, Is.EqualTo(action));
    }

    [TestCase("lab.read")]
    [TestCase("attempt.read.course")]
    [TestCase("lab.revision.read")]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_NonPrivilegedAllow_NotAudited(string action)
    {
        await _writer.WriteAsync(Request(action), Allowed());

        Assert.That(_sink.Records, Is.Empty);
    }

    [TestCase("Account.Status.Change")]
    [TestCase("account.status.change ")]
    [TestCase("account.status.change\n")]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_PrivilegedActionIsComparedExactly_NoNormalization(string action)
    {
        await _writer.WriteAsync(Request(action), Allowed());

        Assert.That(_sink.Records, Is.Empty);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_AnonymousDeny_IsAudited()
    {
        var request = new AccessRequest(null, "lab.read", null, "corr-1");

        await _writer.WriteAsync(request, AccessDecision.Deny(AccessDenialReason.Unauthenticated, "corr-1"));

        Assert.That(_sink.Records.Single().Actor, Is.EqualTo(AccessAuditRecord.Anonymous));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_NullArguments_AreRejected()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _writer.WriteAsync(null!, AccessDecision.Deny(AccessDenialReason.NoPolicy, "c")));
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _writer.WriteAsync(Request("lab.read"), null!));
    }
}
