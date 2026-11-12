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

/// <summary>
/// REV-05 (P): "Autor ne moze sam da odobri reviziju ako je politika to zahtevala" (dvostepeno odobrenje).
///
/// RevisionHttpWorkflowTests vec dokazuje glavni tok i samoodobravanje (403 / 200 po politici). Ovde se
/// proverava ono sto taj test ne pokriva: STANJE posle odbijanja (revizija ostaje InReview, bez odluke,
/// bez objave i bez approve audit zapisa), drugi korak dvostepenog odobrenja, trenutak vazenja politike
/// (u casu odluke, ne u casu predaje), fail-closed kad politika ili pravo nedostaju, i audit odbijanja.
/// Stvarne PostgreSQL sesije, grantovi, politika i audit; bez laznog Allow-a.
/// </summary>
[TestFixture, Category("Integration"), Category("REV-05"), NonParallelizable]
public sealed class RevisionTwoStepApprovalHttpTests
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("rev05_two_step").WithUsername("rev05_tests").WithPassword(Guid.NewGuid().ToString("N")).Build();
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
            // Isti razlog kao u RevisionHttpWorkflowTests: minimalni host cita neke connection string-ove
            // pre odlozene test konfiguracije, pa se stvarni adapteri vezuju za izolovanu bazu.
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

    /// <summary>Svaki test dobija svoj kurs, autora i recenzenta; kurs po defaultu zahteva nezavisno odobrenje.</summary>
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

        var laboratory = new Laboratory(Guid.NewGuid(), "Two-step revision", author);
        await scope.ServiceProvider.GetRequiredService<ILaboratoryRepository>().AddAsync(laboratory);
        lab = laboratory.Id;
        var revisions = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        revisions.CoursePublishPolicies.Add(((CoursePublishPolicy.CreationResult.Success)
            CoursePublishPolicy.Create(course, true)).Value);
        await revisions.SaveChangesAsync();
        var security = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
        // I autor i recenzent imaju SVA prava, ukljucujuci approve: jedino politika nezavisnosti ih razlikuje.
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

    // ------------------------------------------------------------------ prvi korak: autor ne odobrava

    [Test]
    public async Task AuthorSelfApproval_WhenPolicyRequiresIndependence_IsRejected_AndLeavesRevisionInReview()
    {
        var (id, hash) = await CreateDraft();
        await Post(authorClient, id, hash, "submit", 1, "submit-1", HttpStatusCode.OK);

        var denied = await Post(authorClient, id, hash, "approve", 2, "self-approve", HttpStatusCode.Forbidden);

        Assert.That(denied.Body, Does.Contain("forbidden"));
        await AssertStillInReview(id);
        Assert.That(await Decisions(id), Is.Empty, "Odbijeno odobrenje ne sme ostaviti zapis odluke.");
        Assert.That(await Operations(id), Is.EqualTo(new[] { "submit" }));
        Assert.That((await AuditEvents(id)).Select(value => value.Type), Is.EqualTo(new[] { "revision.submitted" }),
            "Nema revision.approved dogadjaja za odbijeno odobrenje.");
    }

    [Test]
    public async Task DeniedSelfApproval_IsAuditedOnce_WithAuthorAsActorAndServerCorrelation()
    {
        var (id, hash) = await CreateDraft();
        await Post(authorClient, id, hash, "submit", 1, "submit-1", HttpStatusCode.OK);

        var denied = await Post(authorClient, id, hash, "approve", 2, "self-approve", HttpStatusCode.Forbidden);

        Assert.That(denied.Correlation, Is.Not.Null.And.Not.Empty);
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var sql = new NpgsqlCommand("""
            SELECT details->>'action', details->>'denialReason'
            FROM audit.audit_events
            WHERE event_type = 'access.denied' AND actor_id = @actor AND correlation_id = @correlation
            """, connection);
        sql.Parameters.AddWithValue("actor", author);
        sql.Parameters.AddWithValue("correlation", denied.Correlation!);
        await using var audit = await sql.ExecuteReaderAsync();
        Assert.That(await audit.ReadAsync(), Is.True, "Odbijanje samoodobravanja mora biti trajno zabelezeno.");
        Assert.Multiple(() =>
        {
            Assert.That(audit.GetString(0), Is.EqualTo("revision.approve"));
            Assert.That(audit.GetString(1), Is.EqualTo("policy_denied"));
        });
        Assert.That(await audit.ReadAsync(), Is.False, "Odluka daje tacno jedan access.denied zapis.");
    }

    // ------------------------------------------------------------------ drugi korak: nezavisni recenzent

    [Test]
    public async Task IndependentReviewer_CanApprove_AfterTheAuthorsSelfApprovalWasRejected()
    {
        var (id, hash) = await CreateDraft();
        await Post(authorClient, id, hash, "submit", 1, "submit-1", HttpStatusCode.OK);
        await Post(authorClient, id, hash, "approve", 2, "self-approve", HttpStatusCode.Forbidden);

        // Odbijeni pokusaj ne sme da pomeri verziju: recenzent i dalje odobrava verziju 2.
        await Post(reviewerClient, id, hash, "approve", 2, "reviewer-approve", HttpStatusCode.OK);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        var revision = await db.LabRevisions.AsNoTracking().SingleAsync(value => value.Id == id);
        var decisions = await db.RevisionDecisions.AsNoTracking().Where(value => value.RevisionId == id).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(revision.Status.ToString(), Is.EqualTo("Published"));
            Assert.That(decisions, Has.Count.EqualTo(1));
            Assert.That(decisions[0].ReviewerId, Is.EqualTo(reviewer));
            Assert.That(decisions[0].ReviewerId, Is.Not.EqualTo(revision.AuthorId));
            Assert.That(decisions[0].Decision.ToString(), Is.EqualTo("Approved"));
            Assert.That(decisions[0].RequiresIndependentApproval, Is.True,
                "Zapis odluke cuva politiku koja je vazila u casu odobravanja.");
        });
        var approval = (await AuditEvents(id)).Single(value => value.Type == AuditEventTypes.RevisionApproved);
        Assert.Multiple(() =>
        {
            Assert.That(approval.Actor, Is.EqualTo(reviewer));
            Assert.That(approval.Details.GetProperty("authorId").GetString(), Is.EqualTo(author.ToString()));
        });
    }

    // ------------------------------------------------------------------ trenutak vazenja politike

    [Test]
    public async Task PolicyTightenedAfterSubmission_IsAppliedAtDecisionTime()
    {
        await SetIndependence(false);
        var (id, hash) = await CreateDraft();
        await Post(authorClient, id, hash, "submit", 1, "submit-1", HttpStatusCode.OK);

        await SetIndependence(true);
        await Post(authorClient, id, hash, "approve", 2, "self-approve", HttpStatusCode.Forbidden);

        await AssertStillInReview(id);
        await Post(reviewerClient, id, hash, "approve", 2, "reviewer-approve", HttpStatusCode.OK);
    }

    [Test]
    public async Task PolicyRowRemovedAfterSubmission_FailsClosed_ForEveryone()
    {
        var (id, hash) = await CreateDraft();
        await Post(authorClient, id, hash, "submit", 1, "submit-1", HttpStatusCode.OK);
        await ExecuteSql("DELETE FROM laboratories.\"CoursePublishPolicies\" WHERE \"CourseId\" = @course",
            ("course", course));

        // Bez politike se ne zna da li je nezavisnost obavezna, pa niko ne sme da objavi.
        await Post(reviewerClient, id, hash, "approve", 2, "reviewer-approve", HttpStatusCode.Forbidden);
        await Post(authorClient, id, hash, "approve", 2, "self-approve", HttpStatusCode.Forbidden);

        await AssertStillInReview(id);
        Assert.That(await Decisions(id), Is.Empty);
    }

    // ------------------------------------------------------------------ nezavisnost nije dovoljna

    [TestCase(false, TestName = "ReviewerWithoutAnyApproveGrant_CannotApprove")]
    [TestCase(true, TestName = "ReviewerWithApproveGrantOnlyInAnotherCourse_CannotApprove")]
    public async Task IndependentReviewer_WithoutApproveGrantForThisCourse_CannotApprove(bool grantInOtherCourse)
    {
        var (id, hash) = await CreateDraft();
        await Post(authorClient, id, hash, "submit", 1, "submit-1", HttpStatusCode.OK);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
            db.Permissions.Remove(await db.Permissions.SingleAsync(value => value.UserId == reviewer &&
                value.CourseId == course && value.Action == RevisionActions.Approve));
            if (grantInOtherCourse)
                db.Permissions.Add(new RevisionCoursePermission { UserId = reviewer, CourseId = Guid.NewGuid(),
                    Action = RevisionActions.Approve, IsActive = true });
            await db.SaveChangesAsync();
        }

        await Post(reviewerClient, id, hash, "approve", 2, "reviewer-approve", HttpStatusCode.Forbidden);

        await AssertStillInReview(id);
        Assert.That(await Decisions(id), Is.Empty);
    }

    [Test]
    public async Task IndependentReviewer_WithApproveButWithoutReadGrant_CannotApprove()
    {
        var (id, hash) = await CreateDraft();
        await Post(authorClient, id, hash, "submit", 1, "submit-1", HttpStatusCode.OK);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RevisionSecurityDbContext>();
            db.Permissions.Remove(await db.Permissions.SingleAsync(value => value.UserId == reviewer &&
                value.CourseId == course && value.Action == RevisionActions.Read));
            await db.SaveChangesAsync();
        }

        await Post(reviewerClient, id, hash, "approve", 2, "reviewer-approve", HttpStatusCode.Forbidden);

        await AssertStillInReview(id);
    }

    // ------------------------------------------------------------------ pomocno

    private async Task AssertStillInReview(Guid id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        var revision = await db.LabRevisions.AsNoTracking().SingleAsync(value => value.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(revision.Status.ToString(), Is.EqualTo("InReview"));
            Assert.That(revision.RowVersion, Is.EqualTo(2), "Samo predaja je promenila verziju.");
        });
    }

    private async Task<List<Guid>> Decisions(Guid id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        return await db.RevisionDecisions.AsNoTracking().Where(value => value.RevisionId == id)
            .Select(value => value.ReviewerId).ToListAsync();
    }

    private async Task<List<string>> Operations(Guid id)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LaboratoriesDbContext>();
        return await db.ReviewOperations.AsNoTracking().Where(value => value.RevisionId == id)
            .Select(value => value.Action).ToListAsync();
    }

    private Task SetIndependence(bool independent) =>
        ExecuteSql("UPDATE laboratories.\"CoursePublishPolicies\" SET \"RequiresIndependentApproval\" = @independent WHERE \"CourseId\" = @course",
            ("independent", independent), ("course", course));

    private async Task ExecuteSql(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(Guid Id, string Hash)> CreateDraft()
    {
        using var response = await authorClient.PostAsJsonAsync("/api/revisions/", new
        {
            laboratoryId = lab, courseId = course,
            definition = new { type = "LogAnalysis", title = "Two-step Lab", goal = "Find failed logins", question = "Which event failed?",
                events = new[] { new { id = "event-1", timestamp = DateTimeOffset.UnixEpoch, message = "Failed login" } },
                evaluator = new { kind = "event-set", version = 1, expectedEventIds = new[] { "event-1" } } }
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("contentHash").GetString()!);
    }

    private static async Task<(string Body, string? Correlation)> Post(HttpClient client, Guid id, string hash,
        string action, int version, string key, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/revisions/{id}/{action}")
            { Content = JsonContent.Create(new { rowVersion = version.ToString(), contentHash = hash, comment = (string?)null }) };
        request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(expected), body);
        var correlation = response.Headers.TryGetValues("X-Correlation-ID", out var values) ? values.FirstOrDefault() : null;
        return (body, correlation);
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