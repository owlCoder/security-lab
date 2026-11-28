using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NUnit.Framework;
using Platform.Access.AspNet;
using Platform.Access.Contracts;
using Platform.Access.Domain.Enums;
using Platform.Access.Domain.Models;
using Platform.Api.Modules.Administration;
using Platform.Api.Modules.Administration.Api;
using Platform.Api.Modules.Administration.Persistence;
using Platform.Api.Modules.Administration.Integration;
using Team21.Acc.Core.Courses;
using Team21.Acc.Infrastructure.Courses.Data;
using Platform.Api.Modules.Kt1;
using Platform.Api.Services.Identity;
using Platform.Audit.Composition;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Interfaces;
using Platform.Identity.Persistence;
using Platform.Identity.Services;
using Platform.Identity.Testing;

namespace Platform.Coursework.Tests;

[TestFixture, NonParallelizable, FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed partial class AdministrationHttpTests
{
    private string bootstrap = null!, connection = null!, database = null!, password = null!;
    private AdministrationFactory factory = null!;
    private readonly List<HttpClient> clients = [];
    private static string Path(Guid? course = null) => $"/api/course-administration/{course ?? DemoStore.Course}";

    [SetUp]
    public async Task SetUp()
    {
        bootstrap = Environment.GetEnvironmentVariable("COURSEWORK_TEST_POSTGRES")!;
        if (string.IsNullOrWhiteSpace(bootstrap))
        { Assert.Ignore("Use scripts/test-coursework-postgres.ps1 to execute the real Identity/PostgreSQL HTTP integration."); return; }
        database = "cw_http_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(bootstrap))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        connection = new NpgsqlConnectionStringBuilder(bootstrap) { Database = database, Timeout = 5, CommandTimeout = 5 }.ConnectionString;
        await using (var db = IdentityDb()) await db.Database.MigrateAsync();
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(connection);
        await using (var catalog = CoursesDb())
        {
            await catalog.Database.MigrateAsync();
            catalog.Courses.Add(new Course("COURSE-A", "Catalog name A", "", DemoStore.Admin) { Id=DemoStore.Course });
            catalog.Courses.Add(new Course("COURSE-B", "Catalog name B", "", DemoStore.OtherAdmin) { Id=DemoStore.OtherCourse });
            await catalog.SaveChangesAsync();
        }
        await AdministrationSchema.MigrateAsync(connection);
        password = Guid.NewGuid().ToString("N"); // disposable test credentials, never project configuration
        var hash = new BCryptPasswordHasher().Hash(password);
        await using (var db = IdentityDb())
        {
            foreach (var id in new[] { DemoStore.Admin, DemoStore.OtherAdmin, DemoStore.DelegateUser, DemoStore.Teacher, DemoStore.ForeignTeacher, DemoStore.Marko, DemoStore.Ana })
            {
                var account = new UserAccount(id, Email(id), AccountStatus.Active,
                    id == DemoStore.Teacher ? AccountRole.Teacher : AccountRole.Student);
                account.SetPasswordHash(hash);
                db.Accounts.Add(account);
            }
            await db.SaveChangesAsync();
        }
        await Sql("""
            INSERT INTO coursework_admin.course_settings(course_id, administrative_label)
                VALUES (@course, 'Administrative label A'), (@other, 'Administrative label B');
            INSERT INTO coursework_admin.grants(grant_id, course_id, grantee_id, granted_by, capabilities, is_root)
                VALUES (@root, @course, @actor, @actor, 7, true), (@otherRoot, @other, @otherActor, @otherActor, 7, true)
            """, ("course", DemoStore.Course), ("other", DemoStore.OtherCourse), ("root", DemoStore.RootGrant),
            ("actor", DemoStore.Admin), ("otherRoot", DemoStore.OtherRootGrant), ("otherActor", DemoStore.OtherAdmin));
        factory = new(connection);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var client in clients) client.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        if (database is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(bootstrap);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private static string Email(Guid id) => $"{id:N}@coursework.example.invalid";
    private CoursesDbContext CoursesDb() => new(new DbContextOptionsBuilder<CoursesDbContext>()
        .UseNpgsql(connection, p => p.MigrationsHistoryTable("__EFMigrationsHistory", "courses")).Options);
    private PlatformIdentityDbContext IdentityDb() => new(new DbContextOptionsBuilder<PlatformIdentityDbContext>()
        .UseNpgsql(connection, p => p.MigrationsHistoryTable("__EFMigrationsHistory", "identity")).Options);

    private async Task<object?> Sql(string sql, params (string Name, object Value)[] parameters)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }

    private HttpClient Client(string? cookie = null)
    {
        var client = factory.CreateCsrfClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Coursework-Request", "1");
        if (cookie is not null) client.DefaultRequestHeaders.Add("Cookie", cookie);
        clients.Add(client);
        return client;
    }

    private async Task<(HttpClient Client, string Cookie)> Login(Guid id)
    {
        var client = Client();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = Email(id), Password = password });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var issued = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal));
        Assert.That(issued.ToLowerInvariant(), Does.Contain("secure").And.Contain("httponly").And.Contain("samesite=strict"));
        var cookie = issued.Split(';')[0];
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain(cookie));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return (client, cookie);
    }

    private async Task<GrantView> Delegate(HttpClient root, string[]? capabilities = null)
    {
        var response = await root.PostAsJsonAsync(Path() + "/delegations", new DelegateRequest(DemoStore.DelegateUser,
            capabilities ?? ["ViewAdministration", "ManageSettings"]));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GrantView>())!;
    }

    [Test]
    public async Task RealLoginDelegationSettingsRevocationRejectsTheExactPreviouslyIssuedCookie()
    {
        var (root, _) = await Login(DemoStore.Admin);
        var (delegated, cookieBeforeGrant) = await Login(DemoStore.DelegateUser);
        TestContext.Out.WriteLine("PASS 1. Real password login for root and delegate: HTTP 200; separate clients.");
        Assert.That((await delegated.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var grant = await Delegate(root);
        TestContext.Out.WriteLine("PASS 2. Limited delegation created: HTTP 200.");
        Assert.That((await delegated.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await delegated.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("Delegated label", 1))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        TestContext.Out.WriteLine("PASS 3. Delegate read and persisted settings update: HTTP 200.");
        Assert.That((await delegated.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("stale", 1))).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        TestContext.Out.WriteLine("PASS 4. Stale version rejected: HTTP 409.");
        Assert.That((await root.PostAsJsonAsync(Path() + $"/delegations/{grant.Id}/revoke", new RevokeRequest(1))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        TestContext.Out.WriteLine("PASS 5. Root revoked the delegation: HTTP 200.");
        // No cookie handler or sign-in after revocation: reuse the exact captured bytes.
        var replay = Client(cookieBeforeGrant);
        Assert.That(replay.DefaultRequestHeaders.GetValues("Cookie").Single(), Is.EqualTo(cookieBeforeGrant));
        Assert.That((await replay.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await replay.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("forbidden", 2))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        TestContext.Out.WriteLine("PASS 6. Exact pre-revocation cookie: administration read and settings write both denied with HTTP 404.");
        await using var db = IdentityDb();
        Assert.That(await db.Sessions.CountAsync(s => s.UserId == DemoStore.DelegateUser), Is.EqualTo(1));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(2));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE outcome='ALLOW' AND action <> 'course.admin.read'"), Is.EqualTo(3L));
        TestContext.Out.WriteLine("ADM08_HTTP_FLOW: real password login -> scoped delegation -> persisted update -> revoke -> same-cookie read/write denied.");
    }

    [TestCase(false), TestCase(true)]
    public async Task AnonymousOrInvalidSessionCannotReadOrWrite(bool invalidCookie)
    {
        var client = Client(invalidCookie ? SessionCookie.Name + "=invalid" : null);
        Assert.That((await client.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("no", 1))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase("Production"), TestCase("Development")]
    public async Task ForgedHeadersAndSyntheticKt1CookieCannotAuthenticateProductionAdministration(string environment)
    {
        await factory.DisposeAsync();
        factory = new(connection, environment: environment, kt1: true);
        var forged = Client();
        forged.DefaultRequestHeaders.Add("X-Test-User", DemoStore.Admin.ToString());
        forged.DefaultRequestHeaders.Add("X-Stub-User", DemoStore.Admin.ToString());
        forged.DefaultRequestHeaders.Add("X-Development-Subject", DemoStore.Admin.ToString());
        Assert.That((await forged.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        if (environment == "Development")
        {
            forged.DefaultRequestHeaders.Add("X-Kt1-Demo", "1");
            var synthetic = await forged.PostAsync("/api/kt1/session/admin", null);
            Assert.That(synthetic.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            var cookie = synthetic.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("SecurityLab.CourseworkKt1=", StringComparison.Ordinal)).Split(';')[0];
            Assert.That((await Client(cookie).GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
        else Assert.That((await forged.PostAsync("/api/kt1/session/admin", null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ForeignAndMissingCourseHaveTheSamePublicResponse()
    {
        var (root, _) = await Login(DemoStore.Admin);
        var foreign = await root.GetAsync(Path(DemoStore.OtherCourse));
        var missing = await root.GetAsync(Path(Guid.NewGuid()));
        Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(missing.StatusCode, Is.EqualTo(foreign.StatusCode));
        Assert.That(await missing.Content.ReadAsStringAsync(), Is.EqualTo(await foreign.Content.ReadAsStringAsync()));
        Assert.That(foreign.Headers.CacheControl!.NoStore, Is.True);
    }

    [Test]
    public async Task TeachingAndAdministrativePowerRemainSeparate()
    {
        var (teacher, _) = await Login(DemoStore.Teacher);
        Assert.That((await teacher.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var (root, _) = await Login(DemoStore.Admin);
        Assert.That((await root.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var accountCreation = await root.PostAsJsonAsync("/api/admin/accounts", new { Email = "not-created@example.invalid", Password = Guid.NewGuid().ToString("N") });
        Assert.That(accountCreation.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
        await using var db = IdentityDb();
        Assert.That(await db.Accounts.CountAsync(a => a.Email == "not-created@example.invalid"), Is.Zero);
        Assert.That((await db.Accounts.SingleAsync(a => a.Id == DemoStore.Admin)).Role, Is.EqualTo(AccountRole.Student));
    }

    [TestCase(AccountStatus.Blocked), TestCase(AccountStatus.Archived)]
    public async Task CurrentAccountStatusRejectsExistingCookie(AccountStatus status)
    {
        var (root, cookie) = await Login(DemoStore.Admin);
        Assert.That((await root.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using (var db = IdentityDb()) await db.Accounts.Where(a => a.Id == DemoStore.Admin).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
        var replay = Client(cookie);
        Assert.That((await replay.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await replay.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("no", 1))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(false), TestCase(true)]
    public async Task LoggedOutOrExpiredSessionCannotBeReused(bool expired)
    {
        var (root, cookie) = await Login(DemoStore.Admin);
        if (expired)
        {
            await using var db = IdentityDb();
            await db.Sessions.Where(s => s.UserId == DemoStore.Admin).ExecuteUpdateAsync(s => s.SetProperty(v => v.ExpiresAt, DateTimeOffset.UtcNow.AddHours(-1)));
        }
        else Assert.That((await root.PostAsync("/api/auth/logout", null)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await Client(cookie).GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task MfaPendingSessionIsNotAnAdministrativeIdentity()
    {
        using var scope = factory.Services.CreateScope();
        var session = await scope.ServiceProvider.GetRequiredService<ISessionStore>().CreateForActiveAccountAsync(
            DemoStore.Admin, AuthLevel.MfaPending, TimeSpan.FromMinutes(5), CancellationToken.None);
        Assert.That(session, Is.Not.Null);
        Assert.That((await Client(SessionCookie.Name + "=" + session!.Value.RawToken).GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [TestCase(false), TestCase(true)]
    public async Task WriteRequiresHeaderAndSameOrigin(bool foreignOrigin)
    {
        var (root, _) = await Login(DemoStore.Admin);
        if (foreignOrigin) root.DefaultRequestHeaders.Add("Origin", "https://outside.example.invalid");
        else root.DefaultRequestHeaders.Remove("X-Coursework-Request");
        Assert.That((await root.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("no", 1))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
    }

    [TestCase("ManageDelegations"), TestCase("Teacher"), TestCase("3")]
    public async Task HttpDelegationCannotEscalateCapabilities(string capability)
    {
        var (root, _) = await Login(DemoStore.Admin);
        Assert.That((await root.PostAsJsonAsync(Path() + "/delegations", new DelegateRequest(DemoStore.DelegateUser, [capability]))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants"), Is.EqualTo(2L));
    }

    [Test]
    public async Task ViewOnlyDelegateCannotUpdateOrRedelegate()
    {
        var (root, _) = await Login(DemoStore.Admin);
        await Delegate(root, ["ViewAdministration"]);
        var (view, _) = await Login(DemoStore.DelegateUser);
        Assert.That((await view.GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await view.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("no", 1))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await view.PostAsJsonAsync(Path() + "/delegations", new DelegateRequest(DemoStore.Marko, ["ViewAdministration"]))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase(false), TestCase(true)]
    public async Task DelegationRequiresAnExistingActiveIdentityAccount(bool missing)
    {
        var (root, _) = await Login(DemoStore.Admin);
        if (!missing)
        {
            await using var db = IdentityDb();
            await db.Accounts.Where(a => a.Id == DemoStore.DelegateUser).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AccountStatus.Blocked));
        }
        var target = missing ? Guid.NewGuid() : DemoStore.DelegateUser;
        Assert.That((await root.PostAsJsonAsync(Path() + "/delegations", new DelegateRequest(target, ["ViewAdministration"]))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.grants"), Is.EqualTo(2L));
    }

    [Test]
    public async Task OversizedRequestIsRejectedBeforeItCanChangeSettings()
    {
        var (root, _) = await Login(DemoStore.Admin);
        Assert.That((await root.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest(new string('x', 20000), 1))).StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
    }

    // US-15 / SEC-03 with ADM-08: the CSRF check runs before the ADM-08 16 KiB body limit, so without X-CSRF-TOKEN
    // it must refuse the request without reading the body. A valid token placed in the form body must not count.
    [Test]
    public async Task WriteWithValidSessionButWithoutCsrfHeaderIsRefusedWithoutReadingTheBody()
    {
        var (_, cookie) = await Login(DemoStore.Admin);
        var sessionToken = cookie[(SessionCookie.Name.Length + 1)..];
        using var raw = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        var pair = await CsrfPair.FetchAsync(raw, sessionToken);
        var fieldName = factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.FormFieldName;

        using var request = new HttpRequestMessage(HttpMethod.Put, Path() + "/settings")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [fieldName] = pair.Token,
                ["administrativeLabel"] = "changed by a forged form",
                ["expectedVersion"] = "1"
            })
        };
        request.Headers.Add("X-Coursework-Request", "1");
        request.Headers.Add("Cookie", $"{cookie}; {pair.Cookie}");

        factory.BodyRead = false;
        factory.ObserveBody = true;
        using var response = await raw.SendAsync(request);
        factory.ObserveBody = false;

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.RootElement.GetProperty("code").GetString(), Is.EqualTo(CsrfToken.InvalidCode));
        Assert.That(factory.BodyRead, Is.False, "The CSRF check read the request body.");
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
        Assert.That(await Sql("SELECT administrative_label FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)),
            Is.EqualTo("Administrative label A"));
    }

    [Test]
    public async Task OwnAuditFailureRollsBackAnHttpMutation()
    {
        var (root, _) = await Login(DemoStore.Admin);
        await Sql("""
            CREATE FUNCTION coursework_admin.reject_audit() RETURNS trigger LANGUAGE plpgsql AS
              $$ BEGIN RAISE EXCEPTION 'injected producer audit failure'; END $$;
            CREATE TRIGGER reject_audit BEFORE INSERT ON coursework_admin.outbox
              FOR EACH ROW EXECUTE FUNCTION coursework_admin.reject_audit();
            """);
        Assert.That((await root.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("rolled back", 1))).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public async Task DependencyOutageIsFailClosedWithoutResourceEnumeration(bool identity)
    {
        var (root, _) = await Login(DemoStore.Admin);
        await Sql(identity ? "ALTER TABLE identity.sessions RENAME TO unavailable_sessions"
            : "ALTER TABLE coursework_admin.grants RENAME TO unavailable_grants");
        var present = await root.GetAsync(Path());
        var missing = await root.GetAsync(Path(Guid.NewGuid()));
        Assert.That(present.StatusCode, Is.EqualTo(identity ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NotFound));
        Assert.That(missing.StatusCode, Is.EqualTo(present.StatusCode));
        Assert.That(await missing.Content.ReadAsStringAsync(), Is.EqualTo(await present.Content.ReadAsStringAsync()));
    }

    [Test]
    public async Task SessionRevokedDuringOperationRollsBackBeforeCommit()
    {
        var (root, _) = await Login(DemoStore.Admin);
        var decisions = 0;
        factory.AfterAllow = async request =>
        {
            if (request.Action != AdministrationPolicy.Rename || Interlocked.Increment(ref decisions) != 2) return;
            await using var db = IdentityDb();
            await db.Sessions.Where(s => s.UserId == DemoStore.Admin).ExecuteUpdateAsync(s => s.SetProperty(v => v.RevokedAt, DateTimeOffset.UtcNow));
        };
        Assert.That((await root.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("must roll back", 1))).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(decisions, Is.EqualTo(2));
        Assert.That(await Sql("SELECT version FROM coursework_admin.course_settings WHERE course_id = @id", ("id", DemoStore.Course)), Is.EqualTo(1));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE outcome='ALLOW'"), Is.EqualTo(0L));
        Assert.That(await Sql("SELECT count(*) FROM coursework_admin.outbox WHERE outcome='DENY' AND reason='SESSION_INVALIDATED'"), Is.EqualTo(1L));
    }

    [Test]
    public async Task HttpMutationAuditContainsServerActorAndCorrelationWithoutSecrets()
    {
        var (root, cookie) = await Login(DemoStore.Admin);
        root.DefaultRequestHeaders.Add("X-Correlation-ID", "untrusted-client-value");
        var response = await root.PutAsJsonAsync(Path() + "/settings", new RenameCourseRequest("private input label", 1));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.That(correlation, Is.Not.EqualTo("untrusted-client-value"));
        var json = (string)(await Sql("SELECT row_to_json(e)::text FROM coursework_admin.outbox e WHERE action='course.settings.update'"))!;
        using var entry = JsonDocument.Parse(json);
        Assert.That(entry.RootElement.GetProperty("actor_id").GetGuid(), Is.EqualTo(DemoStore.Admin));
        Assert.That(entry.RootElement.GetProperty("correlation_id").GetString(), Is.EqualTo(correlation));
        Assert.That(json, Does.Not.Contain(cookie).And.Not.Contain(password).And.Not.Contain("private input label"));
    }

    [Test]
    public async Task EndpointMetadataIsExplicitAndFeatureCanBeDisabled()
    {
        var routes = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<AdministrationEndpointMetadata>() is not null).ToArray();
        Assert.That(routes.Length, Is.EqualTo(4));
        Assert.That(routes.All(e => e.Metadata.GetOrderedMetadata<IAccessRequirementMetadata>().Count == 1), Is.True);
        Assert.That(routes.All(e => e.Metadata.GetMetadata<AllowPublicAttribute>() is null && e.Metadata.GetMetadata<DelegatedAuthorizationAttribute>() is null), Is.True);
        await factory.DisposeAsync();
        factory = new(connection, enabled: false);
        Assert.That((await Client().GetAsync(Path())).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private sealed class AdministrationFactory(string connection, bool enabled = true, string environment = "Production", bool kt1 = false,
        bool attempts = false, bool membershipProvider = true)
        : WebApplicationFactory<Program>
    {
        public Func<AccessRequest, Task>? AfterAllow { get; set; }
        public AttemptProviderDouble Attempts { get; } = new();
        public MembershipProviderDouble Members { get; } = new();
        /// <summary>While set, every request body is wrapped first in the pipeline, and <see cref="BodyRead"/> records a read.</summary>
        public volatile bool ObserveBody;
        public volatile bool BodyRead;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(AdministrationEndpoints.EnabledSetting, enabled.ToString());
            builder.UseSetting(Platform.Api.Modules.Access.Api.AttemptEndpoints.EnabledSetting, attempts.ToString());
            builder.UseSetting("CourseworkAttempts:ProviderBaseUrl", "https://att.example.invalid/v1/");
            builder.UseSetting("CourseworkAttempts:ProviderToken", AttemptProviderDouble.TestToken);
            builder.UseSetting("ConnectionStrings:CourseworkAdministration", connection);
            builder.UseSetting("ConnectionStrings:Identity", connection);
            builder.UseSetting("ConnectionStrings:Team21", connection);
            builder.UseSetting("ConnectionStrings:Audit", connection);
            builder.UseSetting("RateLimiting:Login:PermitLimit", "100");
            builder.UseSetting("Kt1Demo:Enabled", kt1.ToString());
            builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
            builder.ConfigureServices(services =>
            {
                if (attempts)
                {
                    services.AddHttpClient<Platform.Api.Modules.Access.Integration.HttpAttemptStore>()
                        .ConfigurePrimaryHttpMessageHandler(() => new AttemptProviderHandler(Attempts));
                    if (membershipProvider) services.AddSingleton<Platform.Access.Contracts.IMembershipReader>(Members);
                }
                var descriptor = services.Last(s => s.ServiceType == typeof(IAccessDecision));
                services.Remove(descriptor);
                services.AddSingleton<IAccessDecision>(sp => new ObservedDecision(
                    (IAccessDecision)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(sp) ??
                        ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!)), this));
                services.AddSingleton<IStartupFilter>(new BodyProbeStartupFilter(this));
            });
        }
        // Runs before every middleware of Program.cs, so it sees any read of the body, including antiforgery's form read.
        private sealed class BodyProbeStartupFilter(AdministrationFactory owner) : IStartupFilter
        {
            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                app.Use(async (http, inner) =>
                {
                    if (owner.ObserveBody) http.Request.Body = new ReadProbeStream(http.Request.Body, () => owner.BodyRead = true);
                    await inner(http);
                });
                next(app);
            };
        }
        private sealed class ReadProbeStream(Stream inner, Action onRead) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) { onRead(); return inner.Read(buffer, offset, count); }
            public override int Read(Span<byte> buffer) { onRead(); return inner.Read(buffer); }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            { onRead(); return inner.ReadAsync(buffer, offset, count, cancellationToken); }
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            { onRead(); return inner.ReadAsync(buffer, cancellationToken); }
            public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
            { onRead(); return inner.CopyToAsync(destination, bufferSize, cancellationToken); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
        private sealed class ObservedDecision(IAccessDecision inner, AdministrationFactory owner) : IAccessDecision
        {
            public async ValueTask<AccessDecision> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            {
                var decision = await inner.DecideAsync(request, cancellationToken);
                if (decision.Outcome == AccessOutcome.Allow && owner.AfterAllow is { } hook) await hook(request);
                return decision;
            }
        }
    }
}
