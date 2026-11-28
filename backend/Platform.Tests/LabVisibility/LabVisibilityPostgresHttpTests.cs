using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Models;
using Platform.LabVisibility.Contracts;
using Platform.LabVisibility.Domain;
using Platform.LabVisibility.Infrastructure.Persistence;

namespace Platform.Tests.LabVisibility;

// AUT-04 (Antonić, KT2-09): ceo HTTP tok (sesija -> politika -> EF reader -> PostgreSQL) za
// studentski prikaz i pregled, nad pravom bazom i demo seed-om iz Program.cs.
// Bazu pravi scripts/test-labvisibility-postgres.ps1; bez promenljive testovi se preskaču.
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class LabVisibilityPostgresHttpTests
{
    private const string Teacher = "11111111-1111-1111-1111-111111111111";
    private const string Student = "22222222-2222-2222-2222-222222222222";
    private const string SecretAnswer = "SECRET-HTTP-ANSWER-7731";

    private static readonly Guid CourseId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private PostgresApiFactory factory = null!;
    private HttpClient client = null!;
    private Guid publishedLab;
    private Guid draftLab;

    [OneTimeSetUp]
    public async Task StartApiOverPostgres()
    {
        var connectionString = Environment.GetEnvironmentVariable(LabVisibilityPostgresTests.ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"{LabVisibilityPostgresTests.ConnectionVariable} nije podešen; HTTP testovi nad PostgreSQL-om su preskočeni.");

        factory = new PostgresApiFactory(connectionString);
        client = factory.CreateClient(); // pokreće Program.cs: migracija + demo seed u test bazu

        publishedLab = await CreateLabWithSecretKeyAsync(LabVisibilityState.Published);
        draftLab = await CreateLabWithSecretKeyAsync(LabVisibilityState.Draft);
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        if (factory is null)
            return;

        await using (var db = await DbFactory.CreateDbContextAsync())
        {
            var ids = new[] { publishedLab, draftLab };
            await db.PublicContents.Where(x => ids.Contains(x.LabId)).ExecuteDeleteAsync();
        }

        client.Dispose();
        await factory.DisposeAsync();
    }

    private IDbContextFactory<LabVisibilityDbContext> DbFactory =>
        factory.Services.GetRequiredService<IDbContextFactory<LabVisibilityDbContext>>();

    [Test]
    public async Task DemoSeed_IsInPostgres_WithKeyInSeparateTable()
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var demoLabs = new[] { LabVisibilityDemoSeed.PublishedLabId, LabVisibilityDemoSeed.DraftLabId };

        var labs = await db.PublicContents.CountAsync(x => demoLabs.Contains(x.LabId));
        var keyRows = await db.EvaluatorKeys.CountAsync(x => demoLabs.Contains(x.LabId));

        Assert.That(labs, Is.EqualTo(2));
        Assert.That(keyRows, Is.EqualTo(3));
    }

    [Test]
    public async Task StudentView_OverPostgres_ReturnsLabWithoutAnswer()
    {
        using var response = await client.SendAsync(Request(publishedLab, "student-view", Student));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("Zadatak iz baze"));
            Assert.That(body, Does.Not.Contain(SecretAnswer));
            Assert.That(body, Does.Not.Contain("expectedAnswer").IgnoreCase);
        });
    }

    [Test]
    public async Task StudentView_OverPostgres_DraftIsNotFound()
    {
        using var response = await client.SendAsync(Request(draftLab, "student-view", Student));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task StudentView_OverPostgres_UnknownLabIsNotFound()
    {
        using var response = await client.SendAsync(Request(Guid.NewGuid(), "student-view", Student));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Preview_OverPostgres_TeacherSeesDraftWithoutAnswer()
    {
        using var response = await client.SendAsync(Request(draftLab, "preview", Teacher));
        var body = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("Zadatak iz baze"));
            Assert.That(body, Does.Not.Contain(SecretAnswer));
            Assert.That(body, Does.Not.Contain("expectedAnswer").IgnoreCase);
        });
    }

    [Test]
    public async Task Preview_OverPostgres_StudentGetsNotFound()
    {
        using var response = await client.SendAsync(Request(draftLab, "preview", Student));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // Laboratorija na demo kursu sa tekstualnim zadatkom; odgovor nije među opcijama,
    // pa njegov tekst sme da se pojavi samo ako je ključ procureo.
    private async Task<Guid> CreateLabWithSecretKeyAsync(LabVisibilityState state)
    {
        var content = LabPublicContent.Create(
            Guid.NewGuid(), CourseId, Guid.Parse(Teacher), state,
            "Zadatak iz baze", "Provera prikaza nad PostgreSQL-om",
            [new PublicTask("t1", 1, "Upiši vrednost iz dnevnika.", [])]);

        await using (var db = await DbFactory.CreateDbContextAsync())
        {
            db.PublicContents.Add(new LabPublicContentRow
            {
                LabId = content.LabId,
                CourseId = content.CourseId,
                AuthorId = content.AuthorId,
                State = content.State.ToString(),
                Title = content.Title,
                Goal = content.Goal,
                Tasks =
                [
                    new LabPublicTaskRow
                    {
                        LabId = content.LabId,
                        TaskId = "t1",
                        Order = 1,
                        Prompt = "Upiši vrednost iz dnevnika.",
                        Options = []
                    }
                ]
            });
            await db.SaveChangesAsync();
        }

        var store = factory.Services.GetRequiredService<IEvaluatorKeyStore>();
        Assert.That(store, Is.InstanceOf<EfEvaluatorKeyStore>(), "API mora da koristi EF store nad test bazom.");
        await store.SaveAsync(EvaluatorKey.Create(content, [new TaskAnswerKey("t1", SecretAnswer)]));

        return content.LabId;
    }

    private static HttpRequestMessage Request(Guid labId, string view, string user)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/labs/{labId:D}/{view}");
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={user}");
        return request;
    }

    // Pravi API (Development) sa EF skladištem nad test bazom. Zamenjeni su samo sesija i status
    // naloga, isto kao u LabPreviewHostFactory; reader, store i politike su pravi.
    private sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        private static readonly HashSet<string> KnownUsers = [Teacher, Student];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(
                $"ConnectionStrings:{LabVisibilityDatabase.ConnectionStringName}", connectionString);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISessionManager>();
                services.AddSingleton<ISessionManager, TestSessionManager>();

                services.RemoveAll<IAccountStatusReader>();
                services.AddSingleton<IAccountStatusReader, ActiveTestAccounts>();

                // Uvek test baza, i kad lokalni user-secrets ima drugi connection string.
                services.RemoveAll<IDbContextFactory<LabVisibilityDbContext>>();
                services.AddSingleton<IDbContextFactory<LabVisibilityDbContext>>(new TestDbFactory(connectionString));
            });
        }

        private sealed class TestDbFactory(string connectionString) : IDbContextFactory<LabVisibilityDbContext>
        {
            public LabVisibilityDbContext CreateDbContext() =>
                new(new DbContextOptionsBuilder<LabVisibilityDbContext>()
                    .UseNpgsql(
                        connectionString,
                        npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", LabVisibilityDbContext.Schema))
                    .Options);
        }

        private sealed class TestSessionManager : ISessionManager
        {
            public Task<ValidateSession?> ValidateAsync(string token, CancellationToken ct = default) =>
                Task.FromResult(KnownUsers.Contains(token)
                    ? new ValidateSession(Guid.NewGuid(), Guid.Parse(token), AuthenticationLevel.Active)
                    : null);

            public Task<int> RevokeAllForUser(Guid userId, string reason, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<bool> RevokeSession(Guid ownerUserId, Guid sessionHandle, CancellationToken ct = default) =>
                throw new NotSupportedException();

            public Task<IReadOnlyList<SessionInfo>> ListForUser(Guid userId, CancellationToken ct = default) =>
                throw new NotSupportedException();
        }

        private sealed class ActiveTestAccounts : IAccountStatusReader
        {
            public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken = default) =>
                Task.FromResult(KnownUsers.Contains(accountId.ToString("D"))
                    ? new AccountSnapshot(accountId, AccountStatus.Active)
                    : null);
        }
    }
}
