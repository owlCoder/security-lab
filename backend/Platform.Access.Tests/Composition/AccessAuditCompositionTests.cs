using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Access.Composition;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Access.Infrastructure.Auditing;
using Platform.Access.Infrastructure.Catalog;
using Platform.Access.Infrastructure.Policies;
using Platform.Access.Tests.Support;

namespace Platform.Access.Tests.Composition;

[TestFixture]
public sealed class AccessAuditCompositionTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly ServiceProviderOptions Strict = new() { ValidateScopes = true, ValidateOnBuild = true };

    private sealed class FixedPolicy(bool permitted) : IAccessPolicy
    {
        public string Id => "test.policy";
        public string Version => "v1";

        public ValueTask<bool> EvaluateAsync(AccessRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permitted);
    }

    private static ServiceProvider Build(
        out CollectingLoggerProvider logs,
        ManualTimeProvider? clock = null,
        ILoggerProvider? extraProvider = null)
    {
        var provider = new CollectingLoggerProvider();
        logs = provider;

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
            if (extraProvider is not null)
            {
                logging.AddProvider(extraProvider);
            }
        });

        if (clock is not null)
        {
            services.AddSingleton<TimeProvider>(clock);
        }

        services.AddAccessControl();
        var built = services.BuildServiceProvider(Strict);

        var registry = built.GetRequiredService<InMemoryAccessPolicyRegistry>();
        registry.Register("evaluation.task.process", new FixedPolicy(true));
        registry.Register("account.status.change", new FixedPolicy(true));
        return built;
    }

    private static AccessRequest Request(string action, string subject = "user-1") =>
        new(new SubjectRef(subject, AccountState.Active), action, null, "corr-1");

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_Composition_ResolvesResilientChain()
    {
        using var provider = Build(out _);

        var writer = provider.GetRequiredService<IAccessAuditWriter>();

        Assert.That(writer, Is.TypeOf<ResilientAccessAuditWriter>());
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_Composition_PassesStrictScopeValidation_WithoutExtraRegistrations()
    {
        var services = new ServiceCollection();
        services.AddAccessControl();

        using var provider = services.BuildServiceProvider(Strict);
        using var scope = provider.CreateScope();

        Assert.That(scope.ServiceProvider.GetRequiredService<IAccessDecision>(), Is.Not.Null);
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Composition_Deny_IsLoggedOnce_AsWarning()
    {
        using var provider = Build(out var logs);
        var decision = provider.GetRequiredService<IAccessDecision>();

        var result = await decision.DecideAsync(Request("unknown.action"));

        Assert.That(result.Outcome, Is.EqualTo(AccessOutcome.Deny));
        var entry = logs.Entries.Single(e => e.EventId.Name == "access.denied");
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entry.Message, Does.Contain("denialReason=unknown_action"));
        Assert.That(entry.Message, Does.Contain("action=unknown.action"));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Composition_PrivilegedAllow_IsLogged_NormalAllow_IsNot()
    {
        using var provider = Build(out var logs);
        var decision = provider.GetRequiredService<IAccessDecision>();

        var normal = await decision.DecideAsync(Request("evaluation.task.process"));
        var privileged = await decision.DecideAsync(Request("account.status.change", subject: "admin-1"));

        Assert.That(normal.Outcome, Is.EqualTo(AccessOutcome.Allow));
        Assert.That(privileged.Outcome, Is.EqualTo(AccessOutcome.Allow));
        var entry = logs.Entries.Single(e => e.EventId.Name == "access.allowed.privileged");
        Assert.That(entry.Message, Does.Contain("actor=admin-1"));
        Assert.That(logs.Entries.Count(e => e.EventId.Name is "access.denied" or "access.allowed.privileged"), Is.EqualTo(1));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Composition_RepeatedDenies_AreAggregated_UsingTheRegisteredClock()
    {
        var clock = new ManualTimeProvider(Start);
        using var provider = Build(out var logs, clock);
        var decision = provider.GetRequiredService<IAccessDecision>();

        for (var i = 0; i < 4; i++)
        {
            await decision.DecideAsync(Request("unknown.action"));
        }

        Assert.That(logs.Entries.Count(e => e.EventId.Name == "access.denied"), Is.EqualTo(1));

        clock.Advance(TimeSpan.FromSeconds(10));
        await decision.DecideAsync(Request("unknown.action", subject: "user-2"));

        var denied = logs.Entries.Where(e => e.EventId.Name == "access.denied").ToList();
        Assert.That(denied, Has.Count.EqualTo(3));
        Assert.That(denied.Any(e => e.Message.Contains("count=3")), Is.True, "summary of the 3 held-back repeats");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Composition_BrokenLogging_DoesNotChangeDecisions()
    {
        using var provider = Build(out _, extraProvider: new ThrowingLoggerProvider());
        var decision = provider.GetRequiredService<IAccessDecision>();

        var denied = await decision.DecideAsync(Request("unknown.action"));
        var allowed = await decision.DecideAsync(Request("evaluation.task.process"));

        Assert.That(denied.Outcome, Is.EqualTo(AccessOutcome.Deny));
        Assert.That(allowed.Outcome, Is.EqualTo(AccessOutcome.Allow));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_Composition_AuditWriterAndDecision_AreSingletons()
    {
        using var provider = Build(out _);

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        Assert.That(scopeA.ServiceProvider.GetRequiredService<IAccessAuditWriter>(),
            Is.SameAs(scopeB.ServiceProvider.GetRequiredService<IAccessAuditWriter>()));
        Assert.That(scopeA.ServiceProvider.GetRequiredService<IAccessDecision>(),
            Is.SameAs(scopeB.ServiceProvider.GetRequiredService<IAccessDecision>()));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_Composition_RegistersExactlyOneFlushService()
    {
        using var provider = Build(out _);

        var services = provider.GetServices<IHostedService>().OfType<AccessAuditFlushService>().ToList();

        Assert.That(services, Has.Count.EqualTo(1));
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public async Task ACC05_Composition_Shutdown_WritesCountsHeldBackByTheDecisionPipeline()
    {
        using var provider = Build(out var logs);
        var decision = provider.GetRequiredService<IAccessDecision>();
        var flush = provider.GetServices<IHostedService>().OfType<AccessAuditFlushService>().Single();
        await flush.StartAsync(CancellationToken.None);

        for (var i = 0; i < 4; i++)
        {
            await decision.DecideAsync(Request("unknown.action"));
        }

        Assert.That(logs.Entries.Count(e => e.EventId.Name == "access.denied"), Is.EqualTo(1), "repeats are held back");

        await flush.StopAsync(CancellationToken.None);

        var denied = logs.Entries.Where(e => e.EventId.Name == "access.denied").ToList();
        Assert.That(denied, Has.Count.EqualTo(2));
        Assert.That(denied.Any(e => e.Message.Contains("count=3")), Is.True, "held-back repeats reported at shutdown");
    }

    [Test]
    [Property("RequirementId", "ACC-05")]
    public void ACC05_Composition_IsIdempotentForSharedServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        services.AddAccessControl();

        Assert.That(services.Count(d => d.ServiceType == typeof(TimeProvider)), Is.EqualTo(1),
            "an existing TimeProvider is respected, not replaced");
    }
}
