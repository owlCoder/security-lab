using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NUnit.Framework;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using static Platform.Audit.Tests.AuditDraftExamples;

namespace Platform.Audit.Tests;

[TestFixture]
[Category("AuditRuntime")]
public sealed class AuditHostPostgresTests
{
    private string connectionString = null!;
    private AuditHostFactory? factory;
    private HttpClient client = null!;

    [OneTimeSetUp]
    public void StartHost()
    {
        var configured = Environment.GetEnvironmentVariable("AUDIT_TEST_RUNTIME_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Assert.Ignore("Set AUDIT_TEST_RUNTIME_CONNECTION_STRING to run the Audit host against PostgreSQL.");
            return;
        }

        connectionString = new NpgsqlConnectionStringBuilder(configured) { Pooling = false }.ConnectionString;
        factory = new AuditHostFactory(connectionString);
        client = factory.CreateClient();
    }

    [OneTimeTearDown]
    public void StopHost()
    {
        client?.Dispose();
        factory?.Dispose();
    }

    [Test]
    public async Task Write_PersistsTrustedContextThroughHostAndRestrictedStore()
    {
        var draft = StatusChange() with { EventId = Guid.NewGuid() };
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        using var response = await client.PostAsJsonAsync(
            $"{AuditHostFactory.ProbePath}?actorId={TargetAccountId}&correlationId=client-supplied", draft);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await using var context = OpenContext();
        var stored = await new PostgresAuditEventStore(context).FindAsync(draft.EventId.Value);
        Assert.That(stored, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored!.ActorId, Is.EqualTo(UserId));
            Assert.That(stored.ActorKind, Is.EqualTo(AuditActorKind.User));
            Assert.That(stored.CorrelationId, Is.EqualTo(CorrelationId));
            Assert.That(stored.OccurredAt, Is.InRange(before, DateTimeOffset.UtcNow));
            Assert.That(stored.OccurredAt.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(stored.EventType, Is.EqualTo(draft.EventType));
            Assert.That(stored.Outcome, Is.EqualTo(draft.Outcome));
            Assert.That(stored.ResourceId, Is.EqualTo(TargetAccountId.ToString()));
        }
    }

    [Test]
    public async Task Write_RejectsForbiddenDetailWithoutEchoingOrPersistingIt()
    {
        const string marker = "synthetic-sensitive-marker";
        var draft = StatusChange(details: Details(("password", marker))) with { EventId = Guid.NewGuid() };
        using var response = await client.PostAsJsonAsync(AuditHostFactory.ProbePath, draft);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain(marker));

        await using var context = OpenContext();
        Assert.That(await new PostgresAuditEventStore(context).FindAsync(draft.EventId.Value), Is.Null);
    }

    private AuditDbContext OpenContext() =>
        new(new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(connectionString).Options);
}
