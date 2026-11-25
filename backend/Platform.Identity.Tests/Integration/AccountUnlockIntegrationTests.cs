using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Api.Services.Identity;
using Platform.Audit.Application;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

/// <summary>
/// IDN-05 administrator unlock against a real PostgreSQL database and the real host: the lockout cleared by the
/// admin endpoint is the lockout login reads, and the Audit record is atomic with the change.
/// Accounts are locked with SQL, so the per-IP login limiter plays no part in the arrangement.
/// </summary>
[Category("IDN-05")]
public class AccountUnlockIntegrationTests : IntegrationTestBase
{
    // Must be an id in Stub:AdminIds of appsettings.Development.json.
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Password = "Password123!";
    private const string WrongPassword = "Wrong-Password123!";

    [OneTimeSetUp]
    public async Task MigrateAuditSchema() =>
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(TestDatabaseFixture.ConnectionString);

    [Test]
    public async Task Admin_UnlocksLockedAccount_LoginWorksAgain_AndSuccessIsAudited()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        await LockAsync(student.Id, failedLogins: 2);
        var lockedUntil = (await UserTestData.GetAsync(Factory, student.Id))!.LockoutEnd!.Value;
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        Assert.That((await LoginAsync(student.Email, Password)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "the correct password is refused while the account is locked");

        var response = await SendAsync(UnlockUrl(student.Id), adminSession.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<UnlockResponse>();
        Assert.That(body, Is.EqualTo(new UnlockResponse(student.Id, true)));

        var stored = (await UserTestData.GetAsync(Factory, student.Id))!;
        Assert.That(stored.LockoutEnd, Is.Null);
        Assert.That(stored.FailedLoginCount, Is.Zero);
        Assert.That(stored.Status, Is.EqualTo(AccountStatus.Active), "an unlock does not change the status");

        Assert.That((await LoginAsync(student.Email, Password)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var record = (await AuditRowsAsync(student.Id)).Single();
        Assert.That(record.Outcome, Is.EqualTo("Success"));
        Assert.That(record.ActorId, Is.EqualTo(admin.Id));
        Assert.That(record.Details, Does.Contain("lockoutEnd").And.Not.Contain(student.Email));
        // The exact value that was cleared, in UTC, with the microseconds PostgreSQL stored.
        Assert.That(record.Details, Does.Contain(
            lockedUntil.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture)));
    }

    [Test]
    public async Task Student_CannotUnlock_NothingChanges_DenialIsAuditedByAccess()
    {
        var (_, student) = await ArrangeAdminAndStudentAsync();
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        await LockAsync(other.Id);
        var lockedUntil = (await UserTestData.GetAsync(Factory, other.Id))!.LockoutEnd;
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        // #113: the shared factory replaces ITrustedAuditWriter with a fake that stores nothing. This test reads
        // the real access.denied row, so it restores the real writer; ConnectionStrings:Audit is the test database.
        using var audited = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ITrustedAuditWriter>();
            services.AddScoped<ITrustedAuditWriter>(provider => provider.GetRequiredService<AuditWriter>());
        }));
        using var client = audited.CreateClient();

        var response = await SendAsync(UnlockUrl(other.Id), studentSession.RawToken, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await UserTestData.GetAsync(Factory, other.Id))!.LockoutEnd, Is.EqualTo(lockedUntil));

        // ACC-05: the central Access middleware denies a non-administrator before the handler runs, so the denial
        // is recorded once as access.denied for the caller; UnlockAccount never starts and writes no account.unlocked row.
        Assert.That(await AuditRowsAsync(other.Id), Is.Empty);
        var denial = (await AccessDeniedRowsAsync(student.Id)).Single();
        Assert.That(denial.Outcome, Is.EqualTo("Denied"));
        Assert.That(denial.Details, Does.Contain("account.unlock"));
    }

    [Test]
    public async Task WithoutSession_Returns401SessionInvalid_NothingChanges()
    {
        var (_, student) = await ArrangeAdminAndStudentAsync();
        await LockAsync(student.Id);

        var response = await Client.PostAsync(UnlockUrl(student.Id), content: null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("session_invalid"));
        Assert.That((await UserTestData.GetAsync(Factory, student.Id))!.LockoutEnd, Is.Not.Null);
        Assert.That(await AuditRowsAsync(student.Id), Is.Empty);
    }

    // A stale session: an unknown token, or the administrator's own session after it was revoked.
    [TestCase(false)]
    [TestCase(true)]
    public async Task InvalidSession_Returns401SessionInvalid_ClearsCookie_NothingChanges(bool revokedAdminSession)
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        await LockAsync(student.Id);
        var lockedUntil = (await UserTestData.GetAsync(Factory, student.Id))!.LockoutEnd;
        var token = "not-a-session-token";
        if (revokedAdminSession)
        {
            token = (await SessionTestData.CreateAsync(Factory, admin.Id)).RawToken;
            await RevokeSessionsAsync(admin.Id);
        }

        var response = await SendAsync(UnlockUrl(student.Id), token);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("session_invalid"));
        var cleared = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith($"{SessionCookie.Name}=", StringComparison.Ordinal)
                                     && cookie.Contains("1970", StringComparison.Ordinal));
        Assert.That(cleared, Is.True, "the rejected cookie is expired");
        Assert.That((await UserTestData.GetAsync(Factory, student.Id))!.LockoutEnd, Is.EqualTo(lockedUntil));
        Assert.That(await AuditRowsAsync(student.Id), Is.Empty);
    }

    [Test]
    public async Task AccountThatIsNotLocked_Returns409_AndFailureIsAudited()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);

        var response = await SendAsync(UnlockUrl(student.Id), adminSession.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("account_not_locked"));
        var record = (await AuditRowsAsync(student.Id)).Single();
        Assert.That(record.Outcome, Is.EqualTo("Failure"));
        Assert.That(record.ActorId, Is.EqualTo(admin.Id));
        Assert.That(record.Details, Does.Contain("not_locked"));
    }

    [Test]
    public async Task ExpiredLockout_Returns409_CounterUnchanged_AndFailureIsAudited()
    {
        // Failures after an expired lockout keep the old lockout_end and count up, so this state is real.
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        await SetLockoutAsync(student.Id, TimeSpan.FromMinutes(-1), failedLogins: 2);
        var expiredAt = (await UserTestData.GetAsync(Factory, student.Id))!.LockoutEnd;
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);

        var response = await SendAsync(UnlockUrl(student.Id), adminSession.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("account_not_locked"));
        var stored = (await UserTestData.GetAsync(Factory, student.Id))!;
        Assert.That(stored.FailedLoginCount, Is.EqualTo(2), "the counter is not reset");
        Assert.That(stored.LockoutEnd, Is.EqualTo(expiredAt));
        var record = (await AuditRowsAsync(student.Id)).Single();
        Assert.That(record.Outcome, Is.EqualTo("Failure"));
        Assert.That(record.ActorId, Is.EqualTo(admin.Id));
        Assert.That(record.Details, Does.Contain("not_locked"));
    }

    [Test]
    public async Task UnknownAccount_Returns404_AndFailureIsAudited()
    {
        var (admin, _) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var unknown = Guid.NewGuid();

        var response = await SendAsync(UnlockUrl(unknown), adminSession.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("account_not_found"));
        var record = (await AuditRowsAsync(unknown)).Single();
        Assert.That(record.Outcome, Is.EqualTo("Failure"));
        Assert.That(record.Details, Does.Contain("account_not_found"));
    }

    [Test]
    public async Task Admin_CannotUnlockOwnAccount_Returns409_LockoutStays()
    {
        var (admin, _) = await ArrangeAdminAndStudentAsync();
        await LockAsync(admin.Id);
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);

        var response = await SendAsync(UnlockUrl(admin.Id), adminSession.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("self_change_not_allowed"));
        Assert.That((await UserTestData.GetAsync(Factory, admin.Id))!.LockoutEnd, Is.Not.Null);
        var record = (await AuditRowsAsync(admin.Id)).Single();
        Assert.That(record.Outcome, Is.EqualTo("Failure"));
        Assert.That(record.ActorId, Is.EqualTo(admin.Id));
        Assert.That(record.Details, Does.Contain("self_change"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task AuditFailure_RollsBackTheUnlockAndTheAuditRow(int failurePoint)
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        await LockAsync(student.Id, failedLogins: 2);
        var lockedUntil = (await UserTestData.GetAsync(Factory, student.Id))!.LockoutEnd;
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);

        using var failing = Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITransactionalAuditWriter>();
            services.AddSingleton<ITransactionalAuditWriter>(new UnavailableAuditWriter(failurePoint));
        }));
        using var client = failing.CreateClient();

        var response = await SendAsync(UnlockUrl(student.Id), adminSession.RawToken, client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var stored = (await UserTestData.GetAsync(Factory, student.Id))!;
        Assert.That(stored.LockoutEnd, Is.EqualTo(lockedUntil));
        Assert.That(stored.FailedLoginCount, Is.EqualTo(2));
        Assert.That(await AuditRowsAsync(student.Id), Is.Empty);
    }

    [Test]
    public async Task AfterUnlock_AFullRunOfFailuresIsNeededToLockAgain()
    {
        var maxAttempts = Factory.Services.GetRequiredService<IOptions<IdentityLockoutOptions>>().Value.MaxFailedAccessAttempts;
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        // Defence in depth: a real lockout already restarts the counter at 0 (both lockout paths do), so login
        // never leaves this state. The arrangement starts from the worst case anyway: if the unlock did not reset
        // the counter itself, a single failure after it would lock the account again.
        await LockAsync(student.Id, failedLogins: maxAttempts - 1);
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);

        Assert.That((await SendAsync(UnlockUrl(student.Id), adminSession.RawToken)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        for (var i = 0; i < maxAttempts - 1; i++)
            await LoginAsync(student.Email, WrongPassword);

        var afterAlmostFullRun = (await UserTestData.GetAsync(Factory, student.Id))!;
        Assert.That(afterAlmostFullRun.LockoutEnd, Is.Null, "one failure short of the limit must not lock");
        Assert.That(afterAlmostFullRun.FailedLoginCount, Is.EqualTo(maxAttempts - 1));

        await LoginAsync(student.Email, WrongPassword);

        Assert.That((await UserTestData.GetAsync(Factory, student.Id))!.LockoutEnd, Is.GreaterThan(DateTimeOffset.UtcNow));
    }

    // A lockout is separate from the status: ending it reopens neither a blocked nor an archived account.
    [TestCase(AccountStatus.Blocked)]
    [TestCase(AccountStatus.Archived)]
    public async Task InactiveAccount_IsUnlocked_StatusAndSessionsStay_LoginIsStillRefused(AccountStatus status)
    {
        var (admin, _) = await ArrangeAdminAndStudentAsync();
        var inactive = await UserTestData.AddAsync(Factory, "inactive@example.com", Password, status);
        var revoked = await SessionTestData.CreateAsync(Factory, inactive.Id);
        await RevokeSessionsAsync(inactive.Id);
        var revokedAt = (await SessionTestData.GetAsync(Factory, revoked.Session.Id))!.RevokedAt;
        Assert.That(revokedAt, Is.Not.Null);
        // Blocking revokes every session, so an open one is arranged: an unlock that revoked sessions would show here.
        var open = await SessionTestData.CreateAsync(Factory, inactive.Id);
        await LockAsync(inactive.Id, failedLogins: 2);
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);

        var response = await SendAsync(UnlockUrl(inactive.Id), adminSession.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadFromJsonAsync<UnlockResponse>(), Is.EqualTo(new UnlockResponse(inactive.Id, true)));
        var stored = (await UserTestData.GetAsync(Factory, inactive.Id))!;
        Assert.That(stored.LockoutEnd, Is.Null);
        Assert.That(stored.FailedLoginCount, Is.Zero);
        Assert.That(stored.Status, Is.EqualTo(status), "an unlock does not change the status");
        Assert.That((await SessionTestData.GetAsync(Factory, open.Session.Id))!.RevokedAt, Is.Null, "no session is revoked");
        Assert.That((await SessionTestData.GetAsync(Factory, revoked.Session.Id))!.RevokedAt, Is.EqualTo(revokedAt),
            "a revoked session stays revoked");

        // The lockout is gone and the password is correct, so only the status refuses this login.
        var login = await LoginAsync(inactive.Email, Password);
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await login.Content.ReadAsStringAsync(), Does.Contain("invalid_credentials"));
        Assert.That(login.Headers.Contains("Set-Cookie"), Is.False, "no session is issued");

        Assert.That((await AuditRowsAsync(inactive.Id)).Single().Outcome, Is.EqualTo("Success"));
    }

    private sealed record UnlockResponse(Guid Id, bool Unlocked);

    private sealed class UnavailableAuditWriter(int failurePoint) : ITransactionalAuditWriter
    {
        public async ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
            DbTransaction transaction, CancellationToken cancellationToken = default)
        {
            if (failurePoint == 0)
                return AuditWriteResult.Unavailable();

            var result = await new PostgresTransactionalAuditWriter(TimeProvider.System)
                .WriteAsync(draft, context, transaction, cancellationToken);
            Assert.That(result.IsRecorded, Is.True);
            await using var check = transaction.Connection!.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT count(*) FROM audit.audit_events WHERE resource_id = @target";
            var target = check.CreateParameter();
            target.ParameterName = "target";
            target.Value = draft.Resource!.Id;
            check.Parameters.Add(target);
            Assert.That(Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)), Is.EqualTo(1));

            if (failurePoint == 2)
            {
                await using var failure = transaction.Connection.CreateCommand();
                failure.Transaction = transaction;
                failure.CommandText = "SELECT 1 / 0";
                await failure.ExecuteNonQueryAsync(cancellationToken);
            }

            return AuditWriteResult.Unavailable();
        }
    }

    /// <summary>
    /// Locks the account for 15 minutes directly in the table login reads. A real lockout leaves the counter at 0;
    /// a non-zero <paramref name="failedLogins"/> is an arranged worst case that shows what the unlock resets.
    /// </summary>
    private static Task LockAsync(Guid accountId, int failedLogins = 0) =>
        SetLockoutAsync(accountId, TimeSpan.FromMinutes(15), failedLogins);

    /// <summary>Sets lockout_end relative to the database clock; a negative offset gives an expired lockout.</summary>
    private static async Task SetLockoutAsync(Guid accountId, TimeSpan fromNow, int failedLogins)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            UPDATE identity.accounts
            SET lockout_end = now() + @fromNow, failed_login_count = @failures
            WHERE id = @id
            """, connection);
        command.Parameters.AddWithValue("id", accountId);
        command.Parameters.AddWithValue("fromNow", fromNow);
        command.Parameters.AddWithValue("failures", failedLogins);
        Assert.That(await command.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }

    private static async Task RevokeSessionsAsync(Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        Assert.That(await new SessionManager(db).RevokeAllForUser(userId, "test"), Is.EqualTo(1));
    }

    private static string UnlockUrl(Guid accountId) => $"/api/admin/accounts/{accountId}/unlock";

    private static async Task<(UserAccount Admin, UserAccount Student)> ArrangeAdminAndStudentAsync()
    {
        var admin = new UserAccount(AdminId, "admin@example.com", AccountStatus.Active, AccountRole.Administrator);
        await InsertAsync(admin);
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        return (admin, student);
    }

    private static Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });

    private static async Task<HttpResponseMessage> SendAsync(string url, string token, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        return await (client ?? Client).SendAsync(request);
    }

    private sealed record AuditRow(string Outcome, Guid? ActorId, string Details);

    private static async Task<List<AuditRow>> AuditRowsAsync(Guid accountId)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT outcome, actor_id, details::text FROM audit.audit_events
            WHERE event_type = 'account.unlocked' AND resource_id = @id
            """, connection);
        command.Parameters.AddWithValue("id", accountId.ToString("D"));
        return await ReadRowsAsync(command);
    }

    private static async Task<List<AuditRow>> AccessDeniedRowsAsync(Guid actorId)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT outcome, actor_id, details::text FROM audit.audit_events
            WHERE event_type = 'access.denied' AND actor_id = @id
            """, connection);
        command.Parameters.AddWithValue("id", actorId);
        return await ReadRowsAsync(command);
    }

    private static async Task<List<AuditRow>> ReadRowsAsync(NpgsqlCommand command)
    {
        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new AuditRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2)));
        return rows;
    }
}
