using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NUnit.Framework;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
[Category("AuditRuntime")]
[Category("S17")]
public sealed class AuditRegressionPostgresTests
{
    private string connectionString = null!;

    [OneTimeSetUp]
    public void RequireDatabase()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_RUNTIME_CONNECTION_STRING to run S-17 PostgreSQL regressions.");
            return;
        }
        connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
    }

    [Test]
    public async Task ConcurrentScopes_PersistOnlyTheirOwnActorAndCorrelation()
    {
        await using var provider = BuildProvider();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var contexts = Enumerable.Range(0, 8)
            .Select(index => new AuditContext(AuditActor.ForUser(Guid.NewGuid()), $"s17-parallel-{index}"))
            .ToArray();
        var ids = await Task.WhenAll(contexts.Select(async trusted =>
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TestAuditContext>().Current = trusted;
            var writer = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
            Assert.That(scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>(), Is.SameAs(writer));
            if (Interlocked.Increment(ref started) == contexts.Length)
                ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var result = await writer.WriteAsync(StatusChange());
            Assert.That(result.IsRecorded, Is.True);
            return result.EventId!.Value;
        }));

        await using var database = OpenContext();
        var rows = await database.Events.AsNoTracking().Where(row => ids.Contains(row.Id)).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(contexts.Length));
        for (var index = 0; index < ids.Length; index++)
        {
            var row = rows.Single(record => record.Id == ids[index]);
            Assert.That(row.ActorId, Is.EqualTo(contexts[index].Actor.Id));
            Assert.That(row.CorrelationId, Is.EqualTo(contexts[index].CorrelationId));
        }
    }

    [Test]
    public async Task CancelBeforeWrite_DoesNotPersistAnEvent()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>();
        var draft = StatusChange() with { EventId = Guid.NewGuid() };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await writer.WriteAsync(draft, UserContext, cancellation.Token));

        await using var database = OpenContext();
        Assert.That(await database.Events.AnyAsync(row => row.Id == draft.EventId), Is.False);
    }

    [Test]
    public async Task RetryAfterRecorded_InNewScope_PreservesOneOriginalEvent()
    {
        await using var provider = BuildProvider();
        var draft = StatusChange() with { EventId = Guid.NewGuid() };
        await using (var first = provider.CreateAsyncScope())
        {
            var result = await first.ServiceProvider.GetRequiredService<ITrustedAuditWriter>()
                .WriteAsync(draft, UserContext);
            Assert.That(result.IsRecorded, Is.True);
        }
        await using var database = OpenContext();
        var original = await database.Events.AsNoTracking().SingleAsync(row => row.Id == draft.EventId);

        await using (var retry = provider.CreateAsyncScope())
        {
            var result = await retry.ServiceProvider.GetRequiredService<ITrustedAuditWriter>()
                .WriteAsync(draft, UserContext);
            Assert.That(result, Is.EqualTo(AuditWriteResult.Recorded(draft.EventId!.Value)));
        }

        await using var fresh = OpenContext();
        var rows = await fresh.Events.AsNoTracking().Where(row => row.Id == draft.EventId).ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows.Single().OccurredAt, Is.EqualTo(original.OccurredAt));
    }

    [Test]
    public async Task RetryWithDifferentActorOrCorrelation_IsRejectedWithoutChangingHistory()
    {
        await using var provider = BuildProvider();
        var draft = StatusChange() with { EventId = Guid.NewGuid() };
        await using (var first = provider.CreateAsyncScope())
            Assert.That((await first.ServiceProvider.GetRequiredService<ITrustedAuditWriter>()
                .WriteAsync(draft, UserContext)).IsRecorded, Is.True);

        foreach (var changed in new[]
        {
            UserContext with { Actor = AuditActor.ForUser(Guid.NewGuid()) },
            UserContext with { CorrelationId = "s17-different-correlation" }
        })
        {
            await using var retry = provider.CreateAsyncScope();
            var result = await retry.ServiceProvider.GetRequiredService<ITrustedAuditWriter>()
                .WriteAsync(draft, changed);
            Assert.That(result.RejectionCode, Is.EqualTo(AuditRejectionCode.EventIdConflict));
            Assert.That(result.IsRecorded, Is.False);
        }

        await using var database = OpenContext();
        var row = await database.Events.AsNoTracking().SingleAsync(record => record.Id == draft.EventId);
        Assert.That(row.ActorId, Is.EqualTo(UserId));
        Assert.That(row.CorrelationId, Is.EqualTo(CorrelationId));
    }

    [Test]
    public async Task MissingAmbientContext_DoesNotInventAnActorOrPersistTheDraft()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var draft = StatusChange() with { EventId = Guid.NewGuid() };
        var result = await scope.ServiceProvider.GetRequiredService<IAuditWriter>().WriteAsync(draft);

        Assert.That(result.RejectionCode, Is.EqualTo(AuditRejectionCode.InvalidContext));
        await using var database = OpenContext();
        Assert.That(await database.Events.AnyAsync(row => row.Id == draft.EventId), Is.False);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddAuditPersistence(connectionString);
        services.RemoveAll<IAuditContextProvider>();
        services.AddScoped<TestAuditContext>();
        services.AddScoped<IAuditContextProvider>(provider => provider.GetRequiredService<TestAuditContext>());
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
    }

    private AuditDbContext OpenContext() =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString).Options);

    private sealed class TestAuditContext : IAuditContextProvider
    {
        public AuditContext? Current { get; set; }
        public AuditContext? GetCurrentContext() => Current;
    }
}
