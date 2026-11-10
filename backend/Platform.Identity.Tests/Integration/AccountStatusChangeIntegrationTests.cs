using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Platform.Api.Enums.Identity;
using Platform.Api.Interfaces.Identity;
using Platform.Api.Models.Identity;
using Platform.Api.Services.Identity;
using Platform.Audit.Composition;
using Platform.Audit.Contracts;
using Platform.Identity.Entities;
using Platform.Identity.Enums;
using Platform.Identity.Persistence;
using Platform.Identity.Tests.Helpers;

namespace Platform.Identity.Tests.Integration;

/// <summary>
/// IDN-03 / UC-05 / SEC-03 against a real PostgreSQL database: the status written by the admin endpoint is the
/// status login reads, sessions are revoked in the same transaction, and the Audit record is atomic with both.
/// </summary>
[Category("IDN-03")]
public class AccountStatusChangeIntegrationTests : IntegrationTestBase
{
    // Must be an id in Stub:AdminIds of appsettings.Development.json.
    private static readonly Guid AdminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Password = "Password123!";

    [OneTimeSetUp]
    public async Task MigrateAuditSchema() =>
        await AuditPersistenceExtensions.MigrateAuditDatabaseAsync(TestDatabaseFixture.ConnectionString);

    [Test]
    public async Task Block_RevokesActiveSession_RefusesNextRequestAndLogin_AndRecordsAudit()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);
        Assert.That((await SendAsync(HttpMethod.Get, "/api/auth/me", studentSession.RawToken)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        var response = await SendAsync(HttpMethod.Put, StatusUrl(student.Id), adminSession.RawToken, new { status = "BLOCKED" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await UserTestData.GetAsync(Factory, student.Id))!.Status, Is.EqualTo(AccountStatus.Blocked));
        Assert.That(await RevokedAtAsync(studentSession.Session.Id), Is.Not.Null);
        Assert.That((await SendAsync(HttpMethod.Get, "/api/auth/me", studentSession.RawToken)).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
        var login = await Client.PostAsJsonAsync("/api/auth/login", new { Email = student.Email, Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        var record = (await AuditRowsAsync(student.Id)).Single();
        Assert.That(record.Outcome, Is.EqualTo("Success"));
        Assert.That(record.ActorId, Is.EqualTo(admin.Id));
        Assert.That(record.Details, Does.Contain("blocked").And.Not.Contain(studentSession.RawToken));
    }

    [Test]
    public async Task Archive_RevokesAllSessionsOfTheAccount()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var first = await SessionTestData.CreateAsync(Factory, student.Id);
        var second = await SessionTestData.CreateAsync(Factory, student.Id);

        var response = await SendAsync(HttpMethod.Put, StatusUrl(student.Id), adminSession.RawToken, new { status = "archived" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await RevokedAtAsync(first.Session.Id), Is.Not.Null);
        Assert.That(await RevokedAtAsync(second.Session.Id), Is.Not.Null);
    }

    [Test]
    public async Task AuditUnavailable_RollsBackStatusAndSessionRevocation()
    {
        var (admin, student) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        using var failing = Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITransactionalAuditWriter>();
            services.AddSingleton<ITransactionalAuditWriter, UnavailableAuditWriter>();
        }));
        using var client = failing.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, StatusUrl(student.Id))
        {
            Content = JsonContent.Create(new { status = "BLOCKED" })
        };
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={adminSession.RawToken}");

        var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await UserTestData.GetAsync(Factory, student.Id))!.Status, Is.EqualTo(AccountStatus.Active));
        Assert.That(await RevokedAtAsync(studentSession.Session.Id), Is.Null);
        Assert.That(await AuditRowsAsync(student.Id), Is.Empty);
        Assert.That((await SendAsync(HttpMethod.Get, "/api/auth/me", studentSession.RawToken)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Student_CannotChangeStatus_NothingChanges_DenialIsAudited()
    {
        var (_, student) = await ArrangeAdminAndStudentAsync();
        var other = await UserTestData.AddAsync(Factory, "other@example.com", Password);
        var studentSession = await SessionTestData.CreateAsync(Factory, student.Id);

        var response = await SendAsync(HttpMethod.Put, StatusUrl(other.Id), studentSession.RawToken, new { status = "BLOCKED" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await UserTestData.GetAsync(Factory, other.Id))!.Status, Is.EqualTo(AccountStatus.Active));
        var record = (await AuditRowsAsync(other.Id)).Single();
        Assert.That(record.Outcome, Is.EqualTo("Denied"));
        Assert.That(record.ActorId, Is.EqualTo(student.Id));
    }

    [Test]
    public async Task SessionOfAccountBlockedBehindTheRevocation_IsStillRefused()
    {
        // Defense in depth: even a session row that exists for a blocked account (created without the issuing
        // check) is refused on use. Issuing such a session is refused too, see the tests below.
        var user = await UserTestData.AddAsync(Factory, "raced@example.com", Password, AccountStatus.Blocked);
        var session = await SessionTestData.CreateAsync(Factory, user.Id);

        var response = await SendAsync(HttpMethod.Get, "/api/auth/me", session.RawToken);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task IssuingASession_ForAnActiveAccount_StoresOneThatWorks()
    {
        var student = await UserTestData.AddAsync(Factory, "active@example.com", Password);

        var issued = await IssueSessionAsync(student.Id);

        Assert.That(issued, Is.Not.Null);
        Assert.That((await SendAsync(HttpMethod.Get, "/api/auth/me", issued!.Token)).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task LoginThatStartedBeforeTheBlock_IsRefusedAtIssue_SoReactivationCannotRevive_ItsToken()
    {
        // The status change holds the account lock (as ChangeAccountStatus does) while a login that already
        // passed its status check tries to store a session. The login waits; once the block commits it must
        // see Blocked and store nothing, so there is no token a later reactivation could bring back to life.
        var student = await UserTestData.AddAsync(Factory, "racer@example.com", Password);

        using var statusScope = Factory.Services.CreateScope();
        var statusStore = new EfAccountStatusStore(statusScope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>());
        await using var unitOfWork = await statusStore.BeginAsync();
        var account = await unitOfWork.FindForUpdateAsync(student.Id);
        await unitOfWork.ApplyAsync(account!, AccountStatus.Blocked);

        var login = IssueSessionAsync(student.Id);
        await Task.Delay(500);
        Assert.That(login.IsCompleted, Is.False, "the login must wait for the status change that holds the lock");

        await unitOfWork.CommitAsync();

        await Assert.ThrowsAsync<AccountNotActiveException>(async () => await login);
        Assert.That(await SessionCountAsync(student.Id), Is.Zero);

        await SetStatusAsync(student.Id, AccountStatus.Active);
        Assert.That(await SessionCountAsync(student.Id), Is.Zero, "nothing is left that reactivation could revive");
    }

    [Test]
    public async Task LoginsRacingWithABlock_NeverLeaveATokenThatWorksAfterReactivation()
    {
        var (admin, _) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var student = await UserTestData.AddAsync(Factory, $"racer{attempt}@example.com", Password);

            var login = Task.Run(async () =>
            {
                try { return (await IssueSessionAsync(student.Id))?.Token; }
                catch (AccountNotActiveException) { return null; }
            });
            var block = Task.Run(() => SendAsync(HttpMethod.Put, StatusUrl(student.Id), adminSession.RawToken,
                new { status = "BLOCKED" }));

            await Task.WhenAll(login, block);
            var token = await login;

            Assert.That((await block).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            await SetStatusAsync(student.Id, AccountStatus.Active);

            // Whichever came first: either the block revoked the new session or the login was refused.
            if (token is not null)
                Assert.That((await SendAsync(HttpMethod.Get, "/api/auth/me", token)).StatusCode,
                    Is.EqualTo(HttpStatusCode.Unauthorized), $"attempt {attempt}: a token outlived the block");
        }
    }

    [Test]
    public async Task UnknownAccount_ReturnsNotFound_AndLeavesNoOtherRows()
    {
        var (admin, _) = await ArrangeAdminAndStudentAsync();
        var adminSession = await SessionTestData.CreateAsync(Factory, admin.Id);
        var unknown = Guid.NewGuid();

        var response = await SendAsync(HttpMethod.Put, StatusUrl(unknown), adminSession.RawToken, new { status = "BLOCKED" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await AuditRowsAsync(unknown)).Single().Outcome, Is.EqualTo("Failure"));
    }

    private sealed class UnavailableAuditWriter : ITransactionalAuditWriter
    {
        public ValueTask<AuditWriteResult> WriteAsync(AuditEventDraft draft, AuditContext context,
            DbTransaction transaction, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(AuditWriteResult.Unavailable());
    }

    private static async Task<IssuedSession?> IssueSessionAsync(Guid accountId)
    {
        // Own scope = own DbContext and connection, like a separate login request.
        using var scope = Factory.Services.CreateScope();
        var creator = scope.ServiceProvider.GetRequiredService<ISessionCreator>();
        return await creator.CreateAsync(accountId, AuthenticationLevel.Active);
    }

    private static async Task<int> SessionCountAsync(Guid accountId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Sessions.AsNoTracking().CountAsync(session => session.UserId == accountId);
    }

    private static async Task SetStatusAsync(Guid accountId, AccountStatus status)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        var account = await db.Accounts.SingleAsync(candidate => candidate.Id == accountId);
        account.Status = status;
        await db.SaveChangesAsync();
    }

    private static string StatusUrl(Guid accountId) => $"/api/admin/accounts/{accountId}/status";

    private static async Task<(UserAccount Admin, UserAccount Student)> ArrangeAdminAndStudentAsync()
    {
        var admin = new UserAccount(AdminId, "admin@example.com", AccountStatus.Active, AccountRole.Administrator);
        await InsertAsync(admin);
        var student = await UserTestData.AddAsync(Factory, "student@example.com", Password);
        return (admin, student);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", $"{SessionCookie.Name}={token}");
        if (body is not null) request.Content = JsonContent.Create(body);
        return await Client.SendAsync(request);
    }

    private static async Task<DateTimeOffset?> RevokedAtAsync(Guid sessionId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformIdentityDbContext>();
        return await db.Sessions.AsNoTracking()
            .Where(session => session.Id == sessionId)
            .Select(session => session.RevokedAt)
            .SingleAsync();
    }

    private sealed record AuditRow(string Outcome, Guid? ActorId, string Details);

    private static async Task<List<AuditRow>> AuditRowsAsync(Guid accountId)
    {
        await using var connection = new NpgsqlConnection(TestDatabaseFixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT outcome, actor_id, details::text FROM audit.audit_events
            WHERE event_type = 'account.status.changed' AND resource_id = @id
            """, connection);
        command.Parameters.AddWithValue("id", accountId.ToString("D"));

        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new AuditRow(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2)));
        return rows;
    }
}
