using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Security.AccessAudit;
using Platform.Audit.Contracts;

namespace Platform.Access.Tests.Auditing;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public sealed class CentralAccessAuditWriterTests
{
    private static readonly AuditEventDraft Draft = new(AuditEventTypes.AccessDenied, AuditOutcome.Denied);
    private static readonly AuditContext Context = new(AuditActor.Anonymous, "corr-1");

    private sealed class RecordingTrustedWriter(AuditWriteResult result) : ITrustedAuditWriter
    {
        public List<(AuditEventDraft Draft, AuditContext Context)> Writes { get; } = [];

        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft, AuditContext context, CancellationToken cancellationToken = default)
        {
            Writes.Add((draft, context));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CancellationAwareTrustedWriter : ITrustedAuditWriter
    {
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }

            return AuditWriteResult.Recorded(Guid.NewGuid());
        }
    }

    private sealed class FixedMapper : IAccessDeniedAuditMapper
    {
        public AuditEventDraft CreateDraft(AccessRequest request, AccessDecision decision) => Draft;

        public AuditContext CreateContext(AccessRequest request, AccessDecision decision) => Context;
    }

    private static (CentralAccessAuditWriter Writer, RecordingTrustedWriter Central) Create(AuditWriteResult? result = null)
    {
        var central = new RecordingTrustedWriter(result ?? AuditWriteResult.Recorded(Guid.NewGuid()));
        var provider = new ServiceCollection()
            .AddScoped<ITrustedAuditWriter>(_ => central)
            .BuildServiceProvider();

        return (new CentralAccessAuditWriter(
            new FixedMapper(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CentralAccessAuditWriter>.Instance), central);
    }

    private static AccessRequest Request(string action = "course.read") =>
        new(null, action, null, "corr-1");

    private static AccessDecision Denied() =>
        new(AccessOutcome.Deny, AccessDenialReason.PolicyDenied, "course.role", "v1", "corr-1");

    [Test]
    public async Task Deny_IsWrittenToCentralAuditWithMappedDraftAndContext()
    {
        var (writer, central) = Create();

        await writer.WriteAsync(Request(), Denied());

        Assert.That(central.Writes.Single(), Is.EqualTo((Draft, Context)));
    }

    [Test]
    public async Task CancellationToken_ReachesTrustedAuditWriteOperation()
    {
        var central = new CancellationAwareTrustedWriter();
        await using var provider = new ServiceCollection()
            .AddScoped<ITrustedAuditWriter>(_ => central)
            .BuildServiceProvider();
        var writer = new CentralAccessAuditWriter(
            new FixedMapper(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CentralAccessAuditWriter>.Instance);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var write = writer.WriteAsync(Request(), Denied(), timeout.Token).AsTask();

        await central.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.CatchAsync<OperationCanceledException>(async () => await write);
        await central.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task Allow_IsNotWritten()
    {
        var (writer, central) = Create();

        await writer.WriteAsync(Request(), AccessDecision.Allow("course.role", "corr-1", "v1"));

        Assert.That(central.Writes, Is.Empty);
    }

    [Test]
    public void RejectedWrite_DoesNotThrow()
    {
        var (writer, central) = Create(AuditWriteResult.Rejected(AuditRejectionCode.InvalidContext));

        Assert.DoesNotThrowAsync(async () => await writer.WriteAsync(Request(), Denied()));
        Assert.That(central.Writes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AddCentralAccessAudit_SendsEngineDenialsToCentralAudit()
    {
        var central = new RecordingTrustedWriter(AuditWriteResult.Recorded(Guid.NewGuid()));
        var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<ITrustedAuditWriter>(_ => central)
            .AddCentralAccessAudit()
            .AddAccessControl();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.That(provider.GetServices<IAccessAuditWriter>().Single(), Is.TypeOf<CentralAccessAuditWriter>());

        await provider.GetRequiredService<IAccessDecision>().DecideAsync(Request("unknown.action"));

        Assert.That(central.Writes.Single().Draft.Details!["denialReason"], Is.EqualTo("unknown_action"));
    }

    [Test]
    public void AddCentralAccessAudit_AfterAccessControl_Throws()
    {
        var services = new ServiceCollection().AddAccessControl();

        Assert.Throws<InvalidOperationException>(() => services.AddCentralAccessAudit());
    }
}
