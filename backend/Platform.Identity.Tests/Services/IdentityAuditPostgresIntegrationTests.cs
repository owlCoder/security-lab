using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Api.Enums.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Contracts;
using Platform.Audit.Infrastructure.Persistence;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Testcontainers.PostgreSql;

namespace Platform.Identity.Tests.Services;

[TestFixture]
[NonParallelizable]
public sealed class IdentityAuditPostgresIntegrationTests
{
    private const string Password = "Strong-password!2026";

    private PostgreSqlContainer database = null!;
    private DbContextOptions<PlatformIdentityDbContext> identityOptions = null!;
    private DbContextOptions<AuditDbContext> auditOptions = null!;

    [OneTimeSetUp]
    public async Task StartDatabase()
    {
        database = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await database.StartAsync();

        identityOptions = new DbContextOptionsBuilder<PlatformIdentityDbContext>()
            .UseNpgsql(database.GetConnectionString(), postgres =>
                postgres.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    PlatformIdentityDbContext.SchemaName))
            .Options;
        auditOptions = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(database.GetConnectionString(), postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
            .Options;

        await using var identity = new PlatformIdentityDbContext(identityOptions);
        await identity.Database.MigrateAsync();
        await using var audit = new AuditDbContext(auditOptions);
        await audit.Database.MigrateAsync();
    }

    [OneTimeTearDown]
    public async Task StopDatabase() => await database.DisposeAsync();

    [Test]
    public async Task LoginAndLogoutHttpFlow_PersistsTrustedAuditEventsAndRevokesSession()
    {
        var userId = Guid.NewGuid();
        var email = $"audit-flow-{userId:N}@example.com";
        await SeedAccountAsync(userId, email);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Identity"] = database.GetConnectionString(),
                    ["ConnectionStrings:Audit"] = database.GetConnectionString()
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PlatformIdentityDbContext>();
                services.RemoveAll<DbContextOptions<PlatformIdentityDbContext>>();
                services.AddDbContext<PlatformIdentityDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString(), postgres =>
                        postgres.MigrationsHistoryTable(
                            "__EFMigrationsHistory",
                            PlatformIdentityDbContext.SchemaName)));

                services.RemoveAll<AuditDbContext>();
                services.RemoveAll<DbContextOptions<AuditDbContext>>();
                services.AddDbContext<AuditDbContext>(options =>
                    options.UseNpgsql(database.GetConnectionString(), postgres =>
                        postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        using var loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email, password = Password });
        using var logoutResponse = await client.PostAsync("/api/auth/logout", content: null);

        await using var freshIdentity = new PlatformIdentityDbContext(identityOptions);
        await using var freshAudit = new AuditDbContext(auditOptions);
        var storedSession = await freshIdentity.Sessions.AsNoTracking()
            .SingleAsync(session => session.UserId == userId);
        var events = await freshAudit.Events.AsNoTracking()
            .Where(row => row.ActorId == userId)
            .Select(row => row.EventType)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loginResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(storedSession.RevokedAt, Is.Not.Null);
            Assert.That(events, Does.Contain(AuditEventTypes.AuthLogin));
            Assert.That(events, Does.Contain(AuditEventTypes.AuthLogout));
        });
    }

    [Test]
    public async Task Login_WhenAuditWriteFails_PersistsOnlyARevokedSession()
    {
        var userId = Guid.NewGuid();
        var email = $"audit-down-{userId:N}@example.com";
        await SeedAccountAsync(userId, email);

        await using var identity = new PlatformIdentityDbContext(identityOptions);
        var sessionStore = new SessionStore(identity, TimeProvider.System);
        var login = new LoginService(
            new CredentialVerifier(new EfCredentialStore(identity), new BCryptPasswordHasher()),
            new StoreSessionCreator(sessionStore, NullLogger<StoreSessionCreator>.Instance),
            new SessionManager(identity),
            CreateProducer(new UnavailableAuditWriter()));

        var result = await login.ExecuteAsync(email, Password, Guid.NewGuid().ToString("N"));

        Assert.That(result.Outcome, Is.EqualTo(LoginOutcome.Unavailable));
        Assert.That(result.Session, Is.Null);

        await using var freshIdentity = new PlatformIdentityDbContext(identityOptions);
        var storedSession = await freshIdentity.Sessions.AsNoTracking()
            .SingleAsync(session => session.UserId == userId);
        Assert.That(storedSession.RevokedAt, Is.Not.Null);
    }

    private async Task SeedAccountAsync(Guid userId, string email)
    {
        var account = new UserAccount(userId, CredentialVerifier.NormalizeEmail(email));
        account.SetPasswordHash(new BCryptPasswordHasher().Hash(Password));

        await using var context = new PlatformIdentityDbContext(identityOptions);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
    }

    private static IdentityAuditProducer CreateProducer(ITrustedAuditWriter writer) =>
        new(writer, NullLogger<IdentityAuditProducer>.Instance);

    private sealed class UnavailableAuditWriter : ITrustedAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(
            AuditEventDraft draft,
            AuditContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuditWriteResult.Unavailable());
    }
}
