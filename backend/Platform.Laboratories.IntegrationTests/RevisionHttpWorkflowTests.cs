using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Platform.Api.Modules.Laboratories.Application;
using Platform.Api.Modules.Laboratories.Domain;
using Platform.Api.Security.RevisionAuthorization;
using Platform.Api.Services.Identity;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Infrastructure.Persistence;
using Platform.Laboratories.Infrastructure.Composition;
using Platform.LaboratoryPersistence.Composition;
using Platform.Api.Modules.Laboratories.Infrastructure;
using Platform.LaboratoryPersistence.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Platform.Laboratories.IntegrationTests;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class RevisionHttpWorkflowTests
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("rev05_http").WithUsername("rev05_tests").WithPassword(Guid.NewGuid().ToString("N")).Build();
    private WebApplicationFactory<Program> host = null!;
    private Guid author, reviewer, course, lab;
    private HttpClient authorClient = null!, reviewerClient = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        await database.StartAsync();
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = database.GetConnectionString(),
                    ["ConnectionStrings:Laboratories"] = database.GetConnectionString(),
                    ["ConnectionStrings:Authoring"] = database.GetConnectionString()
                }));
            // The minimal host captures some connection strings before the deferred
            // test configuration callback. Rebind real adapters to the isolated DB.
            builder.ConfigureTestServices(services =>
            {
                var connection = database.GetConnectionString();
                services.AddDbContext<PlatformIdentityDbContext>(options => options.UseNpgsql(connection,
                    postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));
                services.AddLaboratoriesPersistence(connection);
                services.AddRevisionSecurityPersistence(connection);
                services.AddAuditPersistence(connection);
                services.AddLaboratoryPersistence(new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["ConnectionStrings:Authoring"] = connection }).Build());
                services.AddScoped<ILaboratoryRepository, PostgresLaboratoryRepository>();
            });
        });
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LaboratoryDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>().Database.MigrateAsync();
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(database.GetConnectionString());
    }

    [SetUp]
    public async Task Seed()
    {
        author = Guid.NewGuid(); reviewer = Guid.NewGuid(); course = Guid.NewGuid();
        using var scope = host.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        identity.Accounts.AddRange(new UserAccount(author, $"{author:N}@example.test"),
            new UserAccount(reviewer, $"{reviewer:N}@example.test"));
        var authorToken = AddSession(identity, author);
        var reviewerToken = AddSession(identity, reviewer);
        await identity.SaveChangesAsync();
        authorClient = Client(authorToken); reviewerClient = Client(reviewerToken);

        var laboratory = new Laboratory(course, "HTTP revision", author);
        await scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>().AddAsync(laboratory);
        lab = laboratory.Id;
        var revisions = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        revisions.CoursePublishPolicies.Add(((CoursePublishPolicy.CreationResult.Success)
            CoursePublishPolicy.Create(course, true)).Value);
        await revisions.SaveChangesAsync();
        var security = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
        foreach (var user in new[] { author, reviewer })
            foreach (var action in new[] { RevisionActions.Create, RevisionActions.Read, RevisionActions.Mine,
                         RevisionActions.PendingReviews, RevisionActions.Submit, RevisionActions.Approve, RevisionActions.Return })
                security.Permissions.Add(new RevisionCoursePermission
                    { UserId = user, CourseId = course, Action = action, IsActive = true });
        await security.SaveChangesAsync();
    }

    [TearDown]
    public void CloseClients() { authorClient.Dispose(); reviewerClient.Dispose(); }

    [OneTimeTearDown]
    public async Task Stop()
    {
        if (host is not null) await host.DisposeAsync();
        await database.DisposeAsync();
    }

    [Test]
    public async Task AuthorizedHttpFlow_CreatesReadsSubmitsReturnsAndPublishes_WithDurableAudit()
    {
        var (id, hash) = await CreateDraft();
        using (var read = await authorClient.GetAsync($"/api/revisions/{id}"))
            Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using (var mine = await authorClient.GetAsync("/api/revisions/mine"))
            Assert.That((await mine.Content.ReadAsStringAsync()), Does.Contain(id.ToString()));
        await Command(authorClient, id, hash, "submit", 1, "submit-1", HttpStatusCode.OK);
        using (var pending = await reviewerClient.GetAsync($"/api/revisions/pending-reviews?courseId={course}"))
            Assert.That((await pending.Content.ReadAsStringAsync()), Does.Contain(id.ToString()));
        await Command(reviewerClient, id, hash, "return", 2, "return-1", HttpStatusCode.OK, "Revise explanation");
        await Command(authorClient, id, hash, "submit", 3, "submit-2", HttpStatusCode.OK);
        await Command(reviewerClient, id, hash, "approve", 4, "approve-1", HttpStatusCode.OK);
        // Same key is a retry, not a second publication or second audit event.
        await Command(reviewerClient, id, hash, "approve", 4, "approve-1", HttpStatusCode.OK);

        using var freshScope = host.Services.CreateScope();
        var db = freshScope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        Assert.That((await db.LabRevisions.SingleAsync(value => value.Id == id)).Status.ToString(), Is.EqualTo("Published"));
        Assert.That(await db.ReviewOperations.CountAsync(value => value.RevisionId == id), Is.EqualTo(4));
        var events = await AuditEvents(id);
        Assert.That(events.Select(value => value.Type), Is.EquivalentTo(new[]
            { "revision.submitted", "revision.returned", "revision.submitted", "revision.approved" }));
        var approval = events.Single(value => value.Type == AuditEventTypes.RevisionApproved);
        Assert.Multiple(() =>
        {
            Assert.That(approval.Actor, Is.EqualTo(reviewer));
            Assert.That(approval.Details.GetProperty("authorId").GetString(), Is.EqualTo(author.ToString()));
            Assert.That(approval.Details.GetProperty("revisionNumber").GetString(), Is.EqualTo("1"));
            Assert.That(approval.Details.GetProperty("laboratoryId").GetString(), Is.EqualTo(lab.ToString()));
            Assert.That(approval.Correlation, Is.Not.Empty);
        });
    }

    [TestCase(true, HttpStatusCode.Forbidden)]
    [TestCase(false, HttpStatusCode.OK)]
    public async Task SelfApprovalHttp_FollowsPersistedCoursePolicy(bool independent, HttpStatusCode expected)
    {
        await using (var connection = new NpgsqlConnection(database.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var sql = new NpgsqlCommand("UPDATE laboratories.\"CoursePublishPolicies\" SET \"RequiresIndependentApproval\" = @independent WHERE \"CourseId\" = @course", connection);
            sql.Parameters.AddWithValue("independent", independent); sql.Parameters.AddWithValue("course", course);
            await sql.ExecuteNonQueryAsync();
        }
        var (id, hash) = await CreateDraft();
        await Command(authorClient, id, hash, "submit", 1, "submit", HttpStatusCode.OK);
        await Command(authorClient, id, hash, "approve", 2, "approve", expected);
        var events = await AuditEvents(id);
        Assert.That(events.Count(value => value.Type == AuditEventTypes.RevisionApproved), Is.EqualTo(independent ? 0 : 1));
    }

    [Test]
    public async Task RevokedCourseGrant_DeniesNextHttpRead_AndPersistsDenial()
    {
        var (id, _) = await CreateDraft();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
            var grant = await db.Permissions.SingleAsync(value => value.UserId == reviewer && value.CourseId == course && value.Action == RevisionActions.Read);
            grant.IsActive = false; await db.SaveChangesAsync();
        }
        using var denied = await reviewerClient.GetAsync($"/api/revisions/{id}");
        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var correlation = denied.Headers.GetValues("X-Correlation-ID").Single();
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var sql = new NpgsqlCommand("SELECT details->>'denialReason' FROM audit.audit_events WHERE event_type = 'access.denied' AND actor_id = @actor AND correlation_id = @correlation", connection);
        sql.Parameters.AddWithValue("actor", reviewer); sql.Parameters.AddWithValue("correlation", correlation);
        await using var audit = await sql.ExecuteReaderAsync();
        Assert.That(await audit.ReadAsync(), Is.True, "The denial must be persisted.");
        Assert.That(audit.GetString(0), Is.EqualTo("policy_denied"));
        Assert.That(await audit.ReadAsync(), Is.False, "The decision must produce exactly one denial event.");
    }

    [TestCase(null)]
    [TestCase("revoked-session")]
    public async Task DeniedRevisionEntry_PersistsAnonymousAuditWithServerCorrelation(string? token)
    {
        using var client = token is null
            ? host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") })
            : Client(token);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "client-supplied");

        using var response = await client.GetAsync("/api/revisions/mine");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.That(correlation, Is.Not.EqualTo("client-supplied"));

        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var sql = new NpgsqlCommand("""
            SELECT actor_kind, actor_id, outcome, details->>'action', details->>'denialReason'
            FROM audit.audit_events
            WHERE event_type = 'access.denied' AND correlation_id = @correlation
            """, connection);
        sql.Parameters.AddWithValue("correlation", correlation);
        await using var audit = await sql.ExecuteReaderAsync();
        Assert.That(await audit.ReadAsync(), Is.True, "The entry denial must reach the persistent Audit store.");
        Assert.Multiple(() =>
        {
            Assert.That(audit.GetString(0), Is.EqualTo("Anonymous"));
            Assert.That(audit.IsDBNull(1), Is.True);
            Assert.That(audit.GetString(2), Is.EqualTo("Denied"));
            Assert.That(audit.GetString(3), Is.EqualTo("revision.entry"));
            Assert.That(audit.GetString(4), Is.EqualTo("unauthenticated"));
        });
        Assert.That(await audit.ReadAsync(), Is.False, "The entry denial must be recorded exactly once.");
    }

    [Test]
    public async Task FailedAudit_RollsBackHttpSubmitAndReceipt()
    {
        var (id, hash) = await CreateDraft();
        await ExecuteSql("ALTER TABLE audit.audit_events RENAME TO audit_events_unavailable");
        try
        {
            await Command(authorClient, id, hash, "submit", 1, "submit", HttpStatusCode.ServiceUnavailable);
        }
        finally
        {
            await ExecuteSql("ALTER TABLE audit.audit_events_unavailable RENAME TO audit_events");
        }
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        var revision = await db.LabRevisions.SingleAsync(value => value.Id == id);
        Assert.That(revision.Status.ToString(), Is.EqualTo("Draft"));
        Assert.That(revision.RowVersion, Is.EqualTo(1));
        Assert.That(await db.ReviewOperations.AnyAsync(value => value.RevisionId == id), Is.False);
        Assert.That(await AuditEvents(id), Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExpiredOrOtherCourseGrant_DoesNotAuthorizeHttpRead(bool expired)
    {
        var (id, _) = await CreateDraft();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
            var grant = await db.Permissions.SingleAsync(value => value.UserId == reviewer &&
                value.CourseId == course && value.Action == RevisionActions.Read);
            if (expired) grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            else
            {
                db.Permissions.Remove(grant);
                db.Permissions.Add(new RevisionCoursePermission { UserId = reviewer,
                    CourseId = Guid.NewGuid(), Action = RevisionActions.Read, IsActive = true });
            }
            await db.SaveChangesAsync();
        }
        using var response = await reviewerClient.GetAsync($"/api/revisions/{id}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("HTTP Lab"));
    }

    [Test]
    public async Task SecurityMigration_IsRepeatableAndPreservesExistingGrants()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
        await db.Database.MigrateAsync();
        Assert.That(await db.Permissions.CountAsync(value => value.UserId == author && value.CourseId == course), Is.EqualTo(7));
    }

    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Archived)]
    public async Task InactiveAccount_CannotUsePreviouslyIssuedSession(AccountStatus status)
    {
        var (id, _) = await CreateDraft();
        using (var scope = host.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
            var account = await identity.Accounts.SingleAsync(value => value.Id == reviewer);
            account.Status = status;
            await identity.SaveChangesAsync();
        }
        // Keep the session row unrevoked: current account state must independently deny access.
        using var response = await reviewerClient.GetAsync($"/api/revisions/{id}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task ExecuteSql(string sql)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(Guid Id, string Hash)> CreateDraft()
    {
        using var response = await authorClient.PostAsJsonAsync("/api/revisions/", new
        {
            laboratoryId = lab, courseId = course,
            definition = new { type = "LogAnalysis", title = "HTTP Lab", goal = "Find failed logins", question = "Which event failed?",
                events = new[] { new { id = "event-1", timestamp = DateTimeOffset.UnixEpoch, message = "Failed login" } },
                evaluator = new { kind = "event-set", version = 1, expectedEventIds = new[] { "event-1" } } }
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("contentHash").GetString()!);
    }

    private static async Task Command(HttpClient client, Guid id, string hash, string action, int version,
        string key, HttpStatusCode expected, string? comment = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/revisions/{id}/{action}")
            { Content = JsonContent.Create(new { rowVersion = version.ToString(), contentHash = hash, comment }) };
        request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
    }

    private HttpClient Client(string token)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookie.Name}={token}");
        return client;
    }

    private static string AddSession(PlatformIdentityDbContext db, Guid user)
    {
        var token = Guid.NewGuid().ToString("N");
        db.Sessions.Add(new Session(Guid.NewGuid(), user, SHA256.HashData(Encoding.UTF8.GetBytes(token)),
            Guid.NewGuid(), AuthLevel.Active, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
        return token;
    }

    private async Task<List<(string Type, Guid Actor, JsonElement Details, string Correlation)>> AuditEvents(Guid id)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT event_type, actor_id, details::text, correlation_id FROM audit.audit_events WHERE resource_type = 'Revision' AND resource_id = @id", connection);
        command.Parameters.AddWithValue("id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        var events = new List<(string, Guid, JsonElement, string)>();
        while (await reader.ReadAsync())
            events.Add((reader.GetString(0), reader.GetGuid(1), JsonSerializer.Deserialize<JsonElement>(reader.GetString(2)), reader.GetString(3)));
        return events;
    }

}
